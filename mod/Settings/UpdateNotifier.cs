using System;
using System.Net.Http;
using System.Threading.Tasks;
using MelonLoader;

namespace AccessibilityMod.Settings
{
    /// <summary>
    /// Checks GitHub once at startup (background thread) whether a newer mod build
    /// exists and announces it once the speech system is ready. Stable builds
    /// (vX.Y.Z) compare against the latest release tag; nightly builds against the
    /// nightly channel's mod-version.txt. Dev builds skip the check. Failures stay
    /// silent - an update hint is a courtesy, never an error.
    /// </summary>
    public static class UpdateNotifier
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/danijel1124/Disco-A11y/releases/latest";
        private const string NightlyModVersionUrl = "https://github.com/danijel1124/Disco-A11y/releases/download/nightly/mod-version.txt";

        private static volatile string announcement;
        private static bool announced;

        public static void Initialize(string currentVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || currentVersion == "dev") return;
            Task.Run(() => CheckAsync(currentVersion));
        }

        private static async Task CheckAsync(string currentVersion)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumAccessibilityMod/1.0");
                http.Timeout = TimeSpan.FromSeconds(15);

                string latest;
                if (currentVersion.StartsWith("nightly-"))
                {
                    latest = (await http.GetStringAsync(NightlyModVersionUrl)).Trim();
                }
                else
                {
                    var json = await http.GetStringAsync(LatestReleaseUrl);
                    var marker = "\"tag_name\":\"";
                    var start = json.IndexOf(marker, StringComparison.Ordinal);
                    if (start < 0) return;
                    start += marker.Length;
                    latest = json.Substring(start, json.IndexOf('"', start) - start);
                }

                if (latest.Length > 0 && latest != currentVersion)
                {
                    announcement = Loc.Get("UpdateAvailable", latest);
                    MelonLogger.Msg($"[UPDATE] Newer build available: {latest} (running {currentVersion})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[UPDATE] Check skipped: {ex.Message}");
            }
        }

        /// <summary>Called from OnUpdate; speaks the pending hint once, queued.</summary>
        public static void Update()
        {
            if (announced || announcement == null) return;
            announced = true;
            TolkScreenReader.Instance.Speak(announcement, false, AnnouncementCategory.Queueable);
        }
    }
}
