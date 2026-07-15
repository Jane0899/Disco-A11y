using System.Text;
using System.Text.RegularExpressions;

namespace KeybindEditor;

/// <summary>
/// Reads/writes UserData/AccessibilityMod.cfg - the same file the mod's MelonPreferences
/// categories ("KeyBindings" and "AccessibilityMod") persist to. Regenerates the whole
/// file from known fields rather than doing a partial patch, since these two categories
/// are the entire contents of this file.
/// </summary>
public sealed class ModConfig
{
    public Dictionary<string, string> KeyBindings { get; } = new();

    public int DialogReadingMode { get; set; } // 0 = Disabled, 1 = Full, 2 = SpeakerOnly
    public bool OrbAnnouncements { get; set; } = true;
    public int OrbVolume { get; set; } = 80;

    /// <summary>Windows voice display name for orb text; empty = system default. Read by the TTS server.</summary>
    public string OrbVoice { get; set; } = "";
    /// <summary>
    /// Minutes before a repeated orb line or a re-entered area description may play again.
    /// 0 = never suppress. Shared by the orb throttle and the area-description throttle.
    /// </summary>
    public int RepeatSuppressionMinutes { get; set; } = 3;
    public bool SpeechInterrupt { get; set; } = false;
    public bool SpeakAudioCaptions { get; set; } = true;
    public bool DialogAutoAdvance { get; set; } = false;
    public bool AutoInteract { get; set; } = false;
    public bool ItemDescriptions { get; set; } = false;
    public bool SpeechLog { get; set; } = false;
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// Everything in the config this editor does not know about - unknown keys of
    /// [AccessibilityMod] and whole unknown sections (the mod's [Tutorial] flags, its
    /// waypoints) - kept verbatim, section by section, and written back on save.
    ///
    /// Saving used to write only the two sections the editor knows, silently deleting
    /// the rest: opening the configurator once replayed every tutorial tip and every
    /// first-visit area introduction the player had already heard.
    /// </summary>
    public Dictionary<string, List<KeyValuePair<string, string>>> PassedThrough { get; } = new();

    private void PassThrough(string section, string key, string value)
    {
        if (!PassedThrough.TryGetValue(section, out var entries))
        {
            entries = new List<KeyValuePair<string, string>>();
            PassedThrough[section] = entries;
        }
        entries.Add(new KeyValuePair<string, string>(key, value));
    }

    /// <summary>
    /// Actions that were not present in the loaded file and therefore fell back to
    /// their default binding - i.e. actions added by a mod update since the config
    /// was written. Empty when the file didn't exist at all (fresh config).
    /// </summary>
    public List<string> AddedActions { get; } = new();

    private readonly HashSet<string> actionsSeenInFile = new();

    public static ModConfig LoadOrDefault(string path)
    {
        var config = new ModConfig();
        foreach (var action in GameKeyCatalog.Actions)
        {
            config.KeyBindings[action.Name] = action.DefaultBinding;
        }

        if (!File.Exists(path))
        {
            return config;
        }

        string? currentSection = null;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var sectionMatch = Regex.Match(line, @"^\[(.+)\]$");
            if (sectionMatch.Success)
            {
                currentSection = sectionMatch.Groups[1].Value;
                continue;
            }

            // MelonPreferences sometimes writes a whole category as a TOML inline table
            // on a single line ("AccessibilityMod = { DialogReadingMode = 0, ... }")
            // instead of a [Section] block - both forms occur in real files. Splitting
            // on commas is safe for our values (ints, bools, and binding strings, none
            // of which can contain a comma).
            var inlineMatch = Regex.Match(line, @"^([A-Za-z0-9_]+)\s*=\s*\{(.*)\}\s*$");
            if (inlineMatch.Success)
            {
                var inlineSection = inlineMatch.Groups[1].Value;
                foreach (var pair in inlineMatch.Groups[2].Value.Split(','))
                {
                    var pairMatch = Regex.Match(pair.Trim(), @"^([A-Za-z0-9_]+)\s*=\s*(.+)$");
                    if (pairMatch.Success)
                    {
                        ApplyValue(config, inlineSection, pairMatch.Groups[1].Value, pairMatch.Groups[2].Value.Trim());
                    }
                }
                continue;
            }

            var kvMatch = Regex.Match(line, @"^([A-Za-z0-9_]+)\s*=\s*(.+)$");
            if (!kvMatch.Success || currentSection == null)
            {
                continue;
            }

            ApplyValue(config, currentSection, kvMatch.Groups[1].Value, kvMatch.Groups[2].Value.Trim());
        }

