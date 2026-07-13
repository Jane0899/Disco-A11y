using System.Diagnostics;
using System.Reflection;

namespace Installer;

/// <summary>
/// Mandatory self-update: development is very active, so an outdated installer binary
/// could install wrongly. The embedded BuildId is compared against installer-version.txt
/// in the nightly release; on mismatch the matching setup asset (framework/standalone
/// flavor) is downloaded, the running exe swapped out (rename-to-.old trick) and
/// restarted. Without a successful, current update check no installation is allowed.
/// Escape hatch for development: --no-selfupdate.
///
/// Mandatory, but never silent: the steps (check, ask, download with progress, restart)
/// are separate calls precisely so a UI can show each one. Replacing the program someone
/// just started, and then killing and relaunching it, is not something to do behind their
/// back - least of all for a blind user, who cannot see what the flickering window did.
/// </summary>
public static class SelfUpdater
{
    private const string ReleaseUrl = "https://github.com/danijel1124/Disco-A11y/releases/download/nightly";
    private const string VersionAssetUrl = ReleaseUrl + "/installer-version.txt";

    public static string LocalBuildId => GetMetadata("BuildId") ?? "dev";
    private static string Flavor => GetMetadata("Flavor") ?? "framework";

    private static string GetMetadata(string key) =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
        // Generous, because this client also fetches the installer binary, and the
        // standalone flavor is ~68 MB: at 20 seconds that download simply died on a normal
        // home connection, and the user was told the update check had failed.
        http.Timeout = TimeSpan.FromMinutes(10);
        return http;
    }

    public enum CheckResult { UpToDate, UpdateAvailable, Failed }

    /// <summary>The version to update to, or null. Failure is fatal: no check, no install.</summary>
    public static async Task<(CheckResult Result, string Version, string Error)> CheckAsync()
    {
        try
        {
            using var http = CreateClient();
            var remote = (await http.GetStringAsync(VersionAssetUrl)).Trim();

            if (remote.Length == 0) return (CheckResult.Failed, null, "empty version file");
            if (remote == LocalBuildId) return (CheckResult.UpToDate, remote, null);
            return (CheckResult.UpdateAvailable, remote, null);
        }
        catch (Exception ex)
        {
            return (CheckResult.Failed, null, Describe(ex));
        }
    }

    /// <summary>
    /// Downloads the new installer and swaps it in, reporting percent-complete as it goes.
    /// Returns null on success, the error to show otherwise. The running exe is only
    /// replaced once the download is complete, so a failed download leaves a working
    /// installer behind.
    /// </summary>
    public static async Task<string> DownloadAndSwapAsync(IProgress<int> progress)
    {
        var exePath = Environment.ProcessPath!;
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        try
        {
            var assetName = Flavor == "standalone" ? "DiscoElysiumSetup-standalone.exe" : "DiscoElysiumSetup.exe";

            using var http = CreateClient();
            using (var response = await http.GetAsync($"{ReleaseUrl}/{assetName}", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? 0;
                await using var content = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(newPath);

                var buffer = new byte[81920];
                long done = 0;
                int lastPercent = -1;
                int read;

                while ((read = await content.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;

                    if (total <= 0) continue;
                    var percent = (int)(done * 100 / total);
                    if (percent == lastPercent) continue;
                    lastPercent = percent;
                    progress?.Report(percent);
                }
            }

            // A downloaded exe carries a mark-of-the-web stream, which makes ShellExecute
            // refuse to launch it (with an empty-message Win32 error) - especially from a
            // removable drive. It is our own release asset; unblock it.
            RemoveMarkOfTheWeb(newPath);

            // A running exe cannot be overwritten, but it can be renamed away.
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            File.Move(newPath, exePath);

            progress?.Report(100);
            return null;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            return Describe(ex);
        }
    }

    /// <summary>
    /// Restarts into the freshly installed binary. Tries the shell first (so the new
    /// process behaves like a double-click), then a plain start, which works in the
    /// environments where the shell refuses.
    /// </summary>
    public static bool TryRestart(string[] originalArgs)
    {
        var exePath = Environment.ProcessPath!;
        var args = string.Join(" ", originalArgs.Where(a => a != "--updated")
            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        foreach (var useShell in new[] { true, false })
        {
            try
            {
                Process.Start(new ProcessStartInfo(exePath, $"--updated {args}".Trim()) { UseShellExecute = useShell });
                return true;
            }
            catch
            {
                // try the next way
            }
        }

        return false;
    }

    /// <summary>Removes the leftover .old binary from a previous swap.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var oldPath = Environment.ProcessPath + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch { /* still locked - a later run gets it */ }
    }

    private static void RemoveMarkOfTheWeb(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch { /* no such stream, or a filesystem without them (FAT32 stick) - both fine */ }
    }

    /// <summary>Some Win32/IO failures carry an empty Message, which produced the useless "update check failed ()".</summary>
    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : $"{ex.GetType().Name}: {ex.Message}";
}
