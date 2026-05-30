namespace GenShin_Launcher_Plus.Models
{
    /// <summary>
    /// Represents a game + server combination, modeled after Starward''s GameBiz.
    /// Each GameBiz is treated as an independent entity with its own install path.
    /// </summary>
    public record struct GameBiz
    {
        private string _value;
        public string Value => _value ?? "";

        public string Game => Value.Contains('_') ? Value[..Value.IndexOf('_')] : Value;
        public string Server => Value.Contains('_') ? Value[(Value.IndexOf('_') + 1)..] : "";

        public GameBiz(string? value) => _value = value ?? "";

        // Genshin
        public const string genshin_cn = "genshin_cn";
        public const string genshin_global = "genshin_global";
        public const string genshin_bilibili = "genshin_bilibili";

        // StarRail
        public const string starrail_cn = "starrail_cn";
        public const string starrail_global = "starrail_global";
        public const string starrail_bilibili = "starrail_bilibili";

        // ZZZ
        public const string zzz_cn = "zzz_cn";
        public const string zzz_global = "zzz_global";
        public const string zzz_bilibili = "zzz_bilibili";

        // Honkai3
        public const string honkai3_cn = "honkai3_cn";
        public const string honkai3_global = "honkai3_global";

        public bool IsKnown() => Value switch
        {
            genshin_cn or genshin_global or genshin_bilibili => true,
            starrail_cn or starrail_global or starrail_bilibili => true,
            zzz_cn or zzz_global or zzz_bilibili => true,
            honkai3_cn or honkai3_global => true,
            _ => false,
        };

        public bool IsChinaServer() => Server is "cn";
        public bool IsGlobalServer() => Server is "global";
        public bool IsBilibili() => Server is "bilibili";

        public override string ToString() => Value;

        public static implicit operator GameBiz(string? value) => new(value);
        public static implicit operator string(GameBiz value) => value.Value;
    }
}