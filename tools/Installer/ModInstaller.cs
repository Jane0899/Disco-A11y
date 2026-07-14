using System.IO.Compression;
using System.Text.Json;

namespace Installer;

/// <summary>
/// Downloads and installs the Disco Elysium Accessibility Mod itself from a GitHub
/// release. Defaults to the danijel1124/Disco-A11y fork since upstream
/// (game-a11y/Disco-A11y) has never published a release - only source + a local
/// release.sh script.
/// </summary>
public static class ModInstaller
{
    public const string DefaultOwner = "danijel1124";
    public const string DefaultRepo = "Disco-A11y";

    /// <summary>True while the game itself is running - its loaded DLLs are locked, so installing would fail halfway.</summary>
    public static bool IsGameRunning() =>
        System.Diagnostics.Process.GetProcessesByName("disco").Length > 0;

    /// <summary>True when no mod config exists yet - i.e. a fresh install that should get a keybind preset written.</summary>
    public static bool IsFreshConfig(string gamePath) =>
        !File.Exists(Path.Combine(gamePath, "UserData", "AccessibilityMod.cfg"));

    /// <summary>
    /// Writes a keybind preset into a fresh install's config via the bundled
    /// configurator's CLI. Without this, fresh installs get the upstream US-QWERTY
    /// punctuation defaults - the exact layout problem this fork exists to fix.
    /// </summary>
    public static async Task<bool> ApplyPresetAsync(string gamePath, string preset, Action<string> log)
    {
        var configurator = KeybindEditorLocator.Find();
        if (configurator == null)
        {
            log(Strings.Get("PresetToolMissing"));
            return false;
        }

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            configurator, $"--cli \"{gamePath}\" --preset {preset}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                log(Strings.Get("PresetApplied", preset));
                return true;
            }
        }
        log(Strings.Get("PresetFailed"));
        return false;
    }

    public enum DevBridgeResult { Installed, Removed, Absent, SourceMissing }

    /// <summary>
    /// Installs or removes the AI dev bridge companion mod (DevBridge.dll, shipped in
    /// the tools bundle next to this installer, never in the mod release zip). Enable
    /// copies it into Mods/; disable deletes it from there so toggling the checkbox
    /// off on a reinstall cleanly de-activates the bridge.
    /// </summary>
    public static DevBridgeResult SetDevBridgeEnabled(string gamePath, bool enable)
    {
        var source = Path.Combine(Program.BundleDir, "DevBridge.dll");
        if (!File.Exists(source)) source = Path.Combine(AppContext.BaseDirectory, "DevBridge.dll");
        var dest = Path.Combine(gamePath, "Mods", "DevBridge.dll");

        if (enable)
        {
            if (!File.Exists(source)) return DevBridgeResult.SourceMissing;
            Directory.CreateDirectory(Path.Combine(gamePath, "Mods"));
            File.Copy(source, dest, overwrite: true);
            return DevBridgeResult.Installed;
        }

        if (File.Exists(dest))
        {
            File.Delete(dest);
            return DevBridgeResult.Removed;
        }
        return DevBridgeResult.Absent;
    }

    /// <summary>
    /// Puts the mod debugger into the game folder, where the mod's Ctrl+Y looks for it.
    ///
    /// Not a checkbox: the key is already behind debug mode, so an exe sitting unused in the
    /// folder costs nothing - whereas a player who switches debug mode on and presses Ctrl+Y
    /// only to be told the tool is missing has to go and find a zip.
    /// </summary>
    public static bool InstallModDebugger(string gamePath)
    {
        const string exeName = "DiscoElysiumModDebugger.exe";

        var source = Path.Combine(Program.BundleDir, exeName);
        if (!File.Exists(source)) source = Path.Combine(AppContext.BaseDirectory, exeName);
        if (!File.Exists(source)) return false;

        try
        {
            File.Copy(source, Path.Combine(gamePath, exeName), overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;   // in use (debugger open) - the copy already there still works
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string VersionMarkerPath(string gamePath) =>
        Path.Combine(gamePath, "Mods", "AccessibilityMod.version.txt");

    /// <summary>Version tag of the currently installed mod, or null (no mod / pre-marker install).</summary>
    public static string? GetInstalledVersion(string gamePath)
    {
        try
        {
            var marker = VersionMarkerPath(gamePath);
            if (File.Exists(marker)) return File.ReadAllText(marker).Trim();
            return File.Exists(Path.Combine(gamePath, "Mods", "AccessibilityMod.dll")) ? "unknown" : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> InstallLatestAsync(
        string gamePath,
        Action<string>? statusCallback = null,
        bool includePrerelease = false,
        Func<string, string, bool>? confirmOverwrite = null,
        string owner = DefaultOwner,
        string repo = DefaultRepo
    )
    {
        if (IsGameRunning())
        {
            throw new Exception(Strings.Get("GameRunningError"));
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        // /releases/latest never returns prereleases, so the nightly channel has to
        // walk the full release list (newest first) and take the first non-draft entry.
        var releaseUrl = includePrerelease
            ? $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=10"
            : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var releaseResponse = await httpClient.GetAsync(releaseUrl);
        releaseResponse.EnsureSuccessStatusCode();

        using var releaseDoc = JsonDocument.Parse(await releaseResponse.Content.ReadAsStreamAsync());
        JsonElement root;
        if (includePrerelease)
        {
            // The nightly tag is updated in place, so its created_at stays old and
            // "first in the list" would wrongly pick a newer-created stable release -
            // the prerelease channel explicitly means the nightly tag when present.
            root = default;
            var found = false;
            foreach (var candidate in releaseDoc.RootElement.EnumerateArray())
            {
                if (candidate.GetProperty("draft").GetBoolean()) continue;
                if (candidate.GetProperty("tag_name").GetString() == "nightly")
                {
                    root = candidate;
                    found = true;
                    break;
                }
                if (!found)
                {
                    root = candidate;
                    found = true;
                }
            }
            if (!found)
            {
                throw new Exception($"No published release found on {owner}/{repo}.");
            }
        }
        else
        {
            root = releaseDoc.RootElement;
        }
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";

        // Releases can carry more than one zip (the mod itself plus a tools bundle), so
        // prefer the asset named like the mod package and only fall back to "any zip".
        string? downloadUrl = null;
        string? fallbackUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            if (name.StartsWith("DiscoElysiumAccessibilityMod", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = url;
                break;
            }
            fallbackUrl ??= url;
        }
        downloadUrl ??= fallbackUrl;

        if (downloadUrl == null)
        {
            throw new Exception($"Release {tag} on {owner}/{repo} has no .zip asset.");
        }

        // "Found vX - overwrite with vY?" so upgrades are explicit and verifiable.
        var installed = GetInstalledVersion(gamePath);
        if (installed != null)
        {
            statusCallback?.Invoke(Strings.Get("ModVersionFound", installed, tag));
            if (confirmOverwrite != null && !confirmOverwrite(installed, tag))
            {
                statusCallback?.Invoke(Strings.Get("ModOverwriteSkipped"));
                return installed;
            }
        }

        statusCallback?.Invoke(Strings.Get("StepDownloadingRelease", tag));

        var tempZip = Path.Combine(Path.GetTempPath(), $"DiscoA11y_{Guid.NewGuid():N}.zip");
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiscoA11y_{Guid.NewGuid():N}");

        try
        {
            using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await contentStream.CopyToAsync(fileStream);
            }

            statusCallback?.Invoke(Strings.Get("StepExtracting"));
            ZipFile.ExtractToDirectory(tempZip, tempDir);

            var extractedRoot = FindExtractedRoot(tempDir);
            InstallFiles(extractedRoot, gamePath, statusCallback);
            File.WriteAllText(VersionMarkerPath(gamePath), tag);

            return tag;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* ignore */ }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    private static string FindExtractedRoot(string extractPath)
    {
        var subdirs = Directory.GetDirectories(extractPath);
        if (subdirs.Length == 1) return subdirs[0];
        return extractPath;
    }

    private static void InstallFiles(string extractedRoot, string gamePath, Action<string>? statusCallback)
    {
        var modsPath = Path.Combine(gamePath, "Mods");
        Directory.CreateDirectory(modsPath);

        CopyFile(Path.Combine(extractedRoot, "Mods", "AccessibilityMod.dll"), Path.Combine(modsPath, "AccessibilityMod.dll"), statusCallback);
        CopyFile(Path.Combine(extractedRoot, "Tolk.dll"), Path.Combine(gamePath, "Tolk.dll"), statusCallback);
        CopyFile(Path.Combine(extractedRoot, "nvdaControllerClient64.dll"), Path.Combine(gamePath, "nvdaControllerClient64.dll"), statusCallback);

        var userDataSource = Path.Combine(extractedRoot, "UserData");
        if (Directory.Exists(userDataSource))
        {
            var userDataDest = Path.Combine(gamePath, "UserData");
            Directory.CreateDirectory(userDataDest);
            statusCallback?.Invoke(Strings.Get("StepCopying", "UserData"));
            // Never overwrite: UserData holds the player's own keybinds/waypoints/settings
            // once the mod has run. Only seed files that don't exist yet (first install),
            // so reinstalling/updating never clobbers real progress with the release's
            // bundled defaults.
            CopyDirectory(userDataSource, userDataDest, overwrite: false);
        }
    }

    private static void CopyFile(string source, string dest, Action<string>? statusCallback)
    {
        if (!File.Exists(source)) return;
        statusCallback?.Invoke(Strings.Get("StepCopying", Path.GetFileName(source)));
        File.Copy(source, dest, overwrite: true);
    }

    private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            if (overwrite || !File.Exists(dest))
            {
                File.Copy(file, dest, overwrite: true);
            }
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), overwrite);
        }
    }
}
