using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using AccessibilityMod.Settings;

namespace AccessibilityMod.Patches
{
    /// <summary>
    /// Speaks the world-container panel (Sunshine.Container - the "take all / close"
    /// popup that opens when clicking a lootable box in the world). The panel itself
    /// is pure icons plus two buttons and was completely silent for screen reader
    /// users. Announces the contents once when the panel opens, each item as it is
    /// taken, and when the panel closes. Taking everything is wired to the mod's
    /// interact key in SmartNavigationSystem (press it again while the panel is open).
    /// </summary>
    [HarmonyPatch]
    public static class ContainerPatches
    {
        // SetItems also reruns while the panel stays open (e.g. after taking a single
        // item rebuilds the grid) - only the first run after opening announces contents.
        private static bool panelAnnounced;

        // A one-item box is not worth a panel: the moment it opens, take the item and be
        // done, so looting a single thing is one keypress instead of two. Done on the NEXT
        // frame, never from inside SetItems - taking mutates the item list the game is in the
        // middle of rebuilding, and re-entering it there is a crash.
        private static bool pendingAutoTake;
        // Suppress the "container closed" line for that auto-take, so a one-item pickup is
        // just the game's own "item gained" notification and nothing else - smooth, not chatty.
        private static bool suppressCloseOnce;

        public static bool IsContainerPanelOpen
        {
            get
            {
                try { return Il2CppSunshine.Container.IsActive(); }
                catch { return false; }
            }
        }

        /// <summary>Clicks the panel's own take-all button (or closes an empty panel). Returns false when no panel is open.</summary>
        public static bool TakeAllFromOpenContainer()
        {
            try
            {
                if (!Il2CppSunshine.Container.IsActive()) return false;
                var panel = Il2CppSunshine.Container.singleton;
                var takeAll = panel?.takeAllButton;
                if (takeAll != null && takeAll.gameObject.activeInHierarchy && takeAll.interactable)
                {
                    TolkScreenReader.Instance.Speak(Loc.Get("ContainerTakeAll"), true);
                    takeAll.onClick.Invoke();
                }
                else
                {
                    panel?.OnCloseButton();
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CONTAINER] Take-all failed: {ex}");
                return true; // a panel was open - don't fall through to world interaction
            }
        }

        [HarmonyPatch(typeof(Il2CppSunshine.Container), "SetItems")]
        [HarmonyPostfix]
        public static void SetItemsPostfix(Il2CppSunshine.Container __instance)
        {
            try
            {
                if (panelAnnounced) return;
                panelAnnounced = true;

                var names = new List<string>();
                var items = __instance.Source?.containedItems;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.name)) continue;
                        names.Add(ResolveItemName(item.name));
                    }
                }

                // Exactly one thing in the box: skip the panel, take it on the next frame.
                if (names.Count == 1)
                {
                    pendingAutoTake = true;
                    return;
                }

                string contents = names.Count == 0 ? Loc.Get("ContainerEmpty") : string.Join(", ", names);
                TolkScreenReader.Instance.Speak(
                    Loc.Get("ContainerOpened", contents, KeyBindings.SpeakableName(GameKey.InteractWithSelected)), true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CONTAINER] Error announcing container: {ex}");
            }
        }

        /// <summary>
        /// Performs the deferred single-item take, one frame after the panel opened. Runs from
        /// the mod's OnUpdate, safely outside the game's grid rebuild.
        /// </summary>
        public static void Update()
        {
            if (!pendingAutoTake) return;
            pendingAutoTake = false;

            try
            {
                if (!Il2CppSunshine.Container.IsActive()) return;
                var panel = Il2CppSunshine.Container.singleton;
                var items = panel?.Source?.containedItems;

                // Take everything - which here is the one item - through the game's own
                // take-all, exactly as pressing the interact key on an open panel does.
                var takeAll = panel?.takeAllButton;
                if (takeAll != null && takeAll.gameObject.activeInHierarchy && takeAll.interactable)
                {
                    suppressCloseOnce = true;
                    // Just a short confirmation that the auto-take fired. The name is NOT
                    // repeated here on purpose - the game raises its own "item gained"
                    // notification (spoken by NotificationVocalizationPatches) which carries
                    // the properly localized name, and saying it twice is the annoyance this
                    // whole feature is meant to remove.
                    TolkScreenReader.Instance.Speak(Loc.Get("ContainerSingleTaken"), true);
                    takeAll.onClick.Invoke();
                }
                else
                {
                    panel?.OnCloseButton();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CONTAINER] Auto-take of single item failed: {ex}");
            }
        }

        // Taking an item is not announced here: the game raises its own "item gained"
        // notification, which the mod already speaks (NotificationVocalizationPatches)
        // and which carries the properly localized item name.

        [HarmonyPatch(typeof(Il2CppSunshine.Container), "OnDisable")]
        [HarmonyPostfix]
        public static void OnDisablePostfix()
        {
            pendingAutoTake = false;
            if (!panelAnnounced) return;
            panelAnnounced = false;

            // The auto-take already gave its own confirmation; don't tack "container closed"
            // onto a one-item pickup.
            if (suppressCloseOnce)
            {
                suppressCloseOnce = false;
                return;
            }

            try
            {
                TolkScreenReader.Instance.Speak(Loc.Get("ContainerClosed"), false);
            }
            catch { /* scene teardown - stay silent */ }
        }

        /// <summary>
        /// A container only stores an item's internal key ("music_whirling_smallest_church"),
        /// which is meaningless when spoken. The game's own item list resolves that key to
        /// the localized display name ("Empty tape reel"); fall back to the readable form of
        /// the key if an item isn't in the list.
        /// </summary>
        private static string ResolveItemName(string key)
        {
            try
            {
                var item = Il2Cpp.InventoryItemList.singleton?.GetByName(key);
                var display = item?.displayName;
                if (!string.IsNullOrWhiteSpace(display)) return Utils.RTLHelper.FixForScreenReader(display);
            }
            catch { /* item list not loaded yet - fall through */ }

            return key.Replace('_', ' ').Replace('-', ' ').Trim();
        }
    }
}
