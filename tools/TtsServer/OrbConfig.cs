using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TtsServer;

/// <summary>
/// The orb voice's settings, read straight from the mod's own config file
/// (UserData/AccessibilityMod.cfg, [AccessibilityMod] section). The server owns everything
/// about <em>how</em> orb text is spoken - voice, volume, rate - so these live here, not in
/// the mod: the mod only ever hands over (speaker, text) and never learns which voice or
/// engine is in play.
///
/// Re-read on demand but only when the file actually changed (by last-write time), so that
/// dragging the volume or voice in the configurator takes effect on the next line without a
/// game restart - and without re-parsing the file for every single utterance.
/// </summary>
public sealed class OrbConfig
{
    /// <summary>Voice display name as shown by Windows (e.g. "Microsoft Katja"); empty = system default.</summary>
    public string Voice { get; private set; } = "";

    /// <summary>0-100. Mapped onto the synthesizer's 0.0-1.0 AudioVolume.</summary>
    public int Volume { get; private set; } = 80;

    /// <summary>Speaking rate in percent, 100 = normal. Mapped onto the synthesizer's 0.5-6.0 SpeakingRate.</summary>
    public int Rate { get; private set; } = 100;

    private readonly string path;
    private DateTime lastWriteUtc = DateTime.MinValue;
    private bool everLoaded;

    public OrbConfig(string gamePath)
    {
        path = Path.Combine(gamePath, "UserData", "AccessibilityMod.cfg");
    }

    /// <summary>Reloads from disk if the file changed since last time. Cheap to call per line.</summary>
    public void Refresh()
    {
        try
        {
            if (!File.Exists(path))
            {
                everLoaded = true;
                return; // keep defaults
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (everLoaded && stamp == lastWriteUtc)
            {
                return;
            }
            lastWriteUtc = stamp;
            everLoaded = true;
            Parse(File.ReadAllLines(path));
        }
        catch
        {
            // A malformed or briefly-locked config must never take the server down; the
            // last good values (or defaults) simply stay in effect.
        }
    }

    private void Parse(string[] lines)
    {
        string? section = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var sectionMatch = Regex.Match(line, @"^\[(.+)\]$");
            if (sectionMatch.Success)
            {
                section = sectionMatch.Groups[1].Value;
                continue;
            }

            // MelonPreferences sometimes writes a category as a single TOML inline table
            // ("AccessibilityMod = { OrbVolume = 80, ... }") instead of a [section] block.
            var inline = Regex.Match(line, @"^([A-Za-z0-9_]+)\s*=\s*\{(.*)\}\s*$");
            if (inline.Success && inline.Groups[1].Value == "AccessibilityMod")
            {
                foreach (var pair in inline.Groups[2].Value.Split(','))
                {
                    var m = Regex.Match(pair.Trim(), @"^([A-Za-z0-9_]+)\s*=\s*(.+)$");
                    if (m.Success) Apply(m.Groups[1].Value, m.Groups[2].Value.Trim());
                }
                continue;
            }

            if (section != "AccessibilityMod") continue;
            var kv = Regex.Match(line, @"^([A-Za-z0-9_]+)\s*=\s*(.+)$");
            if (kv.Success) Apply(kv.Groups[1].Value, kv.Groups[2].Value.Trim());
        }
    }

    private void Apply(string key, string value)
    {
        switch (key)
        {
            case "OrbVoice":
                Voice = Unquote(value);
                break;
            case "OrbVolume":
                if (int.TryParse(value, out var v)) Volume = Math.Clamp(v, 0, 100);
                break;
            case "OrbRate":
                if (int.TryParse(value, out var r)) Rate = Math.Clamp(r, 50, 300);
                break;
        }
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1];
        return value;
    }
}
