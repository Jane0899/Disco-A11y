using MelonLoader;
using AccessibilityMod.UI;

namespace AccessibilityMod.Settings
{
    public static class AccessibilityPreferences
    {
        private static MelonPreferences_Category category;
        private static MelonPreferences_Entry<int> dialogModeEntry;
        private static MelonPreferences_Entry<bool> orbAnnouncementsEntry;
        private static MelonPreferences_Entry<bool> speechInterruptEntry;
        private static MelonPreferences_Entry<bool> speakAudioCaptionsEntry;
        private static MelonPreferences_Entry<bool> dialogAutoAdvanceEntry;
        private static MelonPreferences_Entry<bool> autoInteractEntry;
        private static MelonPreferences_Entry<bool> tutorialTipsEntry;
        private static MelonPreferences_Entry<string> languageEntry;
        private static MelonPreferences_Entry<bool> speechLogEntry;
        private static MelonPreferences_Entry<bool> debugModeEntry;
        private static MelonPreferences_Entry<bool> itemDescriptionsEntry;
        private static MelonPreferences_Entry<string> seenAreaIntrosEntry;
        private static MelonPreferences_Entry<int> repeatSuppressionMinutesEntry;

        public static void Initialize()
        {
            category = MelonPreferences.CreateCategory("AccessibilityMod");
            category.SetFilePath("UserData/AccessibilityMod.cfg");

            dialogModeEntry = category.CreateEntry<int>("DialogReadingMode", 0,
                "Dialog Reading Mode (0=Disabled, 1=Full, 2=SpeakerOnly)");

            orbAnnouncementsEntry = category.CreateEntry<bool>("OrbAnnouncements", true,
                "Enable orb text announcements");

            // Orb text plays on its own voice through the external TTS server (tools/TtsServer,
            // driven by mod/OrbSpeech.cs), which reads voice, volume and rate straight from this
            // file. The mod never reads them - it only hands the server (speaker, text). These
            // three keys are still registered here, without accessors, purely so the mod's own
            // config saves keep them in the file (an unregistered key under this category would
            // be dropped on the next SaveToFile) and so the settings are documented in one place.
            // The screen reader has its own volume and is untouched by any of this.
            category.CreateEntry<int>("OrbVolume", 80, "Volume of the separate orb-text voice, 0-100 (read by the TTS server)");
            category.CreateEntry<string>("OrbVoice", "", "Windows voice display name for orb text; empty = system default (read by the TTS server)");
            category.CreateEntry<int>("OrbRate", 100, "Speaking rate of the orb voice in percent, 100 = normal (read by the TTS server)");

            // One window governs two kinds of repetition: an ambient orb line that keeps
            // looping (the hostel radio's weather reports), and an area description replayed
            // because the player walked a door back and forth while searching a room. Both
            // ask the same question - "did I already say this in the last few minutes?" - so
            // they share one player-facing number. 0 = never suppress; default 3 minutes
            // matches the value the orb suppression used before it was made configurable.
            repeatSuppressionMinutesEntry = category.CreateEntry<int>("RepeatSuppressionMinutes", 3,
                "Minutes before a repeated orb line or a re-entered area description may play again (0 = never suppress)");

            speechInterruptEntry = category.CreateEntry<bool>("SpeechInterrupt", false,
                "Enable global speech interrupt");

            speakAudioCaptionsEntry = category.CreateEntry<bool>("SpeakAudioCaptions", true,
                "Speak the game's own sound-effect captions (audio accessibility captions)");

            dialogAutoAdvanceEntry = category.CreateEntry<bool>("DialogAutoAdvance", false,
                "Automatically continue dialogue once the screen reader finishes the current line");

            autoInteractEntry = category.CreateEntry<bool>("AutoInteract", false,
                "Automatically interact with the target object after auto-walk arrives");

            tutorialTipsEntry = category.CreateEntry<bool>("TutorialTips", true,
                "Play contextual one-time tutorial tips");

            languageEntry = category.CreateEntry<string>("Language", "auto",
                "Language for localized announcements: auto, en, de");

            speechLogEntry = category.CreateEntry<bool>("SpeechLog", false,
                "Write everything the mod says to UserData/SpeechLog.txt, with timestamps");

            // The umbrella over everything diagnostic: announcements that only make sense
            // when you are working ON the mod rather than playing with it.
            debugModeEntry = category.CreateEntry<bool>("DebugMode", false,
                "Debug mode: announce internal screens (SPECIAL, LOBBY, ...) and enable the name-sources key");

            seenAreaIntrosEntry = category.CreateEntry<string>("SeenAreaIntros", "",
                "Areas whose long first-visit introduction has already been spoken (comma-separated)");

            // Off by default: the description is one to three sentences, and hearing it after
            // every single item while scanning a room would bury the scan. On, it is read
            // with the item; off, it waits for the key.
            itemDescriptionsEntry = category.CreateEntry<bool>("ItemDescriptions", false,
                "Always read an item's description along with its name (otherwise only on the describe-item key)");

            MelonLogger.Msg($"[PREFERENCES] Initialized - Dialog: {GetDialogMode()}, Orbs: {GetOrbAnnouncements()}, Interrupt: {GetSpeechInterrupt()}, AudioCaptions: {GetSpeakAudioCaptions()}");
        }

