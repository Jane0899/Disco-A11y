using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;
using Il2CppFortressOccident;
using AccessibilityMod.Settings;

namespace AccessibilityMod.Utils
{
    /// <summary>
    /// Says every name the selected object has, from every source we know of, each one
    /// labelled with where it came from.
    ///
    /// This exists because of an asymmetry: the player cannot see the object, so when an
    /// announcement sounds wrong ("that is not what this thing is called"), there is no way
    /// to check it against the screen. This key makes the sources audible so the player can
    /// judge them - and it deliberately only reports. Switching the announcements over to
    /// the dialogue database was tried and abandoned: it names the conversation's actor,
    /// not the object, so "Kims Paperwork" came out as "Cuno". A toggle would just hand the
    /// player wrong names they cannot verify; this hands them the evidence instead.
    /// </summary>
    public static class NameSourceReporter
    {
        public static void AnnounceForSelected(Navigation.SmartNavigationSystem navigation)
        {
            try
            {
                var selected = navigation?.StateManager?.GetCurrentSelectedObject();
                if (selected == null)
                {
                    TolkScreenReader.Instance.Speak(Loc.Get("NameSourcesNoSelection"), true);
                    return;
                }

                var lines = new List<string> { Loc.Get("NameSourcesHeader") };

                lines.Add(Loc.Get("NameSourceSpoken", ObjectNameCleaner.GetBetterObjectName(selected)));

                var unityName = selected.gameObject != null ? selected.gameObject.name : null;
                if (!string.IsNullOrEmpty(unityName))
                {
                    lines.Add(Loc.Get("NameSourceUnity", unityName));
                }

                var entity = selected.GetFirstActive();
                if (entity != null && !string.IsNullOrEmpty(entity.name) && entity.name != unityName)
                {
                    lines.Add(Loc.Get("NameSourceEntity", entity.name));
                }

                var itemName = ObjectNameCleaner.GetPickupItemName(selected);
                lines.Add(Loc.Get("NameSourceItem",
                    string.IsNullOrEmpty(itemName) ? Loc.Get("NameSourceNone") : itemName));

                var conversant = GetConversationActor(selected);
                lines.Add(Loc.Get("NameSourceDialogue",
                    string.IsNullOrEmpty(conversant) ? Loc.Get("NameSourceNone") : conversant));

                var report = string.Join(" ", lines);
                TolkScreenReader.Instance.Speak(report, true);
                MelonLogger.Msg($"[NAMES] {report}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[NAMES] {ex.Message}");
            }
        }

        /// <summary>
        /// What the dialogue data would call this object: the actor its conversation
        /// belongs to, which the entity itself carries. Reported only so it can be heard to
        /// be wrong - the actor is who talks to you, not what you are looking at.
        /// </summary>
        private static string GetConversationActor(MouseOverHighlight obj)
        {
            try
            {
                var entity = obj.GetFirstActive()?.TryCast<BasicEntity>();
                if (entity == null) return null;

                if (!string.IsNullOrEmpty(entity.conversationActorName)) return entity.conversationActorName;
                return string.IsNullOrEmpty(entity.conversation) ? null : entity.conversation;
            }
            catch
            {
                return null;
            }
        }
    }
}
