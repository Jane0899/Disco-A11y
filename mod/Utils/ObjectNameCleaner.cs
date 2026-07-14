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
        public static string GetPickupItemName(MouseOverHighlight obj)
        {
            try
            {
                // Resolved live, not cached: a cached name outlives the loot. The player
                // emptied the tape pile and it kept announcing itself as "Leere
                // Tonbandspule, 1 of 1" in the loot list - an item that no longer existed.
                // The lookup is a component walk plus one dictionary hit, and it only runs
                // during scans and selection changes, never per frame.
                return ResolvePickupItemName(obj);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Whether there is actually something to take here: true/false for containers,
        /// null for objects that are not containers at all.
        /// </summary>
        public static bool? HasLootableContents(MouseOverHighlight obj)
        {
            try
            {
                var source = obj.GetComponentInParent<Il2CppSunshine.ContainerSource>()
                             ?? obj.GetComponentInChildren<Il2CppSunshine.ContainerSource>();
                if (source == null) return null;
                var items = source.containedItems;
                return items != null && items.Count > 0;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The game's own description of the item lying there, or null.
        ///
        /// Item names in this game are proper nouns from a world nobody outside it knows:
        /// "Glastara, 6 meters west" tells a blind player exactly nothing, while a sighted
        /// one reads the tooltip. This is that tooltip.
        /// </summary>
        public static string GetPickupItemDescription(MouseOverHighlight obj)
        {
            try
            {
                var dbItem = ResolvePickupItem(obj);
                var description = dbItem?.description;
                if (string.IsNullOrWhiteSpace(description)) return null;

                return RTLHelper.FixForScreenReader(StripTags(description).Trim());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Rich-text markup would otherwise be spoken out as literal angle brackets.</summary>
        private static string StripTags(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        }

        /// <summary>The single item a pickup holds, looked up in the game's item database.</summary>
        private static Il2CppSunshine.Metric.InventoryItem ResolvePickupItem(MouseOverHighlight obj)
        {
            var source = obj.GetComponentInParent<Il2CppSunshine.ContainerSource>()
                         ?? obj.GetComponentInChildren<Il2CppSunshine.ContainerSource>();
            var items = source?.containedItems;
            if (items == null || items.Count != 1) return null;

            var item = items[0];
            if (item == null || string.IsNullOrEmpty(item.name)) return null;

            return Il2Cpp.InventoryItemList.singleton?.GetByName(item.name);
        }

        private static string ResolvePickupItemName(MouseOverHighlight obj)
        {
            string display = ResolvePickupItem(obj)?.displayName;
            if (string.IsNullOrEmpty(display)) return null;

            return RTLHelper.FixForScreenReader(display.Trim());
        }

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