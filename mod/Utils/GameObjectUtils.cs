using System;
using UnityEngine;
using Il2CppFortressOccident;
using MelonLoader;

namespace AccessibilityMod.Utils
{
    public static class GameObjectUtils
    {
        public static Character GetPlayerCharacter()
        {
            try
            {
                MelonLogger.Msg("[GAMEOBJECT] Searching for player character...");
                
                // Method 1: Try Character.Main static property
                try
                {
                    var mainChar = Character.Main;
                    if (mainChar != null)
                    {
                        MelonLogger.Msg($"[GAMEOBJECT] Found Character.Main: {mainChar.name}");
                        // Test that the object is valid for Il2Cpp calls
                        var testStatus = mainChar.movementStatus;
                        MelonLogger.Msg($"[GAMEOBJECT] Character.Main status test: {testStatus}");
                        return mainChar;
                    }
                    else
                    {
                        MelonLogger.Msg("[GAMEOBJECT] Character.Main is null");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[GAMEOBJECT] Error accessing Character.Main: {ex}");
                }
                
                // Method 2: Find by UnityEngine.Object
                try
                {
                    var foundChar = UnityEngine.Object.FindObjectOfType<Character>();
                    if (foundChar != null)
                    {
                        MelonLogger.Msg($"[GAMEOBJECT] Found Character by type: {foundChar.name}");
                        // Test that the object is valid for Il2Cpp calls
                        var testStatus = foundChar.movementStatus;
                        MelonLogger.Msg($"[GAMEOBJECT] Found Character status test: {testStatus}");
                        return foundChar;
                    }
                    else
                    {
                        MelonLogger.Msg("[GAMEOBJECT] FindObjectOfType<Character>() returned null");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[GAMEOBJECT] Error finding Character by type: {ex}");
                }
                
                MelonLogger.Error("[GAMEOBJECT] Failed to find valid Character object");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GAMEOBJECT] Error in GetPlayerCharacter: {ex}");
                return null;
            }
        }

        public static Vector3 GetPlayerPosition()
        {
            var character = GetPlayerCharacter();
            if (character == null) return Vector3.zero;
            return character.transform.position;
        }

        /// <summary>
        /// Whether a registry entry is a real object standing in the world, as opposed to
        /// a placeholder parked at the world origin.
        ///
        /// The registry contains interactables for people who are not actually present:
        /// Klaasje's highlight sat at exactly (0,0,0) while her room was locked, so the
        /// list offered her "8 meters east", the reachability check came back unknown
        /// (no NavMesh at the origin), and auto-walk ran the player into a wall on the
        /// way to a point outside the level. A sighted player never sees these - there is
        /// nothing there to see. Exactly-zero is the same "does not exist" convention
        /// GetPlayerPosition already uses.
        /// </summary>
        public static bool IsActuallyInWorld(MouseOverHighlight obj)
        {
            if (obj == null || obj.transform == null) return false;
            return obj.transform.position != Vector3.zero;
        }

        public static bool IsPlayerMoving()
        {
            var character = GetPlayerCharacter();
            if (character == null) return false;
            
            try
            {
                return character.movementStatus == Character.MovementStatus.MOVING;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GAMEOBJECT] Error checking movement status: {ex}");
                return false;
            }
        }

        public static Character.MovementStatus GetPlayerMovementStatus()
        {
            var character = GetPlayerCharacter();
            if (character == null) return Character.MovementStatus.IDLE;
            
            try
            {
                return character.movementStatus;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GAMEOBJECT] Error getting movement status: {ex}");
                return Character.MovementStatus.IDLE;
            }
        }
    }
}