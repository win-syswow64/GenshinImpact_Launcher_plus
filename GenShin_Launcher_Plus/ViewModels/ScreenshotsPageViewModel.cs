using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class ScreenshotsPageViewModel : ObservableObject
    {
        private const int ThumbnailDecodeWidth = 240;
        private const int BufferGroups = 10;

        private static readonly string[] SupportedExtensions =
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

        private readonly List<ScreenshotItem> _allItems = new();
        private readonly object _loadLock = new();
        private int _loadedRangeStart = -1;
        private int _loadedRangeEnd = -1;

        public ObservableCollection<ScreenshotDayGroup> Groups { get; } = new();

        private Visibility _isEmpty = Visibility.Collapsed;
        public Visibility IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

        private Visibility _isLoading = Visibility.Visible;
        public Visibility IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private string _backToolTip = string.Empty;
        public string BackToolTip { get => _backToolTip; set => SetProperty(ref _backToolTip, value); }

        private string _loadingText = string.Empty;
        public string LoadingText { get => _loadingText; set => SetProperty(ref _loadingText, value); }

        private string _emptyText = string.Empty;
        public string EmptyText { get => _emptyText; set => SetProperty(ref _emptyText, value); }

        private ICommand? _backCommand;
        public ICommand? BackCommand { get => _backCommand; set => SetProperty(ref _backCommand, value); }

        public int ItemCount => _allItems.Count;

        public async Task LoadScreenshotsAsync(string screenshotPath)
        {
            IsLoading = Visibility.Visible;
            IsEmpty = Visibility.Collapsed;
            Groups.Clear();
            _allItems.Clear();

            await Task.Run(() =>
            {
                if (!Directory.Exists(screenshotPath))
                {
                    IsEmpty = Visibility.Visible;
                    return;
                }

                var files = Directory.EnumerateFiles(screenshotPath)
                    .Where(f => SupportedExtensions.Contains(
                        Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (files.Count == 0)
                {
                    IsEmpty = Visibility.Visible;
                    return;
                }

                var items = files
                    .Select(f => new ScreenshotItem(f))
                    .OrderByDescending(s => s.SaveTime)
                    .ToList();

                _allItems.AddRange(items);
            });

            if (_allItems.Count > 0)
            {
                RebuildGroups();
            }

            IsLoading = Visibility.Collapsed;
        }

        private void RebuildGroups()
        {
            Groups.Clear();
            var grouped = _allItems
                .GroupBy(s => s.SaveTime.Date)
                .OrderByDescending(g => g.Key);

            foreach (var g in grouped)
            {
                var group = new ScreenshotDayGroup(g.Key, g.Count());
                foreach (var item in g)
                {
                    group.Items.Add(item);
                }
                Groups.Add(group);
            }
        }

        /// <summary>
        /// Update which day groups have their thumbnails loaded.
        /// centerGroupIndex: approximate center group from scroll position.
        /// </summary>
        public void UpdateVisibleRange(int centerGroupIndex, int totalGroups)
        {
            int start = Math.Max(0, centerGroupIndex - BufferGroups);
            int end = Math.Min(totalGroups - 1, centerGroupIndex + BufferGroups);

            if (start == _loadedRangeStart && end == _loadedRangeEnd) return;

            var prevStart = _loadedRangeStart;
            var prevEnd = _loadedRangeEnd;
            _loadedRangeStart = start;
            _loadedRangeEnd = end;

            // Unload groups outside new range
            if (prevStart >= 0)
            {
                for (int i = prevStart; i <= prevEnd; i++)
                {
                    if (i < start || i > end)
                    {
                        UnloadGroup(i);
                    }
                }
            }

            // Load groups inside new range
            Task.Run(() =>
            {
                for (int i = start; i <= end; i++)
                {
                    LoadGroup(i);
                }
            });
        }

        private void LoadGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= Groups.Count) return;
            var group = Groups[groupIndex];
            foreach (var item in group.Items)
            {
                lock (_loadLock)
                {
                    item.LoadThumbnail(ThumbnailDecodeWidth);
                }
            }
        }

        private void UnloadGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= Groups.Count) return;
            var group = Groups[groupIndex];
            foreach (var item in group.Items)
            {
                item.UnloadThumbnail();
            }
        }

        public void UnloadAll()
        {
            foreach (var item in _allItems)
            {
                item.UnloadThumbnail();
            }
            _loadedRangeStart = -1;
            _loadedRangeEnd = -1;
        }
    }
}