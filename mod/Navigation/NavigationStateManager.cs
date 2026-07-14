using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using Il2CppFortressOccident;
using AccessibilityMod.Utils;
using AccessibilityMod.Settings;

namespace AccessibilityMod.Navigation
{
    public enum SortingMode
    {
        Distance,
        Directional
    }

    public class NavigationStateManager
    {
        private Dictionary<ObjectCategory, List<MouseOverHighlight>> categorizedObjects = new Dictionary<ObjectCategory, List<MouseOverHighlight>>();
        private ObjectCategory currentCategory = ObjectCategory.NPCs;
        private int selectedObjectIndex = -1;
        private SortingMode currentSortingMode = SortingMode.Directional;

        public ObjectCategory CurrentCategory => currentCategory;
        public int SelectedObjectIndex => selectedObjectIndex;
        public bool HasSelection => selectedObjectIndex >= 0 && HasObjectsInCategory(currentCategory);
        public SortingMode CurrentSortingMode => currentSortingMode;

        public NavigationStateManager()
        {
            // Initialize all categories
            foreach (ObjectCategory category in Enum.GetValues(typeof(ObjectCategory)))
            {
                categorizedObjects[category] = new List<MouseOverHighlight>();
            }
        }

        public void UpdateCategorizedObjects(Vector3 playerPos, ObjectCategory targetCategory)
        {
            try
            {
                lastScanTime = Time.unscaledTime;
                // Get current objects from registry
                var registry = MouseOverHighlight.registry;
                if (registry == null || registry.Count == 0)
                {
                    ClearAllCategories();
                    return;
                }

                // Clear and populate categorized objects
                ClearAllCategories();

                // Special handling for Everything category - add ALL objects within range
                if (targetCategory == ObjectCategory.Everything)
                {
                    float maxDistance = ObjectCategorizer.GetMaxDistanceForCategory(ObjectCategory.Everything);
                    foreach (var obj in registry)
                    {
                        if (!Utils.GameObjectUtils.IsActuallyInWorld(obj)) continue;

                        float distance = Vector3.Distance(playerPos, obj.transform.position);
                        if (distance > maxDistance) continue;
                        if (ReachabilityChecker.IsReachable(playerPos, obj.transform.position) == false) continue;

                        // Add to Everything category regardless of what it is
                        categorizedObjects[ObjectCategory.Everything].Add(obj);
                    }
                }
                else
                {
                    // Normal categorization for specific categories
                    foreach (var obj in registry)
                    {
                        if (!Utils.GameObjectUtils.IsActuallyInWorld(obj)) continue;

                        float distance = Vector3.Distance(playerPos, obj.transform.position);

                        // Apply category-specific distance limits
                        float maxDistance = ObjectCategorizer.GetMaxDistanceForCategory(targetCategory);
                        if (distance > maxDistance) continue;

                        // Rough room awareness: skip objects auto-walk provably cannot
                        // reach from here (other rooms behind walls, other floors).
                        // Doors stay listed - they count as reachable within interaction
                        // range. Unknown (null) is kept, never guessed away.
                        if (ReachabilityChecker.IsReachable(playerPos, obj.transform.position) == false) continue;

                        ObjectCategory objCategory = ObjectCategorizer.CategorizeObject(obj, playerPos);
                        categorizedObjects[objCategory].Add(obj);
                    }
                }
                
                // Sort each category based on current sorting mode
                foreach (var categoryList in categorizedObjects.Values)
                {
                    if (currentSortingMode == SortingMode.Directional)
                    {
                        // Sort by angular position (clockwise from North) with reachability weighting
                        // First group by reachability-weighted distance ranges, then sort by angle within each range
                        categoryList.Sort((a, b) =>
                        {
                            // Use reachability-weighted distance to maintain same-level priority
                            float weightedDistA = DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, a.transform.position);
                            float weightedDistB = DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, b.transform.position);

                            // Define distance ranges (0-10m, 10-20m, 20-30m, etc.) based on weighted distance
                            int rangeA = (int)(weightedDistA / 10);
                            int rangeB = (int)(weightedDistB / 10);

                            // Sort by weighted distance range first (maintains level priority)
                            if (rangeA != rangeB)
                                return rangeA.CompareTo(rangeB);

                            // Within same range, sort by angle (clockwise from North)
                            float angleA = DirectionCalculator.GetAngleToTarget(playerPos, a.transform.position);
                            float angleB = DirectionCalculator.GetAngleToTarget(playerPos, b.transform.position);
                            return angleA.CompareTo(angleB);
                        });
                    }
                    else
                    {
                        // Original distance-based sorting with reachability weighting
                        categoryList.Sort((a, b) =>
                            DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, a.transform.position)
                            .CompareTo(DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, b.transform.position)));
                    }
                }
                
                // Switch to selected category and reset selection
                currentCategory = targetCategory;
                selectedObjectIndex = HasObjectsInCategory(targetCategory) ? 0 : -1;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[NAVIGATION STATE] Error updating categorized objects: {ex}");
            }
        }

        public MouseOverHighlight GetCurrentSelectedObject()
        {
            if (!HasSelection) return null;
            
            var objects = categorizedObjects[currentCategory];
            if (selectedObjectIndex >= objects.Count) return null;
            
            return objects[selectedObjectIndex];
        }

        private float lastScanTime;

        /// <summary>
        /// Rebuilds the current category when the snapshot has gone stale, keeping the
        /// selection where it was.
        ///
        /// The list is a snapshot from the moment a category key was pressed, and the
        /// world moves on without it: the player opened a door, walked through, and the
        /// list kept offering him the old room - the balcony and the money on the other
        /// side simply did not exist for him until he re-selected the category by hand.
        /// Refreshing only after a pause (not between quick successive presses) keeps
        /// the cycling order stable under the player's fingers.
        /// </summary>
        public void RefreshIfStale(Vector3 playerPos)
        {
            const float STALE_AFTER_SECONDS = 4f;
            if (Time.unscaledTime - lastScanTime < STALE_AFTER_SECONDS) return;

            var previous = GetCurrentSelectedObject();
            int previousId = previous != null ? previous.GetInstanceID() : 0;

            UpdateCategorizedObjects(playerPos, currentCategory);

            if (previousId == 0 || !categorizedObjects.TryGetValue(currentCategory, out var objects)) return;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null && objects[i].GetInstanceID() == previousId)
                {
                    selectedObjectIndex = i;
                    return;
                }
            }
            // The selected object is gone from the category (taken, emptied, despawned) -
            // the index stays at the top and the next cycle starts from the closest object.
        }

        public void CycleToNextObject()
        {
            if (!HasObjectsInCategory(currentCategory)) return;

            var objects = categorizedObjects[currentCategory];
            selectedObjectIndex = (selectedObjectIndex + 1) % objects.Count;
        }

        public void CycleToPreviousObject()
        {
            if (!HasObjectsInCategory(currentCategory)) return;

            var objects = categorizedObjects[currentCategory];
            selectedObjectIndex = (selectedObjectIndex - 1 + objects.Count) % objects.Count;
        }

        public int GetObjectCountForCategory(ObjectCategory category)
        {
            return categorizedObjects.ContainsKey(category) ? categorizedObjects[category].Count : 0;
        }

        public bool HasObjectsInCategory(ObjectCategory category)
        {
            return GetObjectCountForCategory(category) > 0;
        }

        public List<MouseOverHighlight> GetObjectsInCategory(ObjectCategory category)
        {
            return categorizedObjects.ContainsKey(category) ? categorizedObjects[category] : new List<MouseOverHighlight>();
        }

        private void ClearAllCategories()
        {
            foreach (var categoryList in categorizedObjects.Values)
            {
                categoryList.Clear();
            }
            selectedObjectIndex = -1;
        }

        public void ResetSelection()
        {
            selectedObjectIndex = -1;
        }

        public void ToggleSortingMode()
        {
            currentSortingMode = currentSortingMode == SortingMode.Distance
                ? SortingMode.Directional
                : SortingMode.Distance;
        }

        public void SetSortingMode(SortingMode mode)
        {
            currentSortingMode = mode;
        }

        /// <summary>
        /// ", open"/", closed"/", locked" for doors, empty otherwise. The game's Door
        /// component (FortressOccident) carries the live state - name heuristics alone
        /// could never tell a locked door from an open one.
        /// </summary>
        private static string DescribeDoorState(MouseOverHighlight obj)
        {
            try
            {
                var door = obj.GetComponentInParent<Door>();
                if (door == null) return "";
                if (door._isLocked) return ", locked";
                return door._isOpen ? ", open" : ", closed";
            }
            catch
            {
                return "";
            }
        }

        public NavigationInfo GetCurrentNavigationInfo(Vector3 playerPos)
        {
            var selectedObj = GetCurrentSelectedObject();
            if (selectedObj == null)
            {
                return new NavigationInfo
                {
                    HasSelection = false,
                    ObjectName = "",
                    Distance = 0f,
                    Direction = "",
                    CurrentIndex = 0,
                    TotalCount = GetObjectCountForCategory(currentCategory),
                    CategoryName = ObjectCategorizer.GetCategoryDisplayName(currentCategory)
                };
            }

            float distance = Vector3.Distance(playerPos, selectedObj.transform.position);
            string name = ObjectNameCleaner.GetBetterObjectName(selectedObj);
            name += DescribeDoorState(selectedObj);
            string direction = DirectionCalculator.GetCardinalDirection(playerPos, selectedObj.transform.position);

            return new NavigationInfo
            {
                HasSelection = true,
                ItemDescription = AccessibilityPreferences.GetItemDescriptions()
                    ? ObjectNameCleaner.GetPickupItemDescription(selectedObj)
                    : null,
                ObjectName = name,
                Distance = distance,
                Direction = direction,
                CurrentIndex = selectedObjectIndex + 1,
                TotalCount = GetObjectCountForCategory(currentCategory),
                CategoryName = ObjectCategorizer.GetCategoryDisplayName(currentCategory),
                SortingMode = currentSortingMode,
                IsReachable = ReachabilityChecker.IsReachable(playerPos, selectedObj.transform.position)
            };
        }
    }

    public class NavigationInfo
    {
        public bool HasSelection { get; set; }
        public string ObjectName { get; set; } = "";
        public float Distance { get; set; }
        public string Direction { get; set; } = "";
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CategoryName { get; set; } = "";
        public SortingMode SortingMode { get; set; } = SortingMode.Directional;
        // null = unknown (endpoint off the NavMesh / no NavMesh) - only a definite
        // "false" gets announced, an unknown stays silent.
        public bool? IsReachable { get; set; }

        /// <summary>The game's description of the item, when the player asked for it always.</summary>
        public string ItemDescription { get; set; }

        public string FormatAnnouncement()
        {
            if (!HasSelection)
            {
                return $"No {CategoryName.ToLower()}s nearby";
            }

            string sortModeHint = SortingMode == SortingMode.Directional ? " (clockwise)" : " (by distance)";
            string reachabilityHint = IsReachable == false ? " Not reachable on foot from here." : "";
            string description = string.IsNullOrEmpty(ItemDescription) ? "" : $" {ItemDescription}";
            return $"{ObjectName} {Distance:F0} meters {Direction}, {CurrentIndex} of {TotalCount}.{reachabilityHint}{description} " +
                   $"Press {KeyBindings.SpeakableName(GameKey.CycleForward)} to cycle, {KeyBindings.SpeakableName(GameKey.NavigateToSelected)} to navigate.";
        }
    }
}