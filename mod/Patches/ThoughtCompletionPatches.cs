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
    /// AnnounceSplash (via the SetProject + OnEnable patches below) reads EVERYTHING the
    /// splash shows - title, the thought's own musing text, completion effect and the
    /// bonus list - not just the name. The keyboard close itself lives in
    /// InputManager.TryCloseThoughtSplash (Enter / the interact key), which is the single,
    /// verified-working exit path.
    ///
    /// The third half - "what the cabinet says about the thought" while BROWSING the
    /// cabinet - is deliberately NOT a patch here: ThoughtSlot.OnSelect is a virtual
    /// Il2Cpp method whose patching crashes the game (see the NOTE at the bottom of this
    /// file), so that narration lives in ThoughtCabinetNavigationHandler.CheckSelectedThought
    /// as a per-frame EventSystem poll instead.
    /// </summary>
    public static class ThoughtSplashAnnouncer
    {
        // SetProject and OnEnable both run when the splash opens (order depends on the
        // game's flow), and both call in here. Dedup on the THOUGHT, not on the rendered
        // text: the two calls can read the panel labels at different render stages, so
        // the text can differ even though it is the same thought - keying on text would
        // let that difference slip through and announce the same result twice. A
        // genuinely different thought (tabbing to another completed one via
        // ChangeShownThought -> SetProject) has a different key and re-announces.
        private static string lastAnnouncedThought = "";
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

                // The exit hint is PART of the same utterance, not a second Speak call:
                // a queued follow-up line gets promoted to interrupting when the player's
                // global speech-interrupt setting is on, and beheaded the whole result
                // read after ~0 ms (PR review finding 3). One utterance cannot interrupt
                // itself. The hint renders the LIVE binding (finding 10) - remapping the
                // key updates the spoken text automatically.
                parts.Add(Loc.Get("SplashCloseHint", KeyBindings.SpeakableName(GameKey.CloseSplash)));

                string announcement = string.Join(" ", parts);

                // Same thought within 2s = the second patch of the pair firing (or the
                // game re-running SetProject), not new info. Keyed on the thought's own
                // name, so a differently-rendered-but-same thought is still deduped.
                string thoughtKey = project.displayName ?? "";
                if (thoughtKey == lastAnnouncedThought && UnityEngine.Time.unscaledTime - lastAnnouncedTime < 2f) return;
                lastAnnouncedThought = thoughtKey;
                lastAnnouncedTime = UnityEngine.Time.unscaledTime;

                // Interrupting on purpose: this is a modal takeover the player must
                // acknowledge - exactly the moment to stop whatever else was talking.
                // (Content + exit hint travel in this ONE call, see above.)
                TolkScreenReader.Instance.Speak(announcement, true);
                MelonLogger.Msg($"[THOUGHT] Splash announced: {project.displayName}");

                // The game is now telling the player about this thought itself, so the
                // "a thought is finished, press T" hint has served its purpose and must
                // not fire behind the splash (J11).
                PendingThoughtWatcher.MarkSplashSeen(project.displayName);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing thought splash: {ex}");
            }
        }
    }

    /// <summary>
    /// J11: a thought that finished cooking and was never confirmed blocks EVERY
    /// interaction in the world, and nothing says so.
    ///
    /// What the player experienced (25.09.2026, reconstructed from the log): the last
    /// working interaction was at 18:46:41, from 19:01:13 on every single one returned
    /// false - the kitchen back door, a window, the courtyard exit, Kim, Lena. Walking,
    /// announcements, pathfinding, key handling all kept working, so the world felt
    /// completely normal and simply refused to respond. At 19:18 she opened the thought
    /// cabinet by hand, the research splash for "Weiße Trauer" appeared, she confirmed
    /// it - and at 19:19:53 interaction worked again.
    ///
    /// The game's own name for the mechanism is telling: ThoughtManager has a public
    /// static WillShowSplashScreenInstead(). With a mouse this is invisible, because the
    /// next click anywhere pops the splash INSTEAD of interacting. Our keyboard path
    /// calls MouseOverHighlight.InteractFirstActive directly, gets a bare false, and
    /// nothing ever shows the splash. The only other clues are visual: an orange dot on
    /// the cabinet button. A blind player cannot possibly find this.
    ///
    /// So this watcher polls for "a thought is finished and still unconfirmed" and says
    /// so, once, in words, naming the game's own cabinet key. It also answers the second
    /// half of the problem: while it is waiting, a failed interaction stops claiming the
    /// object "cannot be used right now" and names the actual cause instead (see
    /// SmartNavigationSystem.InteractWithSelectedObject).
    ///
    /// Deliberately keyed off global runtime signals, never off scene or thought names,
    /// per the project rule - the same code holds in every area of the game.
    /// </summary>
    public static class PendingThoughtWatcher
    {
        // Once a second is plenty: the flag changes at most once every few in-game hours,
        // and the per-poll work includes a FindObjectsOfType scan we do not want on every
        // frame. Unscaled time, so it keeps ticking while the game is paused behind a
        // fullscreen view.
        private const float POLL_SECONDS = 1f;
        private static float nextPollTime;

        // Last polled answer to "is a finished thought blocking interaction right now".
        private static bool waiting;

        // Display name of that thought, when we could identify it. May be null while
        // waiting is true: the block is a global flag, the name is a best effort.
        private static string waitingName;

        // The thought we already spoke about, so the announcement happens once and not
        // every second. Cleared when the block lifts, so the next finished thought is
        // announced again.
        private static string announcedName;

        // Held back because the player is in a conversation. Thoughts finish DURING
        // dialogue (that is exactly what happened on 25.09: the Kryptide conversation
        // ended at 18:59:55, the block was live by 19:01), and an announcement spoken
        // into a running dialogue is talked straight over - the same reason area
        // descriptions wait (see AccessibilityMod.SpeakPendingDescriptionIfReady).
        private static bool announcementPending;

        // Stand-in key for "announced, but we never learned which thought it was" - so the
        // once-only rule still holds when the name is unknown.
        private const string UNNAMED = "\u0000unnamed";

        // The thoughts that were cooking at the previous poll, by display name. A thought
        // that was cooking and suddenly is not IS the one that just finished - which
        // recovers the name even when the only signal that fired is the game's global
        // splash flag, and the per-thought `fresh` scan came up empty.
        private static readonly System.Collections.Generic.HashSet<string> lastCooking =
            new System.Collections.Generic.HashSet<string>();

        // How often the cooking snapshot is refreshed while nothing is going on.
        private const float SNAPSHOT_SECONDS = 30f;
        private static float nextSnapshotTime;

        // The character sheet carries FreshCount. Held on to rather than looked up every
        // second: FindObjectOfType is exactly the kind of scan the cheap gate exists to
        // avoid. Re-found when it goes away (scene change, load).
        private static Il2CppSunshine.Metric.CharacterSheet cachedSheet;

        /// <summary>
        /// The game's count of thought-cabinet entries the player has not looked at yet -
        /// the orange dot as a number. Returns -1 when it cannot be read, which callers
        /// treat as "unknown, look properly" rather than "nothing there".
        /// </summary>
        private static int ReadFreshCount()
        {
            try
            {
                if (cachedSheet == null)
                {
                    cachedSheet = UnityEngine.Object.FindObjectOfType<Il2CppSunshine.Metric.CharacterSheet>();
                }
                if (cachedSheet == null || cachedSheet.thoughts == null) return -1;
                return cachedSheet.thoughts.FreshCount;
            }
            catch
            {
                cachedSheet = null;
                return -1;
            }
        }

        /// <summary>
        /// True while a finished thought is waiting for confirmation and the game is
        /// therefore refusing interactions. Read by the interaction path to explain a
        /// failure instead of just reporting it.
        /// </summary>
        public static bool IsBlockingInteraction => waiting;

        /// <summary>Per-frame entry point, called from AccessibilityMod.OnUpdate.</summary>
        public static void Update()
        {
            try
            {
                if (UnityEngine.Time.unscaledTime < nextPollTime) return;
                nextPollTime = UnityEngine.Time.unscaledTime + POLL_SECONDS;

                bool nowWaiting = CheckWaiting(out string name, out bool splashFlag, out bool freshFlag);

                if (nowWaiting && !waiting)
                {
                    // Rising edge: a thought just finished. Log the two signals SEPARATELY,
                    // not just the combined answer - which of them actually marks the state
                    // is still an open question, and this line is how the next real session
                    // settles it without anyone having to reproduce the situation on
                    // purpose. (Baseline measured live on 25.09 with nothing waiting:
                    // WillShowSplashScreenInstead=False, FreshCount=0, and every finished
                    // thought fresh=False - so neither signal is stuck on by default.)
                    MelonLogger.Msg($"[THOUGHT] Finished thought waiting for confirmation: {name ?? "(name unknown)"} "
                                  + $"| WillShowSplashScreenInstead={splashFlag} | freshFinishedThought={freshFlag}");
                    announcementPending = true;
                }
                else if (!nowWaiting && waiting)
                {
                    MelonLogger.Msg("[THOUGHT] Block lifted - thought confirmed");
                    announcedName = null;
                    announcementPending = false;
                    waitingName = null;
                }

                waiting = nowWaiting;

                // Keep a name once we have one. Both ways of learning it are momentary:
                // the `fresh` flag clears when the player looks, and the "left COOKING
                // since the last poll" fallback only matches on the single poll where the
                // transition happened. Overwriting with null a second later would strip
                // the name off an announcement that is still waiting for the dialogue to
                // end. Cleared on the falling edge above, so the next thought starts fresh.
                if (name != null) waitingName = name;

                SpeakIfReady();
            }
            catch (Exception ex)
            {
                // A diagnostic that throws every second would drown the log the way the
                // thought cabinet view check already does. Report once, then stand down.
                MelonLogger.Error($"[THOUGHT] Pending-thought watch failed, disabling: {ex.Message}");
                nextPollTime = float.MaxValue;
            }
        }

        /// <summary>
        /// Speaks the pending announcement as soon as the player can actually hear it:
        /// no dialogue running. Same "wait for control" rule the area descriptions use.
        /// </summary>
        private static void SpeakIfReady()
        {
            if (!announcementPending) return;
            if (UI.DialogStateManager.IsDialogUiActive) return;

            if (waitingName != null && waitingName == announcedName) { announcementPending = false; return; }

            announcedName = waitingName ?? UNNAMED;
            announcementPending = false;

            string key = Settings.GameKeybindConflictChecker.GetGameKeyFor("ThoughtCabinet");

            // Not being able to name the thought must never swallow the announcement:
            // the whole point of J11 is that the player is otherwise left in a world
            // that has silently stopped responding. Without a name the sentence just
            // drops it - "a thought has finished" still says everything that has to be
            // done about it.
            string message;
            if (waitingName == null)
            {
                MelonLogger.Msg("[THOUGHT] Waiting thought could not be named - announcing without it");
                message = key != null
                    ? Loc.Get("ThoughtReadyUnnamed", key)
                    : Loc.Get("ThoughtReadyUnnamedNoKey");
            }
            else
            {
                message = key != null
                    ? Loc.Get("ThoughtReadyToConfirm", waitingName, key)
                    : Loc.Get("ThoughtReadyToConfirmNoKey", waitingName);
            }

            // Interrupting: from this moment the world has stopped responding, and
            // everything the player tries until they act on this is wasted effort.
            TolkScreenReader.Instance.Speak(message, true);
        }

        /// <summary>
        /// The sentence a failed interaction should say instead of "cannot interact with
        /// X right now" - which named the symptom and hid the cause. Null when no thought
        /// is waiting, i.e. when the failure has some other reason.
        /// </summary>
        public static string GetBlockedInteractionMessage(string objectName)
        {
            if (!waiting) return null;

            string key = Settings.GameKeybindConflictChecker.GetGameKeyFor("ThoughtCabinet");
            return key != null
                ? Loc.Get("ThoughtBlocksInteraction", objectName, key)
                : Loc.Get("ThoughtBlocksInteractionNoKey", objectName);
        }

        /// <summary>
        /// Called when the splash actually opened: the player is now being told about the
        /// thought by the game itself, so our hint has done its job and must not repeat.
        /// </summary>
        public static void MarkSplashSeen(string thoughtName)
        {
            announcedName = thoughtName;
            announcementPending = false;
        }

        /// <summary>
        /// The actual state question, kept in one place so the live test has a single
        /// thing to confirm.
        ///
        /// Two independent signals, OR-ed on purpose:
        ///   1. ThoughtManager.WillShowSplashScreenInstead() - the game's own flag, and
        ///      by its name the exact thing that replaces an interaction with the splash.
        ///   2. a thought that is finished (DISCOVERED or FIXED) and still carries the
        ///      game's own "fresh" flag - the orange dot, i.e. "not looked at yet".
        /// Signal 2 also supplies the name. State alone is provably NOT enough: after the
        /// block was resolved on 25.09, two thoughts sat at FIXED with nothing blocked
        /// (read live over the dev bridge).
        /// </summary>
        private static bool CheckWaiting(out string name, out bool splashPending, out bool freshFinished)
        {
            name = null;
            freshFinished = false;

            splashPending = false;
            try
            {
                splashPending = Il2CppSunshine.ThoughtManager.WillShowSplashScreenInstead();
            }
            catch
            {
                // No ThoughtManager yet (main menu, loading) - not an error, just "no".
            }

            // Cheap gate before the expensive part. Scanning all ~53 thought objects once
            // a second to answer a question whose answer changes every few in-game HOURS
            // is waste (the player said so, and she is right). Both of these are single
            // field reads off objects we already hold:
            //   - the game's global splash flag, read above,
            //   - CharacterThoughts.FreshCount, the count behind the orange dot.
            // FreshCount counts every unseen thought, including a newly GAINED one that
            // blocks nothing - so it cannot decide the question, only open the gate. When
            // it is 0 and no splash is pending, there is provably nothing to look for and
            // the scan is skipped entirely. Unreadable (-1) also opens the gate: better a
            // scan too many than a missed block.
            int freshCount = ReadFreshCount();
            bool somethingIsNew = splashPending || freshCount != 0;

            // Even with nothing new, refresh the cooking snapshot now and then: it is what
            // names the finished thought if the `fresh` flag is not what marks the state,
            // and a snapshot from an hour ago would name the wrong one.
            bool timeForSnapshot = UnityEngine.Time.unscaledTime >= nextSnapshotTime;
            if (!somethingIsNew && !timeForSnapshot) return false;
            if (timeForSnapshot) nextSnapshotTime = UnityEngine.Time.unscaledTime + SNAPSHOT_SECONDS;

            // One scan, two jobs: find the finished-but-unseen thought (the second signal,
            // and the announcement's name), and note which thoughts are cooking right now
            // so the NEXT poll can tell which one just stopped.
            var cookingNow = new System.Collections.Generic.HashSet<string>();
            bool scanned = false;
            try
            {
                var projects = UnityEngine.Object.FindObjectsOfType<ThoughtCabinetProject>(true);
                scanned = true;
                foreach (var p in projects)
                {
                    if (p == null) continue;

                    if (p.state == ThoughtState.COOKING)
                    {
                        var cookingName = p.displayName;
                        if (!string.IsNullOrEmpty(cookingName)) cookingNow.Add(cookingName);
                        continue;
                    }

                    if (p.state != ThoughtState.DISCOVERED && p.state != ThoughtState.FIXED) continue;
                    if (!p.fresh) continue;

                    // First finished-and-unseen thought wins; there is normally only one.
                    if (name == null)
                    {
                        name = p.displayName;
                        freshFinished = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[THOUGHT] Could not scan thought projects: {ex.Message}");
            }

            // Fallback name: the game's global flag says a splash is pending, but no
            // thought carries `fresh`. Whatever left COOKING since the last poll is the
            // thought in question.
            if (name == null && splashPending && scanned)
            {
                foreach (var wasCooking in lastCooking)
                {
                    if (!cookingNow.Contains(wasCooking)) { name = wasCooking; break; }
                }
            }

            if (scanned)
            {
                lastCooking.Clear();
                foreach (var c in cookingNow) lastCooking.Add(c);
            }

            return freshFinished || splashPending;
        }
    }

    /// <summary>
    /// SetThoughtProject is the game's own "this splash is about this thought" call - it
    /// fires both when the splash first opens and when the player tabs to another
    /// completed thought. Announcing here covers both.
    ///
    /// The method name matters: this patch targeted "SetProject" for months and therefore
    /// never applied. Harmony said so on every single start ("Could not find method for
    /// type ThoughtSplashScreenView and name SetProject"), and the exception escaped
    /// PatchAll, so the mod logged a failed-to-patch error at boot as well. The real name
    /// is SetThoughtProject (checked against the game's class dump). Consequence while it
    /// was broken: only the FIRST thought of a splash was announced (that one comes from
    /// the OnEnable patch below) - tabbing to a second finished thought stayed silent.
    /// Lesson: a patch that never applies fails loudly in the log and silently in the
    /// game; read the boot log after adding one.
    /// </summary>
    [HarmonyPatch(typeof(ThoughtSplashScreenView), "SetThoughtProject")]
    public static class ThoughtSplashScreen_SetProject_Patch
    {
        public static void Postfix(ThoughtSplashScreenView __instance, ThoughtCabinetProject project)
        {
            ThoughtSplashAnnouncer.AnnounceSplash(__instance);
        }
    }

    /// <summary>
    /// OnEnable = the splash is actually visible now. Announce the content (this also
    /// covers the flow where SetProject ran before the panel texts were filled - here
    /// they are final).
    ///
    /// We deliberately do NOT select the close button here. An earlier version did, to
    /// let Unity's Submit route Enter to the button - but the keyboard close is handled
    /// explicitly in InputManager.TryCloseThoughtSplash (which invokes buttonClose.onClick
    /// itself), so selecting the button as well would give Enter two paths to onClick and
    /// could run the accept bookkeeping (SetThoughtStateAndGoBack) twice per press
    /// (raised in PR review). One path only: the explicit one, which is the verified-
    /// working close (Unity's Submit alone did not close the splash in live testing).
    /// </summary>
    [HarmonyPatch(typeof(ThoughtSplashScreenView), "OnEnable")]
    public static class ThoughtSplashScreen_OnEnable_Patch
    {
        public static void Postfix(ThoughtSplashScreenView __instance)
        {
            ThoughtSplashAnnouncer.AnnounceSplash(__instance);
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
