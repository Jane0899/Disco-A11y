using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;

namespace AccessibilityMod.Navigation
{
    public enum WaypointCategory
    {
        General = 0,   // Default; 0 = backward-compatible with missing JSON field
        NPCs = 1,
        Locations = 2
    }

    public class Waypoint
    {
        public Waypoint(Vector3 position, string name, string sceneName)
            : this(position, name, sceneName, DateTime.UtcNow)
        {
        }

        public Waypoint(Vector3 position, string name, string sceneName, DateTime createdAtUtc)
            : this(position, name, sceneName, createdAtUtc, WaypointCategory.General)
        {
        }

        public Waypoint(Vector3 position, string name, string sceneName, DateTime createdAtUtc, WaypointCategory category)
        {
            Position = position;
            Name = name;
            SceneName = sceneName;
            CreatedAtUtc = createdAtUtc;
            Category = category;
        }

        public Vector3 Position { get; }
        public string Name { get; private set; }
        public string SceneName { get; }
        public DateTime CreatedAtUtc { get; }
        public WaypointCategory Category { get; }

        public void Rename(string newName)
        {
            if (!string.IsNullOrWhiteSpace(newName))
                Name = newName.Trim();
        }
    }

    public class WaypointManager
    {
        private readonly WaypointPersistence persistence;
        private readonly List<Waypoint> waypoints;
        private readonly Dictionary<string, int> selectedIndicesByScene = new Dictionary<string, int>();
        private readonly Dictionary<string, WaypointCategory?> currentCategoryByScene = new Dictionary<string, WaypointCategory?>();

        public IReadOnlyList<Waypoint> Waypoints => waypoints;
        public int Count => waypoints.Count;

        public bool HasAnyWaypoints => waypoints.Count > 0;

        public WaypointManager()
        {
            persistence = new WaypointPersistence();
            waypoints = persistence.LoadWaypoints();

            if (waypoints.Count > 0)
            {
                MelonLogger.Msg($"[WAYPOINTS] Loaded {waypoints.Count} saved waypoint" + (waypoints.Count == 1 ? string.Empty : "s") + ".");
            }
        }

        public void SaveAllWaypoints()
        {
            persistence.SaveWaypoints(waypoints);
        }

        public string GetDefaultName(string sceneName)
        {
            int totalCount = waypoints.Count(w => w.SceneName == sceneName);
            return $"Waypoint {totalCount + 1}";
        }

        public void SetCategoryFilter(string sceneName, WaypointCategory? category)
        {
            currentCategoryByScene[sceneName] = category;
        }

        public WaypointCategory? GetCategoryFilter(string sceneName)
        {
            return currentCategoryByScene.TryGetValue(sceneName, out WaypointCategory? cat) ? cat : null;
        }

        public void ClearCategoryFilter(string sceneName)
        {
            currentCategoryByScene.Remove(sceneName);
        }

        public Waypoint AddWaypoint(Vector3 position, string name, string sceneName, WaypointCategory category = WaypointCategory.General)
        {
            var waypoint = new Waypoint(position, name, sceneName, DateTime.UtcNow, category);
            waypoints.Add(waypoint);

            var sceneWaypoints = GetWaypointsForScene(sceneName);
            selectedIndicesByScene[sceneName] = sceneWaypoints.Count - 1;

            persistence.SaveWaypoints(waypoints);
            return waypoint;
        }

        public bool RemoveWaypoint(string sceneName, Waypoint waypoint)
        {
            if (waypoint == null)
                return false;

            string targetScene = string.IsNullOrEmpty(sceneName) ? waypoint.SceneName : sceneName;
            if (string.IsNullOrEmpty(targetScene))
                return waypoints.Remove(waypoint);

            int previousIndex = selectedIndicesByScene.TryGetValue(targetScene, out int storedIndex) ? storedIndex : -1;
            var sceneWaypointsBefore = GetWaypointsForScene(targetScene);
            int removedSceneIndex = sceneWaypointsBefore.IndexOf(waypoint);

            bool removed = waypoints.Remove(waypoint);
            if (!removed)
                return false;

            var updatedSceneWaypoints = GetWaypointsForScene(targetScene);
            if (updatedSceneWaypoints.Count == 0)
            {
                selectedIndicesByScene.Remove(targetScene);
            }
            else
            {
                int newIndex = removedSceneIndex >= 0 ? removedSceneIndex : previousIndex;
                if (newIndex < 0)
                    newIndex = 0;

                newIndex = Mathf.Clamp(newIndex, 0, updatedSceneWaypoints.Count - 1);
                selectedIndicesByScene[targetScene] = newIndex;
            }

            persistence.SaveWaypoints(waypoints);
            return true;
        }

        public bool HasWaypointsInScene(string sceneName)
        {
            return GetWaypointsForScene(sceneName).Count > 0;
        }

        public void EnsureSelection(string sceneName)
        {
            var sceneWaypoints = GetWaypointsForScene(sceneName);
            if (sceneWaypoints.Count == 0)
            {
                selectedIndicesByScene.Remove(sceneName);
                return;
            }

            int index = GetSelectedIndexForScene(sceneName, sceneWaypoints.Count);
            selectedIndicesByScene[sceneName] = index;
        }

        public void SelectNext(string sceneName)
        {
            var sceneWaypoints = GetWaypointsForScene(sceneName);
            if (sceneWaypoints.Count == 0) return;

            int index = GetSelectedIndexForScene(sceneName, sceneWaypoints.Count);
            index = (index + 1) % sceneWaypoints.Count;
            selectedIndicesByScene[sceneName] = index;
        }

        public void SelectPrevious(string sceneName)
        {
            var sceneWaypoints = GetWaypointsForScene(sceneName);
            if (sceneWaypoints.Count == 0) return;

            int index = GetSelectedIndexForScene(sceneName, sceneWaypoints.Count);
            index = (index - 1 + sceneWaypoints.Count) % sceneWaypoints.Count;
            selectedIndicesByScene[sceneName] = index;
        }

        public bool TryGetSelection(string sceneName, out Waypoint waypoint, out int index, out int total)
        {
            var sceneWaypoints = GetWaypointsForScene(sceneName);
            total = sceneWaypoints.Count;

            if (total == 0)
            {
                waypoint = null;
                index = -1;
                selectedIndicesByScene.Remove(sceneName);
                return false;
            }

            index = GetSelectedIndexForScene(sceneName, total);
            waypoint = sceneWaypoints[index];
            return true;
        }

        private List<Waypoint> GetWaypointsForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return new List<Waypoint>();

            var all = waypoints.Where(w => w.SceneName == sceneName).OrderBy(w => w.CreatedAtUtc);

            WaypointCategory? filter = GetCategoryFilter(sceneName);
            if (filter.HasValue)
                all = all.Where(w => w.Category == filter.Value).OrderBy(w => w.CreatedAtUtc);

            return all.ToList();
        }

        private int GetSelectedIndexForScene(string sceneName, int totalCount)
        {
            if (totalCount == 0)
                return -1;

            if (!selectedIndicesByScene.TryGetValue(sceneName, out int index))
            {
                index = 0;
            }

            if (index < 0 || index >= totalCount)
            {
                index = Mathf.Clamp(index, 0, totalCount - 1);
            }

            selectedIndicesByScene[sceneName] = index;
            return index;
        }
    }
}
