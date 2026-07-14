using System;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTMPro;
using AccessibilityMod.Settings;
using AccessibilityMod.Utils;

namespace AccessibilityMod.Patches
{
    [HarmonyPatch]
    public static class OrbTextVocalizationPatches
    {
        private static bool orbAnnouncementsEnabled = true;
        private static string lastAnnouncedOrbText = "";
        private static float lastOrbAnnouncementTime = 0f;
        private const float ORB_COOLDOWN = 0.5f; // 500ms cooldown to prevent duplicate announcements

        static OrbTextVocalizationPatches()
        {
            // Load initial setting from preferences
            orbAnnouncementsEnabled = AccessibilityPreferences.GetOrbAnnouncements();
        }

        public static void ToggleOrbAnnouncements()
        {
            orbAnnouncementsEnabled = !orbAnnouncementsEnabled;
            string status = orbAnnouncementsEnabled ? "enabled" : "disabled";
            TolkScreenReader.Instance.Speak($"Orb announcements {status}", true);
            MelonLogger.Msg($"[ORB TOGGLE] Orb announcements {status}");

            // Save the new setting
            AccessibilityPreferences.SetOrbAnnouncements(orbAnnouncementsEnabled);
        }

        // Ambient sources loop: the room's radio rotates half a dozen weather reports at a
        // few seconds each, forever, and every line landed in the player's ear mid-task.
        // Each distinct line is spoken once and then suppressed for a while - the first
        // pass through the playlist is information, the fifth is noise.
        private const float REPEAT_SUPPRESSION_SECONDS = 180f;
        private static readonly System.Collections.Generic.Dictionary<string, float> spokenFloats = new();
        private static string spokenFloatsScene = "";

