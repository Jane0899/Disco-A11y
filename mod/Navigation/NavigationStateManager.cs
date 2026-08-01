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

        // Invisible walk-in trigger zones (Il2Cpp.InteractionAreaTrigger with no backing
        // world object - see NavigableAreaTrigger.cs) that MouseOverHighlight.registry
        // simply does not contain. Only ever populated for ObjectCategory.Everything -
        // appended AFTER the normal list in the selection index space, so
        // selectedObjectIndex in [0, categorizedObjects[Everything].Count) means a normal
        // object as always, and [that .. +areaTriggers.Count) means one of these instead.
        private List<NavigableAreaTrigger> areaTriggers = new List<NavigableAreaTrigger>();
        private ObjectCategory currentCategory = ObjectCategory.NPCs;
        private int selectedObjectIndex = -1;

        // Which side of the selected object "navigate to" walks you to (Jana's idea,
        // 01.08.2026, see todos.md J7): the default single interaction point some objects
        // expose is not always the side that matters - a car's headlights only illuminate
        // what is in front of it, not what is beside it. 0 = unchanged default behaviour
        // (the object's own closest reachable point); resets on every new selection so an
        // old side choice never silently carries over onto a different object.
        private int approachSideIndex = 0;
        private static readonly string[] ApproachSideNames = { "the default side", "the front", "the right side", "the back", "the left side" };
        public string CurrentApproachSideName => ApproachSideNames[approachSideIndex];
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

                    // Pure walk-in trigger zones (no basicEntity - see NavigableAreaTrigger.cs)
                    // are invisible to MouseOverHighlight.registry above, so they need their
                    // own scan. Ones WITH a basicEntity already have a normal representation
                    // in the loop above (that's what basicEntity IS - a reference to the real
                    // object), so only the entity-less ones are new information here.
                    areaTriggers.Clear();
                    // includeInactive: true - these are one-off "walk in and see this" zones,
                    // commonly left disabled until a story/inventory condition turns them on
                    // (confirmed live: the default FindObjectsOfType<T>() overload, which
                    // skips inactive GameObjects, found zero of them in a scene that clearly
                    // has some). A disabled trigger still correctly means "not available yet"
                    // for us too - the two checks below already filter to what is currently
                    // relevant (distance, reachability); an inactive one that is also out of
                    // range is simply never added, no different from never being found here.
                    var allTriggers = UnityEngine.Object.FindObjectsOfType<Il2Cpp.InteractionAreaTrigger>(true);
                    int skippedHasEntity = 0, skippedTooFar = 0, skippedUnreachable = 0;
                    foreach (var trigger in allTriggers)
                    {
                        if (trigger == null || trigger.gameObject == null) continue;
                        if (trigger.basicEntity != null) { skippedHasEntity++; continue; }

                        float distance = Vector3.Distance(playerPos, trigger.transform.position);
                        if (distance > maxDistance) { skippedTooFar++; continue; }
                        if (ReachabilityChecker.IsReachable(playerPos, trigger.transform.position) == false) { skippedUnreachable++; continue; }

                        areaTriggers.Add(new NavigableAreaTrigger(trigger));
                    }
                    MelonLoader.MelonLogger.Msg($"[NAVIGATION STATE] Area triggers: {allTriggers.Length} total in scene, {skippedHasEntity} have basicEntity, {skippedTooFar} too far, {skippedUnreachable} unreachable, {areaTriggers.Count} added");
                }
                else
                {
                    areaTriggers.Clear();
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
                
                // Same sort as the normal list above, kept as a second block since
                // NavigableAreaTrigger isn't a MouseOverHighlight and can't share that loop.
                if (currentSortingMode == SortingMode.Directional)
                {
                    areaTriggers.Sort((a, b) =>
                    {
                        float weightedDistA = DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, a.Position);
                        float weightedDistB = DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, b.Position);
                        int rangeA = (int)(weightedDistA / 10);
                        int rangeB = (int)(weightedDistB / 10);
                        if (rangeA != rangeB) return rangeA.CompareTo(rangeB);
                        float angleA = DirectionCalculator.GetAngleToTarget(playerPos, a.Position);
                        float angleB = DirectionCalculator.GetAngleToTarget(playerPos, b.Position);
                        return angleA.CompareTo(angleB);
                    });
                }
                else
                {
                    areaTriggers.Sort((a, b) =>
                        DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, a.Position)
                        .CompareTo(DirectionCalculator.CalculateReachabilityWeightedDistance(playerPos, b.Position)));
                }

                // Switch to selected category and reset selection
                currentCategory = targetCategory;
                selectedObjectIndex = HasObjectsInCategory(targetCategory) ? 0 : -1;
                approachSideIndex = 0;
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

        /// <summary>
        /// The area-trigger counterpart to GetCurrentSelectedObject() - non-null exactly
        /// when selectedObjectIndex has walked past the normal object list and into the
        /// appended trigger-zone range (Everything category only, see the areaTriggers field).
        /// </summary>
        public NavigableAreaTrigger GetCurrentSelectedAreaTrigger()
        {
            if (currentCategory != ObjectCategory.Everything) return null;

            int normalCount = categorizedObjects.TryGetValue(currentCategory, out var normalList) ? normalList.Count : 0;
            int triggerIndex = selectedObjectIndex - normalCount;
            if (triggerIndex < 0 || triggerIndex >= areaTriggers.Count) return null;

            return areaTriggers[triggerIndex];
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
            // Combined count (normal objects + trigger zones for Everything) - cycling
            // past the last normal object walks straight into the trigger zones instead
            // of wrapping early, and back again past the last trigger zone.
            int total = GetObjectCountForCategory(currentCategory);
            if (total == 0) return;
            selectedObjectIndex = (selectedObjectIndex + 1) % total;
            approachSideIndex = 0;
        }

        public void CycleToPreviousObject()
        {
            int total = GetObjectCountForCategory(currentCategory);
            if (total == 0) return;
            selectedObjectIndex = (selectedObjectIndex - 1 + total) % total;
            approachSideIndex = 0;
        }

        /// <summary>Cycles which side of the current selection "navigate to" targets - see
        /// the approachSideIndex field comment. Does nothing without a selection.</summary>
        public void CycleApproachSide(bool backward)
        {
            if (!HasSelection && GetCurrentSelectedAreaTrigger() == null) return;
            int count = ApproachSideNames.Length;
            approachSideIndex = (approachSideIndex + (backward ? -1 : 1) + count) % count;
        }

        /// <summary>
        /// Null means "unchanged": approachSideIndex is still 0 (the player has never
        /// cycled sides for this selection, or explicitly cycled back to the default), so
        /// callers should keep using the object's own normal interaction point. Non-null is
        /// an approximate stand-here point (fixed radius, since we have no collider bounds
        /// to work with generically) offset from the object's own facing direction - "front"
        /// means whatever the object's local +Z axis points at, which is the closest
        /// generic proxy for "front" any GameObject has.
        /// </summary>
        public Vector3? GetApproachOverridePosition(Vector3 objectPosition, Quaternion objectRotation)
        {
            if (approachSideIndex == 0) return null;

            // 5.5m: originally 1.5m (a "just beside the object" guess), widened after live
            // testing against the Kineema's actual "halogen watermarks" reveal trigger sat
            // 5.6m from the car's own transform - object-relative approach points are not
            // necessarily close to the object itself. Still a rough generic default, not
            // tuned per object (see NavigableAreaTrigger.cs for why: no per-object hack).
            const float APPROACH_RADIUS_METERS = 5.5f;
            Vector3 localDirection = approachSideIndex switch
            {
                1 => Vector3.forward,
                2 => Vector3.right,
                3 => Vector3.back,
                4 => Vector3.left,
                _ => Vector3.zero
            };
            return objectPosition + (objectRotation * localDirection) * APPROACH_RADIUS_METERS;
        }

        public int GetObjectCountForCategory(ObjectCategory category)
        {
            int normal = categorizedObjects.ContainsKey(category) ? categorizedObjects[category].Count : 0;
            int triggers = category == ObjectCategory.Everything ? areaTriggers.Count : 0;
            return normal + triggers;
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
            // Checked before the normal object, not after: GetCurrentSelectedObject()
            // legitimately returns null once selectedObjectIndex has walked into the
            // trigger-zone range, which must not be reported as "nothing selected".
            var areaTrigger = GetCurrentSelectedAreaTrigger();
            if (areaTrigger != null)
            {
                float triggerDistance = Vector3.Distance(playerPos, areaTrigger.Position);
                return new NavigationInfo
                {
                    HasSelection = true,
                    ObjectName = areaTrigger.Name,
                    Distance = triggerDistance,
                    Direction = DirectionCalculator.GetCardinalDirection(playerPos, areaTrigger.Position),
                    CurrentIndex = selectedObjectIndex + 1,
                    TotalCount = GetObjectCountForCategory(currentCategory),
                    CategoryName = ObjectCategorizer.GetCategoryDisplayName(currentCategory),
                    SortingMode = currentSortingMode,
                    IsReachable = ReachabilityChecker.IsReachable(playerPos, areaTrigger.Position)
                };
            }

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