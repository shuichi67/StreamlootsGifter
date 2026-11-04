using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamlootsManager;

public sealed class AppConfig
{
    // Preserve fields added by newer/older app versions so switching between
    // the Python and .NET builds does not silently erase settings.
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }
    [JsonPropertyName("mappings")] public Dictionary<string, string> Mappings { get; set; } = new();
    [JsonPropertyName("reward_gift_options")] public Dictionary<string, RewardGiftOption> RewardGiftOptions { get; set; } = new();
    [JsonPropertyName("section_order")] public List<string> SectionOrder { get; set; } = ["bridge", "rewards", "free_chest", "free_chest_history", "activity"];
    [JsonPropertyName("dashboard_sections")] public List<string> DashboardSections { get; set; } = ["bridge", "rewards", "free_chest", "free_chest_history", "activity"];
    [JsonPropertyName("dashboard_widget_sizes")] public Dictionary<string, DashboardWidgetSize> DashboardWidgetSizes { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("dashboard_layout_locked")] public bool DashboardLayoutLocked { get; set; }
    [JsonPropertyName("dashboard_column_ratio")] public double DashboardColumnRatio { get; set; } = 0.5;
    [JsonPropertyName("dashboard_layout")] public DashboardLayoutNode? DashboardLayout { get; set; }
    [JsonPropertyName("free_chest_rules")] public List<FreeChestRule> FreeChestRules { get; set; } = [];
    [JsonPropertyName("streamloots_card_rules")] public List<StreamlootsCardRule> StreamlootsCardRules { get; set; } = [];
    [JsonPropertyName("viewer_aliases")] public List<ViewerAliasEntry> ViewerAliases { get; set; } = [];
    [JsonPropertyName("viewer_aliases_collapsed")] public bool ViewerAliasesCollapsed { get; set; }
    [JsonPropertyName("collapsed_sections")] public List<string> CollapsedSections { get; set; } = [];
    [JsonPropertyName("collapsed_rewards")] public List<string> CollapsedRewardIds { get; set; } = [];
    [JsonPropertyName("reward_collapse_initialized")] public bool RewardCollapseInitialized { get; set; }
    [JsonPropertyName("hidden_rewards")] public List<string> HiddenRewardIds { get; set; } = [];
    [JsonPropertyName("free_chest_history_filter")] public string HistoryFilter { get; set; } = "Username not found";
    [JsonPropertyName("claim_history_user_filter")] public string ClaimHistoryUserFilter { get; set; } = "";
    [JsonPropertyName("claim_history_source_filter")] public string ClaimHistorySourceFilter { get; set; } = "All";
    [JsonPropertyName("activity_user_filter")] public string ActivityUserFilter { get; set; } = "";
    [JsonPropertyName("activity_source_filter")] public string ActivitySourceFilter { get; set; } = "All";
    [JsonPropertyName("activity_status_filters")] public List<string> ActivityStatusFilters { get; set; } = ["Gifted", "Username not found", "Needs attention", "Queued", "Sending"];
    [JsonPropertyName("require_streamloots_username")] public bool RequireStreamlootsUsername { get; set; } = true;
    [JsonPropertyName("introduction_dont_ask_again")] public bool IntroductionDontAskAgain { get; set; }
    [JsonPropertyName("introduction_completed")] public bool IntroductionCompleted { get; set; }
    [JsonPropertyName("settings_collapsed_sections")] public List<string> SettingsCollapsedSections { get; set; } = [];
    [JsonPropertyName("extension_browser")] public string ExtensionBrowser { get; set; } = "Chrome";
    [JsonPropertyName("streamloots_language")] public string StreamlootsLanguage { get; set; } = "English";
    [JsonPropertyName("update_repository")] public string UpdateRepository { get; set; } = "shuichi67/StreamlootsGifter";
    [JsonPropertyName("check_updates_on_startup")] public bool CheckUpdatesOnStartup { get; set; }
    [JsonPropertyName("free_chest_duration_minutes")] public int FreeChestDurationMinutes { get; set; } = 5;
    [JsonPropertyName("theme_preset")] public string ThemePreset { get; set; } = "Midnight";
    [JsonPropertyName("theme_background_start")] public string ThemeBackgroundStart { get; set; } = "#0B1020";
    [JsonPropertyName("theme_background_end")] public string ThemeBackgroundEnd { get; set; } = "#111A30";
    [JsonPropertyName("theme_panel")] public string ThemePanel { get; set; } = "#151D30";
    [JsonPropertyName("theme_input")] public string ThemeInput { get; set; } = "#0E1525";
    [JsonPropertyName("theme_button")] public string ThemeButton { get; set; } = "#25324A";
    [JsonPropertyName("theme_accent")] public string ThemeAccent { get; set; } = "#5D6EFF";
    [JsonPropertyName("theme_text")] public string ThemeText { get; set; } = "#F4F6FF";
    [JsonPropertyName("theme_muted")] public string ThemeMuted { get; set; } = "#9AA7C0";
}

