using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Installer;

/// <summary>
/// Locates the Disco Elysium install folder: default Steam path, then the registry-reported
/// Steam install, then any additional library folders listed in libraryfolders.vdf.
/// </summary>
public static class GamePathFinder
{
    private const string DefaultSteamPath = @"C:\Program Files (x86)\Steam";
    private const string SteamFolderName = "Disco Elysium";
    private const string ExecutableName = "disco.exe";

    public static string? FindGamePath()
    {
        var defaultPath = Path.Combine(DefaultSteamPath, "steamapps", "common", SteamFolderName);
        if (IsValid(defaultPath)) return defaultPath;

        var steamPath = GetSteamPathFromRegistry();
        if (steamPath == null) return null;

        var mainLibraryPath = Path.Combine(steamPath, "steamapps", "common", SteamFolderName);
        if (IsValid(mainLibraryPath)) return mainLibraryPath;

        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath)) return null;

        foreach (var libraryPath in ParseLibraryFolders(File.ReadAllText(libraryFoldersPath)))
        {
            var candidate = Path.Combine(libraryPath, "steamapps", "common", SteamFolderName);
            if (IsValid(candidate)) return candidate;
        }

        return null;
    }

    public static bool IsValid(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, ExecutableName));

    private static string? GetSteamPathFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key?.GetValue("InstallPath") is string path64 && Directory.Exists(path64)) return path64;

            using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key32?.GetValue("InstallPath") is string path32 && Directory.Exists(path32)) return path32;
        }
        catch
        {
            // registry access may fail; ignore
        }
        return null;
    }

    // Minimal VDF ("Valve Data Format") library folder path extractor - just pulls every
    // quoted "path" value, which is all libraryfolders.vdf needs here.
    private static IEnumerable<string> ParseLibraryFolders(string vdfContent)
    {
        foreach (Match match in Regex.Matches(vdfContent, "\"path\"\\s*\"([^\"]+)\""))
        {
            yield return match.Groups[1].Value.Replace("\\\\", "\\");
        }
    }
}