        public static DialogReadingMode GetDialogMode()
        {
            return (DialogReadingMode)dialogModeEntry.Value;
        }

        public static void SetDialogMode(DialogReadingMode mode)
        {
            dialogModeEntry.Value = (int)mode;
            category.SaveToFile();
        }

        public static bool GetOrbAnnouncements()
        {
            return orbAnnouncementsEntry.Value;
        }

        public static void SetOrbAnnouncements(bool enabled)
        {
            orbAnnouncementsEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetSpeechInterrupt()
        {
            return speechInterruptEntry.Value;
        }

        public static void SetSpeechInterrupt(bool enabled)
        {
            speechInterruptEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetSpeakAudioCaptions()
        {
            return speakAudioCaptionsEntry.Value;
        }

        public static bool GetDialogAutoAdvance()
        {
            return dialogAutoAdvanceEntry.Value;
        }

        public static void SetDialogAutoAdvance(bool enabled)
        {
            dialogAutoAdvanceEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool HasSeenAreaIntro(string sceneName)
        {
            return ("," + seenAreaIntrosEntry.Value + ",").Contains("," + sceneName + ",");
        }

        public static void MarkAreaIntroSeen(string sceneName)
        {
            if (HasSeenAreaIntro(sceneName)) return;
            seenAreaIntrosEntry.Value = string.IsNullOrEmpty(seenAreaIntrosEntry.Value)
                ? sceneName
                : seenAreaIntrosEntry.Value + "," + sceneName;
            category.SaveToFile();
        }

        /// <summary>
        /// The shared repeat-suppression window in seconds (0 = never suppress). Governs both
        /// looping orb lines and re-entered area descriptions, so there is one dial for the
        /// player, set in minutes in the configurator.
        /// </summary>
        public static float GetRepeatSuppressionSeconds()
        {
            int minutes = repeatSuppressionMinutesEntry.Value;
            return minutes <= 0 ? 0f : minutes * 60f;
        }

        public static bool GetItemDescriptions()
        {
            return itemDescriptionsEntry.Value;
        }

        public static void SetItemDescriptions(bool enabled)
        {
            itemDescriptionsEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetDebugMode()
        {
            return debugModeEntry.Value;
        }

        public static void SetDebugMode(bool enabled)
        {
            debugModeEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetSpeechLog()
        {
            return speechLogEntry.Value;
        }

        public static void SetSpeechLog(bool enabled)
        {
            speechLogEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetAutoInteract()
        {
            return autoInteractEntry.Value;
        }

        public static void SetAutoInteract(bool enabled)
        {
            autoInteractEntry.Value = enabled;
            category.SaveToFile();
        }

        public static bool GetTutorialTips() => tutorialTipsEntry.Value;

        public static string GetLanguage() => languageEntry.Value;

        public static void SetSpeakAudioCaptions(bool enabled)
        {
            speakAudioCaptionsEntry.Value = enabled;
            category.SaveToFile();
        }
    }
}