public sealed class DashboardLayoutNode
{
    [JsonPropertyName("widget_key")] public string? WidgetKey { get; set; }
    [JsonPropertyName("split_axis")] public string SplitAxis { get; set; } = "columns";
    [JsonPropertyName("ratio")] public double Ratio { get; set; } = 0.5;
    [JsonPropertyName("first")] public DashboardLayoutNode? First { get; set; }
    [JsonPropertyName("second")] public DashboardLayoutNode? Second { get; set; }
    [JsonIgnore] public bool IsLeaf => !string.IsNullOrWhiteSpace(WidgetKey);
}

public sealed class DashboardWidgetSize
{
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
}

public sealed class RewardGiftOption
{
    [JsonPropertyName("packs")] public int Packs { get; set; } = 1;
    [JsonPropertyName("cards")] public int Cards { get; set; } = 3;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public sealed class FreeChestRule
{
    [JsonPropertyName("keyword")] public string Keyword { get; set; } = "";
    [JsonPropertyName("collection")] public string Collection { get; set; } = "";
    [JsonPropertyName("packs")] public int Packs { get; set; } = 1;
    [JsonPropertyName("cards")] public int Cards { get; set; } = 3;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public sealed class StreamlootsCardRule
{
    [JsonPropertyName("card_name")] public string CardName { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "free_gift";
    [JsonPropertyName("collection")] public string Collection { get; set; } = "";
    [JsonPropertyName("chests")] public int Chests { get; set; } = 1;
    [JsonPropertyName("cards")] public int Cards { get; set; } = 3;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public sealed class ViewerAliasEntry
{
    [JsonPropertyName("streamloots_username")] public string StreamlootsUsername { get; set; } = "";
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = [];
}

public sealed record StreamlootsCardFeedEvent(string EventId, string CardName, string Redeemer, string Input, string PlayedAt = "");
public sealed record StreamlootsCardFeedResult(bool Accepted, string Message);

public sealed class QueueJob
{
    [JsonPropertyName("job_id")] public string JobId { get; set; } = "";
    [JsonPropertyName("redemption_id")] public string RedemptionId { get; set; } = "";
    [JsonPropertyName("recipient")] public string Recipient { get; set; } = "";
    [JsonPropertyName("viewer")] public string Viewer { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "channel_points";
    [JsonPropertyName("keyword")] public string Keyword { get; set; } = "";
    [JsonPropertyName("collection")] public string Collection { get; set; } = "";
    [JsonPropertyName("packs")] public int Packs { get; set; } = 1;
    [JsonPropertyName("cards")] public int Cards { get; set; } = 3;
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("played_at")] public string PlayedAt { get; set; } = "";
}

public sealed record GiftRequest(
    string RedemptionId,
    string Viewer,
    string Recipient,
    string Collection,
    string RewardName,
    string Source = "channel_points",
    string Keyword = "",
    int Packs = 1,
    int Cards = 3,
    string PlayedAt = "");

public sealed record GiftResult(string Status, string Detail, QueueJob? Job = null);
public sealed record TwitchUser(string Id, string Login, string DisplayName, string ProfileImageUrl);
public sealed record TwitchReward(string Id, string Title, int Cost, string Prompt, bool IsEnabled);
public sealed record RedemptionEvent(string Id, string UserName, string UserInput, string RewardId, string RewardTitle, string UserLogin = "");
public sealed record ChatMessageEvent(string ChatterId, string Login, string DisplayName, string Text);

public sealed class GiftActivity
{
    public string JobId { get; set; } = "";
    public string Viewer { get; init; } = "";
    public string Reward { get; init; } = "";
    public string Recipient { get; init; } = "";
    public string Collection { get; init; } = "";
    public string Source { get; init; } = "channel_points";
    public string Keyword { get; init; } = "";
    public int Packs { get; init; } = 1;
    public int Cards { get; init; } = 3;
    public string Status { get; set; } = "Detected";
    public string Detail { get; set; } = "";
}

public static class AppPaths
{
    public static readonly string Root = FindRoot();
    public static readonly string Config = Path.Combine(Root, "config.json");
    public static readonly string Queue = Path.Combine(Root, "gift_queue.json");
    public static readonly string BridgeKey = Path.Combine(Root, ".streamloots_bridge_key");
    public static readonly string EnvironmentFile = Path.Combine(Root, ".env");

    private static string FindRoot()
    {
        // Resolve data beside the running app first. Otherwise a copied build
        // launched from a terminal may walk into the source checkout and reuse
        // that install's .env, config, gift history, and bridge key.
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, ".env")) ||
                    File.Exists(Path.Combine(current.FullName, "main_compact.py")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        return AppContext.BaseDirectory;
    }
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;

    public ConfigStore(string path) => _path = path;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions) ?? new AppConfig();
            config.AdditionalSettings?.Remove("streamerbot_host");
            config.AdditionalSettings?.Remove("streamerbot_port");
            config.AdditionalSettings?.Remove("streamerbot_endpoint");
            config.SectionOrder = Normalize(config.SectionOrder);
            config.DashboardSections = config.DashboardSections.Where(IsSection).Distinct().ToList();
            config.DashboardWidgetSizes ??= new(StringComparer.Ordinal);
            config.DashboardWidgetSizes = config.DashboardWidgetSizes
                .Where(pair => IsSection(pair.Key) && pair.Value is not null && double.IsFinite(pair.Value.Width) && double.IsFinite(pair.Value.Height) && pair.Value.Width is >= 320 and <= 2400 && pair.Value.Height is >= 220 and <= 2400)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            config.DashboardColumnRatio = double.IsFinite(config.DashboardColumnRatio)
                ? Math.Clamp(config.DashboardColumnRatio, 0.25, 0.75) : 0.5;
            config.CollapsedSections = config.CollapsedSections.Where(IsSection).Distinct().ToList();
            config.CollapsedRewardIds = (config.CollapsedRewardIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            config.HiddenRewardIds = (config.HiddenRewardIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            config.RewardGiftOptions ??= new();
            config.RewardGiftOptions = config.RewardGiftOptions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null && pair.Value.Packs is >= 1 and <= 50 && pair.Value.Cards is >= 1 and <= 3)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            config.FreeChestDurationMinutes = Math.Clamp(config.FreeChestDurationMinutes, 1, 60);
            config.FreeChestRules = config.FreeChestRules
                .Where(r => !string.IsNullOrWhiteSpace(r.Keyword) && !string.IsNullOrWhiteSpace(r.Collection) && r.Packs is >= 1 and <= 50 && r.Cards is >= 1 and <= 3)
                .GroupBy(r => r.Keyword.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            config.StreamlootsCardRules = (config.StreamlootsCardRules ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.CardName) && !string.IsNullOrWhiteSpace(r.Collection) &&
                    r.Action is "free_gift" or "gift_user" && r.Chests is >= 1 and <= 50 && r.Cards is >= 1 and <= 3)
                .GroupBy(r => r.CardName.Trim(), StringComparer.Ordinal).Select(g => g.First()).ToList();
            config.ViewerAliases = (config.ViewerAliases ?? [])
                .Where(entry => entry is not null)
                .Select(entry =>
                {
                    entry.StreamlootsUsername = (entry.StreamlootsUsername ?? "").Trim();
                    entry.Aliases = (entry.Aliases ?? [])
                        .Select(alias => alias.Trim())
                        .Where(alias => alias.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return entry;
                }).ToList();
            config.SettingsCollapsedSections = (config.SettingsCollapsedSections ?? [])
                .Where(key => SettingsSectionKeys.Contains(key, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (config.StreamlootsLanguage is not ("English" or "Spanish")) config.StreamlootsLanguage = "English";
            if (!HistoryFilters.Contains(config.HistoryFilter)) config.HistoryFilter = "Username not found";
            if (!GiftSourceFilters.Contains(config.ClaimHistorySourceFilter)) config.ClaimHistorySourceFilter = "All";
            if (!GiftSourceFilters.Contains(config.ActivitySourceFilter)) config.ActivitySourceFilter = "All";
            var validActivityStatuses = new[] { "Gifted", "Username not found", "Needs attention", "Queued", "Sending" };
            config.ActivityStatusFilters = (config.ActivityStatusFilters ?? [])
                .Where(validActivityStatuses.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, _path, true);
    }

    public static readonly string[] Sections = ["bridge", "rewards", "free_chest", "free_chest_history", "activity", "streamloots_cards"];
    public static readonly string[] HistoryFilters = ["All", "Gifted", "Username not found", "Needs attention", "Queued", "Sending"];
    public static readonly string[] GiftSourceFilters = ["All", "Channel point redeem", "Free Chest Hour", "Streamloots card"];
    private static readonly string[] SettingsSectionKeys = ["twitch_redeems", "introduction", "viewer_aliases", "streamloots_language", "browser_extension", "application_updates", "appearance"];
    private static bool IsSection(string value) => Sections.Contains(value, StringComparer.Ordinal);
    private static List<string> Normalize(IEnumerable<string>? values)
    {
        var normalized = (values ?? []).Where(IsSection).Distinct().ToList();
        normalized.AddRange(Sections.Where(key => !normalized.Contains(key)));
        return normalized;
    }
}
