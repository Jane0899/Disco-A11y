using UnityEngine;

namespace AccessibilityMod.Navigation
{
    /// <summary>
    /// Wraps the game's own Il2Cpp.InteractionAreaTrigger - a "walk into this zone and
    /// something happens" component the game uses 176 times, with NO backing world object
    /// when its basicEntity field is unset (conversationIfNoEntity carries the conversation
    /// directly instead). Those instances are invisible to MouseOverHighlight.registry, and
    /// therefore to every menu/category our whole navigation system is built on - a blind
    /// player has no way to even discover they exist, let alone walk to the exact spot.
    ///
    /// Deliberately NOT folded into the existing MouseOverHighlight-typed object lists
    /// (Dictionary<ObjectCategory, List<MouseOverHighlight>> in NavigationStateManager):
    /// that type is threaded through categorization, name-cleaning, door-lock checks and
    /// the game's own click/interact plumbing (InteractFirstActive, GetFirstActive) all of
    /// which assume a MouseOverHighlight and would need to tolerate a second, unrelated
    /// type. A trigger has none of that machinery - it only has Interact(). Modeled after
    /// stardew-access's AccessibleTile (references/stardew-access/.../Tiles/AccessibleTile.cs):
    /// a small, standalone wrapper carrying just what navigation needs (name + position),
    /// kept out of the game's own object-selection type entirely.
    /// </summary>
    public class NavigableAreaTrigger
    {
        public readonly Il2Cpp.InteractionAreaTrigger Trigger;
        public readonly string Name;
        public readonly Vector3 Position;

        public NavigableAreaTrigger(Il2Cpp.InteractionAreaTrigger trigger)
        {
            Trigger = trigger;
            Position = trigger.transform.position;

            // These triggers were never meant to be named out loud - there is no display
            // name field at all (conversationIfNoEntity is an internal conversation ID, not
            // player-facing text, e.g. "car_headlight_signature" - readable-ish, but not
            // meant to be heard). The Unity GameObject name is the best available label,
            // same source our regular object names start from before ObjectNameCleaner
            // prettifies them; a light manual cleanup here keeps this file independent of
            // that MouseOverHighlight-specific cleaner.
            string raw = trigger.gameObject != null ? trigger.gameObject.name : "Area";
            Name = raw.Replace('-', ' ').Replace('_', ' ').Trim();
            if (string.IsNullOrEmpty(Name)) Name = "Area";
        }

        /// <summary>
        /// Fires the trigger directly via its own Interact(), the same call the game makes
        /// when the player's collider physically enters the zone. Bypassing the physical
        /// walk-in requirement entirely is the point: there is no accessible way to aim for
        /// an invisible zone's exact bounds, so selecting it and pressing interact IS the
        /// accessible equivalent of walking into it.
        /// </summary>
        public void Interact()
        {
            Trigger.Interact();
        }
    }
}
