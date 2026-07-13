using System;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppFortressOccident;

namespace AccessibilityMod.Utils
{
    public static class ObjectNameCleaner
    {
        public static string CleanObjectName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return rawName;
            
            // Remove common Unity prefixes/suffixes
            string cleaned = rawName;
            cleaned = cleaned.Replace("_", " ");
            cleaned = cleaned.Replace("(Clone)", "");
            cleaned = cleaned.Replace("GameObject", "");
            
            // Remove brackets and their contents
            cleaned = Regex.Replace(cleaned, @"\([^)]*\)", "");
            cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", "");
            
            // Clean up extra whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return cleaned;
        }

        // Note on name sources, so nobody re-derives this the hard way:
        // The dialogue database is NOT a name source. Objects that talk to you name a
        // conversation, but its conversant actor is not the object - the actor behind
        // "Kims Paperwork" is *Cuno*, "Tequilas bed messy" collapses to "Bed", and the
        // names come back in English. The game also shows sighted players no object labels
        // at all (Tab draws orbs, not text), so for doors and scenery the level designer's
        // Unity name really is the best that exists.
        // The item database, however, IS a name source - see GetPickupItemName below.
        public static string GetBetterObjectName(MouseOverHighlight obj)
        {
            try
            {
                // Anything you can pick up is a container holding exactly one item, and
                // that item has a proper localized name ("Glastara", not "empty_beer").
                string itemName = GetPickupItemName(obj);
                if (!string.IsNullOrEmpty(itemName))
                {
                    return itemName;
                }

                // Try to get GameEntity name first (more descriptive)
                var entity = obj.GetFirstActive();
                if (entity != null && !string.IsNullOrEmpty(entity.name))
                {
                    return FormatObjectName(entity.name);
                }

                // Fall back to GameObject name
                if (obj.gameObject != null && !string.IsNullOrEmpty(obj.gameObject.name))
                {
                    return FormatObjectName(obj.gameObject.name);
                }

                return "Unknown Object";
            }
            catch
            {
                return "Unknown Object";
            }
        }

        // Walking the item database per object is far too slow to redo on every
        // announcement, and an object's item never changes.
        private static readonly System.Collections.Generic.Dictionary<int, string> pickupNameCache = new();

        /// <summary>
        /// The real, localized name of a pick-up-able object, or null if it isn't one.
        ///
        /// Loot objects are container sources holding item keys; the item database turns
        /// those keys into the names the game itself shows ("Weißes Satinhemd" for
        /// "Dress Shirt Hanging", "Ultimativer Discoblazer" for "Disco-ass blazer").
        /// Only single-item objects are renamed: those *are* the item. A container holding
        /// several things stays under its own name - it's a box, not an item, and its
        /// contents get announced when you open it.
        /// </summary>
        private static string GetPickupItemName(MouseOverHighlight obj)
        {
            try
            {
                int id = obj.GetInstanceID();
                if (pickupNameCache.TryGetValue(id, out var cached)) return cached;

                string resolved = ResolvePickupItemName(obj);
                pickupNameCache[id] = resolved; // null is cached too - don't redo the lookup
                return resolved;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolvePickupItemName(MouseOverHighlight obj)
        {
            var source = obj.GetComponentInParent<Il2CppSunshine.ContainerSource>()
                         ?? obj.GetComponentInChildren<Il2CppSunshine.ContainerSource>();
            var items = source?.containedItems;
            if (items == null || items.Count != 1) return null;

            var item = items[0];
            if (item == null || string.IsNullOrEmpty(item.name)) return null;

            var dbItem = Il2Cpp.InventoryItemList.singleton?.GetByName(item.name);
            string display = dbItem?.displayName;
            if (string.IsNullOrEmpty(display)) return null;

            return RTLHelper.FixForScreenReader(display.Trim());
        }

        /// <summary>Scene changes recycle instance IDs, so the cache must not outlive a scene.</summary>
        public static void ClearPickupNameCache() => pickupNameCache.Clear();

        private static string FormatObjectName(string rawName)
        {
            // Remove common Unity suffixes and prefixes
            string cleaned = rawName;

            // Remove (Clone) suffix
            if (cleaned.EndsWith("(Clone)"))
                cleaned = cleaned.Substring(0, cleaned.Length - 7);

            // Remove trailing underscores only (preserve numbers for distinction)
            while (cleaned.Length > 0 && cleaned[cleaned.Length - 1] == '_')
                cleaned = cleaned.Substring(0, cleaned.Length - 1);

            // Replace underscores with spaces
            cleaned = cleaned.Replace("_", " ");

            // Capitalize first letter of each word
            if (cleaned.Length > 0)
            {
                var words = cleaned.Split(' ');
                for (int i = 0; i < words.Length; i++)
                {
                    if (!string.IsNullOrEmpty(words[i]))
                    {
                        // Don't lowercase if the word is just numbers
                        if (Regex.IsMatch(words[i], @"^\d+$"))
                        {
                            // Keep numbers as-is
                            continue;
                        }
                        words[i] = char.ToUpper(words[i][0]) + (words[i].Length > 1 ? words[i].Substring(1).ToLower() : "");
                    }
                }
                cleaned = string.Join(" ", words);
            }

            return string.IsNullOrEmpty(cleaned) ? "Object" : cleaned;
        }
    }
}