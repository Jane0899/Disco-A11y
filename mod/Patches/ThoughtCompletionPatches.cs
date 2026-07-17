using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using AccessibilityMod.Settings;
using AccessibilityMod.Utils;
using Il2Cpp;
using Il2CppSunshine;
using Il2CppSunshine.Metric;
using Il2CppSunshine.Views;

namespace AccessibilityMod.Patches
{
    /// <summary>
    /// Everything around thought research completion and the thought cabinet's own
    /// storytelling (bug #57).
    ///
    /// The problem, as reported by the player: the mod announced "almost done" while a
    /// thought was cooking - and then NOTHING. The research result appeared only
    /// visually (a modal fullscreen splash, ThoughtSplashScreenView), its close button
    /// is mouse-only, and the view opens with no EventSystem selection (all verified
    /// live 17.07.2026). A keyboard player was trapped: they could still walk (the
    /// mod's world navigation does not go through the UI) but every interaction was
    /// swallowed by the invisible modal - "I can walk but not interact".
    ///
    /// Three patches fix the three halves of that experience:
    ///  1. AnnounceSplash (via SetProject + OnEnable): read EVERYTHING the splash shows -
    ///     title, completion description, and the bonus list - not just the name.
    ///  2. OnEnable additionally puts the keyboard selection on the game's own close
    ///     button, so Enter leaves the screen the same way a mouse click would.
    ///  3. ThoughtSlot.OnSelect: while browsing the cabinet, each selected thought tells
    ///     its full story (name, state, description, research time) - "what the cabinet
    ///     says about the thought", as requested.
    /// </summary>
    public static class ThoughtSplashAnnouncer
    {
        // SetProject and OnEnable both run when the splash opens (order depends on the
        // game's flow), and both call in here - the window keeps the player from hearing
        // the same splash twice while still allowing a re-announcement when the game
        // legitimately shows another thought right after (tabbing through multiple
        // completed thoughts uses ChangeShownThought -> SetProject again).
        private static string lastAnnounced = "";
        private static float lastAnnouncedTime;

        public static void AnnounceSplash(ThoughtSplashScreenView view)
        {
            try
            {
                if (view == null) return;
                var project = view.currentProject;
                if (project == null) return; // splash without a thought = nothing to read

                var parts = new List<string>();

                // Title: prefer what the panel actually renders (localized by the game),
                // fall back to the data model if the text field is not filled yet.
                string title = view.titleText != null && !string.IsNullOrWhiteSpace(view.titleText.text)
                    ? view.titleText.text
                    : project.displayName;
                parts.Add(Loc.Get("ThoughtCompletedNoEffect", RTLHelper.FixForScreenReader(title)));

                // The thought's own musing text ("der Gedanke") - the story the game's
                // narrator voice reads on this screen. First live test 17.07.: the
                // player heard the bonuses but reported the THOUGHT itself missing -
                // this is that text. Model data, localized by the game.
                if (!string.IsNullOrWhiteSpace(project.description))
                {
                    parts.Add(RTLHelper.FixForScreenReader(project.description.Trim()));
                }

                // The permanent effect ("Effekt: ...") - the payoff the player waited
                // hours of game time for. Panel text first, model fallback again.
                string completion = view.completionDescriptionText != null && !string.IsNullOrWhiteSpace(view.completionDescriptionText.text)
                    ? view.completionDescriptionText.text
                    : project.completionDescription;
                if (!string.IsNullOrWhiteSpace(completion))
                {
                    parts.Add(RTLHelper.FixForScreenReader(completion.Trim()));
                }

                // The bonus list (propertiesText, e.g. "+1 Logik: ...") - a sighted
                // player reads it off the panel; without this line a blind player never
                // learns the numbers. Only exists on the panel, no model fallback.
                if (view.propertiesText != null && !string.IsNullOrWhiteSpace(view.propertiesText.text))
                {
                    parts.Add(RTLHelper.FixForScreenReader(view.propertiesText.text.Replace("\n", ". ").Trim()));
                }

                string announcement = string.Join(" ", parts);

                // Same-text-within-2s = the second patch of the pair firing, not new info.
                if (announcement == lastAnnounced && UnityEngine.Time.unscaledTime - lastAnnouncedTime < 2f) return;
                lastAnnounced = announcement;
                lastAnnouncedTime = UnityEngine.Time.unscaledTime;

                // Interrupting on purpose: this is a modal takeover the player must
                // acknowledge - exactly the moment to stop whatever else was talking.
                TolkScreenReader.Instance.Speak(announcement, true);
                // Queued (interrupt: false) so the exit hint reads AFTER the content.
                TolkScreenReader.Instance.Speak(Loc.Get("SplashCloseHint"), false);
                MelonLogger.Msg($"[THOUGHT] Splash announced: {project.displayName}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing thought splash: {ex}");
            }
        }
    }

    /// <summary>
    /// SetProject is the game's own "this splash is about this thought" call - it fires
    /// both when the splash first opens and when the player tabs to another completed
    /// thought (ChangeShownThought). Announcing here covers both.
    /// </summary>
    [HarmonyPatch(typeof(ThoughtSplashScreenView), "SetProject")]
    public static class ThoughtSplashScreen_SetProject_Patch
    {
        public static void Postfix(ThoughtSplashScreenView __instance, ThoughtCabinetProject project)
        {
            ThoughtSplashAnnouncer.AnnounceSplash(__instance);
        }
    }

    /// <summary>
    /// OnEnable = the splash is actually visible now. Two jobs:
    ///  - announce the content (covers the flow where SetProject ran before the panel
    ///    texts were filled - here they are final),
    ///  - put the keyboard selection on the game's own close button. The splash opens
    ///    with NO selection (verified live: "ui selected" -> nothing), and the button is
    ///    otherwise mouse-only. With the selection set, Unity's input module maps
    ///    Enter/Space (Submit) to the same click a mouse user makes - no custom close
    ///    logic that could drift out of sync with the game.
    /// </summary>
    [HarmonyPatch(typeof(ThoughtSplashScreenView), "OnEnable")]
    public static class ThoughtSplashScreen_OnEnable_Patch
    {
        public static void Postfix(ThoughtSplashScreenView __instance)
        {
            try
            {
                ThoughtSplashAnnouncer.AnnounceSplash(__instance);

                var button = __instance.buttonClose;
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (button == null || eventSystem == null) return;

                // Select rather than click: the player decides when to leave, we only
                // make Enter reach the same button the mouse pointer would.
                eventSystem.SetSelectedGameObject(button.gameObject);
                MelonLogger.Msg("[THOUGHT] Splash close button selected for keyboard access");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error selecting splash close button: {ex}");
            }
        }
    }

    // NOTE - no Harmony patch for "what the cabinet says about the thought":
    // ThoughtSlot.OnSelect is a VIRTUAL Il2Cpp method, and patching those crashes the
    // game natively (learned the hard way 17.07.2026 - instant process death on the
    // first slot selection; the project worklog #30 documents the same failure mode
    // for OneAxisInputControl.get_WasPressed). The cabinet narration lives in
    // ThoughtCabinetNavigationHandler.CheckSelectedThought instead: a per-frame poll
    // of the EventSystem selection, the same safe pattern the rest of the mod uses.
}
