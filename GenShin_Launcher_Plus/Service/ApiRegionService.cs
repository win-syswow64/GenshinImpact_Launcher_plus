using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus.Service;

/// <summary>
/// Resolves which HoYoPlay artwork cluster matches the current public IP.
/// Metadata, news, version and package APIs deliberately do not use this
/// result; they are routed by the selected game server instead.
/// </summary>
public static class ApiRegionService
{
    public const string IpInfoEndpoint =
        "https://api.live.bilibili.com/xlive/web-room/v1/index/getIpInfo";

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly SemaphoreSlim ResolveLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static ApiRegion? _cachedRegion;
    private static DateTimeOffset _cacheExpiresAt;

    public static async ValueTask<ApiRegion> GetCurrentRegionAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedRegion is { } cached && _cacheExpiresAt > DateTimeOffset.UtcNow)
            return cached;

        await ResolveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedRegion is { } lockedCached && _cacheExpiresAt > DateTimeOffset.UtcNow)
                return lockedCached;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, IpInfoEndpoint);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 GenshinLauncherPlus/1.0");
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("country", out var countryElement))
            {
                throw new InvalidOperationException("IP information response is invalid.");
            }

            string country = countryElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(country))
                throw new InvalidOperationException("IP information response contains no country.");

            var region = new ApiRegion(country, IsChinaCountry(country), false);
            Cache(region);
            Logger.Info($"API region resolved: country={country}, cluster={(region.IsChina ? "mihoyo" : "hoyoverse")}", "Region");
            return region;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GetFallbackRegion("IP information request timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return GetFallbackRegion("IP information request failed: " + ex.Message);
        }
        finally
        {
            ResolveLock.Release();
        }
    }

    public static bool IsChinaCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return false;
        return country.Trim() is "中国" or "中国大陆" or "China" or "CN";
    }

    private static ApiRegion GetFallbackRegion(string reason)
    {
        if (_cachedRegion is { } stale)
        {
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            Logger.Warn($"{reason}; using cached country {stale.Country}.", "Region");
            return stale;
        }

        // Only an affirmative China response may select the miHoYo artwork
        // cluster. An unknown location therefore falls back to HoYoverse and
        // is retried after a short cache interval.
        var fallback = new ApiRegion("未知", false, true);
        _cachedRegion = fallback;
        _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        Logger.Warn(reason + "; temporarily using hoyoverse artwork API.", "Region");
        return fallback;
    }

    private static void Cache(ApiRegion region)
    {
        _cachedRegion = region;
        _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
    }
}

public readonly record struct ApiRegion(string Country, bool IsChina, bool IsFallback);