        foreach (var action in GameKeyCatalog.Actions)
        {
            if (!config.actionsSeenInFile.Contains(action.Name))
            {
                config.AddedActions.Add(action.Name);
            }
        }

        return config;
    }

    private static void ApplyValue(ModConfig config, string section, string key, string value)
    {
        if (section == "KeyBindings")
        {
            if (config.KeyBindings.ContainsKey(key))
            {
                config.KeyBindings[key] = Unquote(value);
                config.actionsSeenInFile.Add(key);
            }
            // A binding for an action this editor does not know (older/newer mod) is
            // dropped on purpose: the [KeyBindings] section is rewritten from the catalog.
            return;
        }

        if (section != "AccessibilityMod")
        {
            // A whole section we do not manage ([Tutorial], waypoints, ...) - keep it.
            config.PassThrough(section, key, value);
            return;
        }

        {
            switch (key)
            {
                case "DialogReadingMode":
                    if (int.TryParse(value, out var mode)) config.DialogReadingMode = mode;
                    break;
                case "OrbAnnouncements":
                    config.OrbAnnouncements = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "OrbVolume":
                    if (int.TryParse(value, out var vol)) config.OrbVolume = Math.Max(0, Math.Min(100, vol));
                    break;
                case "OrbVoice":
                    config.OrbVoice = Unquote(value);
                    break;
                case "RepeatSuppressionMinutes":
                    if (int.TryParse(value, out var mins)) config.RepeatSuppressionMinutes = Math.Max(0, Math.Min(60, mins));
                    break;
                case "SpeechInterrupt":
                    config.SpeechInterrupt = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SpeakAudioCaptions":
                    config.SpeakAudioCaptions = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "DialogAutoAdvance":
                    config.DialogAutoAdvance = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SpeechLog":
                    config.SpeechLog = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                case "DebugMode":
                    config.DebugMode = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                case "AutoInteract":
                    config.AutoInteract = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                case "ItemDescriptions":
                    config.ItemDescriptions = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                // Settings this editor does not show (the mod's language, the list of area
                // introductions already seen) are carried over untouched.
                default:
                    config.PassThrough("AccessibilityMod", key, value);
                    break;
            }
        }
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[KeyBindings]");
        foreach (var action in GameKeyCatalog.Actions)
        {
            sb.AppendLine($"{action.Name} = \"{KeyBindings[action.Name]}\"");
        }

        sb.AppendLine();
        sb.AppendLine("[AccessibilityMod]");
        sb.AppendLine($"DialogReadingMode = {DialogReadingMode}");
        sb.AppendLine($"OrbAnnouncements = {(OrbAnnouncements ? "true" : "false")}");
        sb.AppendLine($"OrbVolume = {OrbVolume}");
        sb.AppendLine($"OrbVoice = \"{OrbVoice}\"");
        sb.AppendLine($"RepeatSuppressionMinutes = {RepeatSuppressionMinutes}");
        sb.AppendLine($"SpeechInterrupt = {(SpeechInterrupt ? "true" : "false")}");
        sb.AppendLine($"SpeakAudioCaptions = {(SpeakAudioCaptions ? "true" : "false")}");
        sb.AppendLine($"DialogAutoAdvance = {(DialogAutoAdvance ? "true" : "false")}");
        sb.AppendLine($"AutoInteract = {(AutoInteract ? "true" : "false")}");
        sb.AppendLine($"ItemDescriptions = {(ItemDescriptions ? "true" : "false")}");
        sb.AppendLine($"SpeechLog = {(SpeechLog ? "true" : "false")}");
        sb.AppendLine($"DebugMode = {(DebugMode ? "true" : "false")}");
        if (PassedThrough.TryGetValue("AccessibilityMod", out var extraSettings))
        {
            foreach (var entry in extraSettings)
            {
                sb.AppendLine($"{entry.Key} = {entry.Value}");
            }
        }

        foreach (var section in PassedThrough)
        {
            if (section.Key == "AccessibilityMod") continue;

            sb.AppendLine();
            sb.AppendLine($"[{section.Key}]");
            foreach (var entry in section.Value)
            {
                sb.AppendLine($"{entry.Key} = {entry.Value}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }
        return value;
    }
}