        /// <summary>
        /// Who the speech bubble belongs to. A sighted player sees the bubble hanging
        /// over the radio or over a person; without the name, a weather report is a
        /// poltergeist and a "Hello, officer" comes out of nowhere.
        /// </summary>
        private static string DescribeSpeaker(Transform target)
        {
            try
            {
                if (target == null) return null;

                var highlight = target.GetComponentInParent<Il2CppFortressOccident.MouseOverHighlight>();
                if (highlight != null) return ObjectNameCleaner.GetBetterObjectName(highlight);

                var entity = target.GetComponentInParent<Il2CppFortressOccident.GameEntity>();
                if (entity != null && !string.IsNullOrEmpty(entity.name))
                    return ObjectNameCleaner.CleanObjectName(entity.name);

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// How long a source has to stay quiet before it may speak again, and how that grows.
        ///
        /// Suppressing repeated TEXT is not enough: the hostel's radio reads a weather report
        /// for a different city every twelve seconds, forever, so every line is a new line and
        /// nothing ever repeats. What loops is not the text, it is the SOURCE. So each source
        /// gets a budget instead: the first lines come through (a weather report is worth
        /// hearing once), and every further line from that same source doubles the silence
        /// that must pass before the next one - 15s, 30s, 60s, up to two minutes. A radio
        /// talks itself into the background; a person who says one thing and stops is never
        /// throttled at all. After two minutes of quiet the source is forgiven and starts over,
        /// so an NPC you come back to later is not still being punished.
        /// </summary>
        private const float SOURCE_FIRST_COOLDOWN = 15f;
        private const float SOURCE_MAX_COOLDOWN = 120f;
        private const float SOURCE_FORGIVE_AFTER = 120f;

        private sealed class SourceState
        {
            public float LastSpoken;
            public float Cooldown;
        }

        private static readonly System.Collections.Generic.Dictionary<string, SourceState> sources = new();

        /// <summary>
        /// True when this source has to keep quiet. Updates the source's budget when it speaks.
        /// </summary>
        private static bool SourceIsTalkingTooMuch(string source, float now)
        {
            if (!sources.TryGetValue(source, out var state))
            {
                sources[source] = new SourceState { LastSpoken = now, Cooldown = SOURCE_FIRST_COOLDOWN };
                return false;
            }

            float silence = now - state.LastSpoken;

            // Quiet for a long time: whatever it was doing, it stopped. Start over.
            if (silence >= SOURCE_FORGIVE_AFTER)
            {
                state.LastSpoken = now;
                state.Cooldown = SOURCE_FIRST_COOLDOWN;
                return false;
            }

            if (silence < state.Cooldown) return true;

            state.LastSpoken = now;
            state.Cooldown = Mathf.Min(state.Cooldown * 2f, SOURCE_MAX_COOLDOWN);
            return false;
        }

        /// <summary>
        /// The comparison key for "have I already said this line?". The same line reaches
        /// us through several patch paths and they do not agree on the details: one carries
        /// rich-text markup, another curly quotes where the first has straight ones, a third
        /// a stray line break. Comparing the raw strings let every one of those differences
        /// through as a fresh line, and the player heard each orb twice. The key throws away
        /// everything that does not change what is spoken.
        /// </summary>
        private static string DedupKey(string text)
        {
            string key = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            key = key.Replace('“', '"').Replace('”', '"').Replace('„', '"')
                     .Replace('‘', '\'').Replace('’', '\'')
                     .Replace('–', '-').Replace('—', '-')
                     .Replace(' ', ' ');
            key = System.Text.RegularExpressions.Regex.Replace(key, @"\s+", " ");
            return key.Trim().ToLowerInvariant();
        }

        private static void AnnounceOrbText(string text, Transform target = null, string prefix = "Orb text")
        {
            if (!orbAnnouncementsEnabled || string.IsNullOrEmpty(text))
                return;

            // Markup is stripped from what gets spoken too - "das hier <i>IST</i> gestört"
            // must not reach the screen reader as tags.
            string trimmedText = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            trimmedText = RTLHelper.FixForScreenReader(trimmedText.Trim());
            if (string.IsNullOrEmpty(trimmedText)) return;

            string key = DedupKey(text);
            float currentTime = Time.time;

            // Check if this is a duplicate within the cooldown period
            if (key == lastAnnouncedOrbText &&
                (currentTime - lastOrbAnnouncementTime) < ORB_COOLDOWN)
            {
                return;
            }

            // Looping ambience: a line already spoken recently stays quiet. Kept per
            // scene - other rooms, other radios.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != spokenFloatsScene)
            {
                spokenFloats.Clear();
                sources.Clear();
                spokenFloatsScene = scene;
            }
            if (spokenFloats.TryGetValue(key, out float firstSpoken)
                && currentTime - firstSpoken < REPEAT_SUPPRESSION_SECONDS)
            {
                if (AccessibilityPreferences.GetDebugMode())
                    MelonLogger.Msg($"[ORB] suppressed via {prefix} t={currentTime:F2} key=<{key}>");
                return;
            }

            string speaker = DescribeSpeaker(target);

            // A source that will not stop talking gets ever longer pauses imposed on it.
            // Keyed by the speaker where we know one, and by the bubble's anchor otherwise -
            // an unnamed radio is still one radio.
            string sourceId = speaker
                ?? (target != null ? $"#{target.GetInstanceID()}" : "ambient");
            if (SourceIsTalkingTooMuch(sourceId, currentTime))
            {
                if (AccessibilityPreferences.GetDebugMode())
                    MelonLogger.Msg($"[ORB] throttled source <{sourceId}> t={currentTime:F2} key=<{key}>");
                return;
            }

            spokenFloats[key] = currentTime;

            // Update tracking
            lastAnnouncedOrbText = key;
            lastOrbAnnouncementTime = currentTime;

            if (AccessibilityPreferences.GetDebugMode())
                MelonLogger.Msg($"[ORB] spoken via {prefix} t={currentTime:F2} source=<{sourceId}> key=<{key}>");

            string announcement = speaker != null ? $"{speaker}: {trimmedText}" : $"{prefix}: {trimmedText}";

            // Orb text goes to the separate SAPI voice, not the screen reader: on the screen
            // reader it was drowned out - every keypress and navigation announcement interrupts
            // the line before it, and an atmospheric bubble never survived to the end. The SAPI
            // voice plays in parallel with NVDA instead of competing for the one channel. Lines
            // queue on it (no interrupt) so two speakers close together are both heard. Only if
            // SAPI is somehow unavailable does it fall back to the screen reader.
            if (SapiVoice.Speak(announcement))
            {
                // Spoken on the SAPI voice, but still recorded as heard so the speech log and
                // the debugger transcript stay complete.
                TolkScreenReader.Instance.RecordExternalSpeech(announcement);
            }
            else
            {
                TolkScreenReader.Instance.Speak(announcement, true, AnnouncementCategory.Queueable);
            }
        }
        /// <summary>
        /// Patch for FloatFactory.ShowFloat(string, Transform) to vocalize text
        /// </summary>
        [HarmonyPatch(typeof(Il2Cpp.FloatFactory), "ShowFloat", new System.Type[] { typeof(string), typeof(Transform) })]
        [HarmonyPostfix]
        public static void FloatFactory_ShowFloat_TwoParam_Postfix(string text, Transform target)
        {
            try
            {
                AnnounceOrbText(text, target);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FloatFactory ShowFloat (2-param) patch: {ex}");
            }
        }

        /// <summary>
        /// Patch for FloatFactory.ShowFloat(string, Transform, Vector3, float) to vocalize text
        /// </summary>
        [HarmonyPatch(typeof(Il2Cpp.FloatFactory), "ShowFloat", new System.Type[] { typeof(string), typeof(Transform), typeof(Vector3), typeof(float) })]
        [HarmonyPostfix]
        public static void FloatFactory_ShowFloat_FourParam_Postfix(string text, Transform target, Vector3 offset, float time)
        {
            try
            {
                AnnounceOrbText(text, target);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FloatFactory ShowFloat (4-param) patch: {ex}");
            }
        }

        /// <summary>
        /// Patch for FloatFactory.ShowLocalizedFloat(string, string, Transform) to vocalize localized text
        /// </summary>
        [HarmonyPatch(typeof(Il2Cpp.FloatFactory), "ShowLocalizedFloat", new System.Type[] { typeof(string), typeof(string), typeof(Transform) })]
        [HarmonyPostfix]
        public static void FloatFactory_ShowLocalizedFloat_ThreeParam_Postfix(string term, string fallbackText, Transform target, Il2Cpp.FloatTemplate __result)
        {
            try
            {
                if (__result != null)
                {
                    string displayedText = __result.text;
                    if (!string.IsNullOrEmpty(displayedText))
                    {
                        AnnounceOrbText(displayedText, target);
                    }
                    else if (!string.IsNullOrEmpty(fallbackText))
                    {
                        AnnounceOrbText(fallbackText, target);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FloatFactory ShowLocalizedFloat (3-param) patch: {ex}");
            }
        }

        /// <summary>
        /// Patch for FloatFactory.ShowLocalizedFloat(string, string, Transform, Vector3, float) to vocalize localized text
        /// </summary>
        [HarmonyPatch(typeof(Il2Cpp.FloatFactory), "ShowLocalizedFloat", new System.Type[] { typeof(string), typeof(string), typeof(Transform), typeof(Vector3), typeof(float) })]
        [HarmonyPostfix]
        public static void FloatFactory_ShowLocalizedFloat_FiveParam_Postfix(string term, string fallbackText, Transform target, Vector3 offset, float time, Il2Cpp.FloatTemplate __result)
        {
            try
            {
                if (__result != null)
                {
                    string displayedText = __result.text;
                    if (!string.IsNullOrEmpty(displayedText))
                    {
                        AnnounceOrbText(displayedText, target);
                    }
                    else if (!string.IsNullOrEmpty(fallbackText))
                    {
                        AnnounceOrbText(fallbackText, target);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FloatFactory ShowLocalizedFloat (5-param) patch: {ex}");
            }
        }

        /// <summary>
        /// Alternative approach: Patch FloatTemplate.set_text to catch when text is actually set
        /// </summary>
        [HarmonyPatch(typeof(Il2Cpp.FloatTemplate), "set_text")]
        [HarmonyPostfix]
        public static void FloatTemplate_SetText_Postfix(Il2Cpp.FloatTemplate __instance, string value)
        {
            try
            {
                AnnounceOrbText(value, __instance != null ? __instance.transform : null, "Float text");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in FloatTemplate set_text patch: {ex}");
            }
        }
    }
}