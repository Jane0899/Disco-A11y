using System.Diagnostics;
using System.Reflection;

namespace Installer;

/// <summary>
/// Mandatory self-update: development is very active, so an outdated installer binary
/// could install wrongly. On startup the embedded BuildId is compared against
/// installer-version.txt in the nightly release; on mismatch the matching setup asset
/// (framework/standalone flavor) is downloaded, the running exe is swapped out
/// (rename-to-.old trick) and restarted. Without a successful, current update check no
/// installation is allowed. Escape hatch for development: --no-selfupdate.
/// </summary>
public static class SelfUpdater
{
    private const string VersionAssetUrl =
        "https://github.com/danijel1124/Disco-A11y/releases/download/nightly/installer-version.txt";

    public enum Result { UpToDate, Restarting, Blocked }

    public static string LocalBuildId => GetMetadata("BuildId") ?? "dev";
    private static string Flavor => GetMetadata("Flavor") ?? "framework";

    private static string GetMetadata(string key) =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

    public static async Task<Result> EnsureLatestAsync(string[] originalArgs, Action<string> log)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
            http.Timeout = TimeSpan.FromSeconds(20);

            var remote = (await http.GetStringAsync(VersionAssetUrl)).Trim();
            if (remote.Length == 0)
            {
                log(Strings.Get("UpdateCheckFailed", "empty version file"));
                return Result.Blocked;
            }

            if (remote == LocalBuildId)
            {
                return Result.UpToDate;
            }

            log(Strings.Get("UpdateDownloading", remote));

            var assetName = Flavor == "standalone" ? "DiscoElysiumSetup-standalone.exe" : "DiscoElysiumSetup.exe";
            var assetUrl = $"https://github.com/danijel1124/Disco-A11y/releases/download/nightly/{assetName}";
            var exePath = Environment.ProcessPath!;
            var newPath = exePath + ".new";
            var oldPath = exePath + ".old";

            using (var response = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var content = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(newPath);
                await content.CopyToAsync(file);
            }

            // A running exe cannot be overwritten but can be renamed away.
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            File.Move(newPath, exePath);

            var args = string.Join(" ", originalArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Process.Start(new ProcessStartInfo(exePath, $"--updated {args}".Trim()) { UseShellExecute = true });
            return Result.Restarting;
        }
        catch (Exception ex)
        {
            log(Strings.Get("UpdateCheckFailed", ex.Message));
            return Result.Blocked;
        }
    }

    /// <summary>Removes the leftover .old binary after a successful swap-restart.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var oldPath = Environment.ProcessPath + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch { /* still locked - next run gets it */ }
    }
}
