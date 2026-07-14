using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using Il2CppSunshine.Journal;
using AccessibilityMod.Settings;

namespace AccessibilityMod.UI
{
    /// <summary>
    /// Keyboard access to the journal's map tab.
    ///
    /// The map's travel destinations are mouse-only: they are Selectables, but the
    /// EventSystem's arrow-key navigation never reaches them (selection stays stuck on
    /// the task list), so a keyboard player could open the map with M and then do
    /// nothing with it at all. The announcements for the buttons already exist
    /// (MapPatches speaks them on selection) - what was missing is a way to put the
    /// selection there without a mouse.
    ///
    /// The regular object-cycling keys move through the destinations and the interact
    /// key travels; inside the map those keys have no world to act on anyway, so
    /// reusing them costs nothing and matches what the player already knows.
    /// </summary>
    public static class MapNavigationHandler
    {
        private static int index = -1;
        private static bool wasOpen;
        private static float lastPoll;

        /// <summary>
        /// Announces the map tab when it appears. The screen announcer cannot do it:
        /// tasks and map are both just "JOURNAL" to the view system, and only the map
        /// gets the extra how-to (its keys behave differently from every other screen).
        /// </summary>
        public static void Update()
        {
            if (Time.unscaledTime - lastPoll < 0.3f) return;
            lastPoll = Time.unscaledTime;

            bool open = IsMapOpen;
            if (open && !wasOpen)
            {
                int count = ActiveButtons().Count;
                TolkScreenReader.Instance.Speak(
                    Loc.Get("MapOpened", count,
                        KeyBindings.SpeakableName(GameKey.CycleForward),
                        KeyBindings.SpeakableName(GameKey.InteractWithSelected)),
                    false, AnnouncementCategory.Queueable);
            }
            if (!open) index = -1;
            wasOpen = open;
        }

        /// <summary>The map tab is on screen exactly when its travel buttons are alive.</summary>
        public static bool IsMapOpen
        {
            get
            {
                try
                {
                    foreach (var button in ActiveButtons())
                    {
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static List<QuicktravelButton> ActiveButtons()
        {
            var result = new List<QuicktravelButton>();

            var dict = QuicktravelController.QuicktravelButtonsDict;
            if (dict == null) return result;

            foreach (var kvp in dict)
            {
                var button = kvp.Value;
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                result.Add(button);
            }

            // Dictionary order is arbitrary; sort by marker so the cycle order is stable
            // between presses and between sessions.
            result.Sort((a, b) => string.CompareOrdinal(a.locationMarker, b.locationMarker));
            return result;
        }

        public static void CycleDestination(bool backward)
        {
            try
            {
                var buttons = ActiveButtons();
                if (buttons.Count == 0)
                {
                    TolkScreenReader.Instance.Speak(Loc.Get("MapNoDestinations"), true);
                    return;
                }

                index = backward
                    ? (index <= 0 ? buttons.Count - 1 : index - 1)
                    : (index + 1) % buttons.Count;

                var button = buttons[index];

                // Select() routes through the EventSystem, so the existing OnSelect patch
                // does the announcing - one voice for mouse and keyboard alike.
                EventSystem.current?.SetSelectedGameObject(button.gameObject);
                TolkScreenReader.Instance.Speak($"{index + 1} / {buttons.Count}.", false, AnnouncementCategory.Queueable);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MAP] Cycle failed: {ex.Message}");
            }
        }

        public static void TravelToSelected()
        {
            try
            {
                var buttons = ActiveButtons();
                if (index < 0 || index >= buttons.Count)
                {
                    TolkScreenReader.Instance.Speak(Loc.Get("MapNothingSelected"), true);
                    return;
                }

                var button = buttons[index];

                if (button.CheckTequilaInActivationRadius())
                {
                    TolkScreenReader.Instance.Speak(Loc.Get("MapAlreadyHere"), true);
                    return;
                }

                // The button's own click path (the same one the mouse takes), so every
                // in-game rule about when travel is allowed keeps applying.
                var pointer = new PointerEventData(EventSystem.current);
                button.OnPointerDown(pointer);
                TolkScreenReader.Instance.Speak(
                    Loc.Get("MapTravelling",
                        Patches.QuicktravelButton_OnSelect_Patch.GetFriendlyLocationName(button.locationMarker)),
                    true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MAP] Travel failed: {ex.Message}");
            }
        }

    }
}
