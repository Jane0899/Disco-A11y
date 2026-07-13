using System;
using Il2CppSunshine.Views;
using MelonLoader;
using UnityEngine;
using AccessibilityMod.Settings;

namespace AccessibilityMod.UI
{
    /// <summary>
    /// Says which screen you just opened, and the numbers that only exist at screen level.
    ///
    /// The existing patches announce the focused *element* well (a skill, a thought, a
    /// task), but never the screen around it: opening the character sheet read out
    /// "Encyclopedia +5..." without ever saying you were in the character sheet - or that
    /// you had skill points waiting to be spent. Sighted players read that off the panel;
    /// blind players had no way to learn it, and no way to report it missing either.
    ///
    /// Keyed off ViewController.GetCurrentView(), the game's own "which screen am I on"
    /// state, so this holds for every screen rather than a list of the ones we tested.
    /// (The DiscoPages page system that also ships in the build is dead code on PC -
    /// FindObjectsOfType&lt;DiscoPage&gt; returns 0 even with the character sheet open.)
    /// Numbers come from PlayerCharacter, the game's own model, not from scraping labels.
    /// </summary>
    public static class ScreenAnnouncer
    {
        private const float POLL_INTERVAL = 0.3f;

        private static float lastPoll;
        private static string lastViewName = "";

        public static void Update()
        {
            if (Time.unscaledTime - lastPoll < POLL_INTERVAL) return;
            lastPoll = Time.unscaledTime;

            View view;
            ViewType viewType;
            try
            {
                view = ViewController.GetCurrentView();
                if (view == null)
                {
                    lastViewName = "";
                    return;
                }

                // Il2Cpp hands back the base View type, so the concrete class name is
                // useless here - the game's own ViewType enum is the reliable identity.
                // Between scenes the current view is a stale wrapper whose type throws;
                // that is normal, not an error worth logging every frame.
                viewType = view.GetViewType();
            }
            catch
            {
                lastViewName = "";
                return;
            }

            try
            {
                string viewName = viewType.ToString();

                if (viewName == lastViewName) return;
                lastViewName = viewName;

                // The game switches views constantly while you just play (CLEAR, SPECIAL,
                // DIALOGUE, CUTSCENE...). Only the screens a player deliberately opens are
                // worth saying out loud - those are exactly the ones with a name below.
                if (!IsPlayerScreen(viewType)) return;

                Announce(view, viewType);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SCREEN] {ex.Message}");
            }
        }

        private static void Announce(View view, ViewType viewType)
        {
            string title = GetScreenName(viewType);
            string extra = GetScreenContext(viewType);

            string announcement = string.IsNullOrEmpty(extra) ? title : $"{title}. {extra}";
            TolkScreenReader.Instance.Speak(announcement, true);
            MelonLogger.Msg($"[SCREEN] Opened: {viewType} -> {announcement}");
        }

        /// <summary>
        /// The screen's own heading, in the player's language, taken from the title label
        /// the game renders ("GEDANKENKABINETT"). Falls back to the class name so a screen
        /// without a heading is still announced rather than silently skipped.
        /// </summary>
        /// <summary>Screens a player opens on purpose - the ones worth announcing.</summary>
        private static bool IsPlayerScreen(ViewType viewType) =>
            viewType == ViewType.INVENTORY ||
            viewType == ViewType.CHARACTERSHEET ||
            viewType == ViewType.THOUGHTCABINET ||
            viewType == ViewType.JOURNAL ||
            viewType == ViewType.OPTIONS ||
            viewType == ViewType.SAVE ||
            viewType == ViewType.LOAD ||
            viewType == ViewType.HELPOVERLAY;

        private static string GetScreenName(ViewType viewType) => Loc.Get("Screen_" + viewType);

        /// <summary>
        /// Screen-level numbers a blind player would otherwise never hear. Read from the
        /// game's own PlayerCharacter model, so they stay right even when the panel that
        /// displays them is off screen.
        /// </summary>
        /// <summary>
        /// How many items you are carrying. The game keeps no "my items" list in its model
        /// (Sunshine.Inventory only has actions, and InventoryItemList is the catalogue of
        /// every item that exists), so this counts the filled slots of the open inventory -
        /// exactly what a sighted player sees on the panel.
        /// </summary>
        private static int CountCarriedItems()
        {
            try
            {
                int count = 0;
                foreach (var slot in UnityEngine.Object.FindObjectsOfType<Il2CppDiscoPages.Elements.Inventory.InventoryItemSlot>())
                {
                    if (slot == null || !slot.gameObject.activeInHierarchy) continue;
                    if (string.IsNullOrWhiteSpace(slot.itemName)) continue;
                    count++;
                }
                return count;
            }
            catch
            {
                return -1;
            }
        }

        private static string GetScreenContext(ViewType viewType)
        {
            try
            {
                var player = Il2CppSunshine.Metric.PlayerCharacter.Singleton;
                if (player == null) return "";

                string context = "";

                // Unspent skill points are the one thing on these screens you must act on,
                // and nothing ever said them out loud.
                int skillPoints = player.SkillPoints;
                if (skillPoints > 0 &&
                    (viewType == ViewType.CHARACTERSHEET || viewType == ViewType.THOUGHTCABINET))
                {
                    context = Loc.Get(skillPoints == 1 ? "SkillPointOne" : "SkillPointsMany", skillPoints);
                }

                if (viewType == ViewType.CHARACTERSHEET)
                {
                    string xp = Loc.Get("ExperienceAndLevel", player.XpAmount, player.Level);
                    context = string.IsNullOrEmpty(context) ? xp : $"{context} {xp}";
                }

                if (viewType == ViewType.INVENTORY)
                {
                    int items = CountCarriedItems();
                    if (items >= 0)
                    {
                        string count = Loc.Get(items == 1 ? "ItemCountOne" : "ItemCountMany", items);
                        context = string.IsNullOrEmpty(count) ? context : count;
                    }
                }

                return context;
            }
            catch
            {
                return "";
            }
        }
    }
}
