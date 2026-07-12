using System;
using UnityEngine;
using UnityEngine.AI;

namespace AccessibilityMod.Utils
{
    /// <summary>
    /// Answers "can the player actually walk there?" using the game's own NavMesh - the
    /// same data click-to-move pathfinding uses. Lets navigation announcements say
    /// "not reachable" up front instead of the player only finding out after an
    /// auto-walk fails. Returns null (unknown) whenever either endpoint can't be
    /// snapped onto the NavMesh, so callers can stay silent rather than guess wrong.
    /// </summary>
    public static class ReachabilityChecker
    {
        // How far an endpoint may be off the NavMesh and still count as "on it".
        // Interactables often sit on furniture/walls slightly above or beside the
        // walkable surface, so the target gets a little more slack than the player.
        private const float PLAYER_SNAP_RADIUS = 2.0f;
        private const float TARGET_SNAP_RADIUS = 3.0f;

        public static bool? IsReachable(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                if (!NavMesh.SamplePosition(playerPos, out var playerHit, PLAYER_SNAP_RADIUS, NavMesh.AllAreas))
                {
                    return null;
                }

                if (!NavMesh.SamplePosition(targetPos, out var targetHit, TARGET_SNAP_RADIUS, NavMesh.AllAreas))
                {
                    return null;
                }

                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(playerHit.position, targetHit.position, NavMesh.AllAreas, path))
                {
                    return false;
                }

                return path.status == NavMeshPathStatus.PathComplete;
            }
            catch (Exception)
            {
                // Il2Cpp interop hiccup or no NavMesh in this scene - report unknown,
                // never a false "not reachable".
                return null;
            }
        }
    }
}
