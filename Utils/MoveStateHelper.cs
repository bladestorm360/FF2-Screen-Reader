using System;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using MelonLoader;
using FFII_ScreenReader.Core;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for tracking and announcing player movement state (walking, ship, airship, chocobo, etc.)
    /// Uses event-driven state updates from MovementSpeechPatches (GetOn/GetOff/ChangeTransportation hooks).
    ///
    /// FF2 vehicles: Canoe (rivers), Ship (ocean), Airship (air travel)
    /// </summary>
    public static class MoveStateHelper
    {
        // MoveState enum values (from FieldPlayerConstants.MoveState)
        public const int MOVE_STATE_WALK = 0;
        public const int MOVE_STATE_DUSH = 1;    // Dash
        public const int MOVE_STATE_AIRSHIP = 2;
        public const int MOVE_STATE_SHIP = 3;
        public const int MOVE_STATE_LOWFLYING = 4;
        public const int MOVE_STATE_CHOCOBO = 5;
        public const int MOVE_STATE_GIMMICK = 6;
        public const int MOVE_STATE_UNIQUE = 7;

        // TransportationType enum values (from MapConstants.TransportationType in dump.cs)
        private const int TRANSPORT_NONE = 0;
        private const int TRANSPORT_PLAYER = 1;
        private const int TRANSPORT_SHIP = 2;
        private const int TRANSPORT_PLANE = 3;       // Airship
        private const int TRANSPORT_SYMBOL = 4;
        private const int TRANSPORT_CONTENT = 5;
        private const int TRANSPORT_SUBMARINE = 6;
        private const int TRANSPORT_LOWFLYING = 7;
        private const int TRANSPORT_SPECIALPLANE = 8;
        private const int TRANSPORT_YELLOWCHOCOBO = 9;
        private const int TRANSPORT_BLACKCHOCOBO = 10;
        private const int TRANSPORT_BOKO = 11;

        // Cached state tracking (event-driven, no timeouts)
        private static int cachedMoveState = MOVE_STATE_WALK;
        private static int cachedTransportationType = 0;
        private const string CONTEXT_STATE = "Movement.State";

        // Cached dashFlag state (set by SetDashFlag patch)
        private static bool cachedDashFlag = false;

        /// <summary>
        /// Set vehicle state when boarding (called from GetOn/ChangeTransportation patches).
        /// Maps TransportationType to MoveState.
        /// </summary>
        public static void SetVehicleState(int transportationType)
        {
            cachedTransportationType = transportationType;
            cachedMoveState = TransportTypeToMoveState(transportationType);
        }

        /// <summary>
        /// Set on-foot state when disembarking (called from GetOff/ChangeTransportation patches).
        /// </summary>
        public static void SetOnFoot()
        {
            cachedTransportationType = 0;
            cachedMoveState = MOVE_STATE_WALK;
        }

        /// <summary>
        /// Update cached move state directly (called from ChangeMoveState patch as backup).
        /// </summary>
        public static void UpdateCachedMoveState(int newState)
        {
            int previousState = cachedMoveState;
            cachedMoveState = newState;

            // Announce state changes
            if (newState != previousState)
            {
                AnnounceStateChange(previousState, newState);
            }
        }

        /// <summary>
        /// Convert TransportationType to MoveState for compatibility with existing code.
        /// </summary>
        private static int TransportTypeToMoveState(int transportationType)
        {
            switch (transportationType)
            {
                case TRANSPORT_SHIP: return MOVE_STATE_SHIP;
                case TRANSPORT_PLANE: return MOVE_STATE_AIRSHIP;
                case TRANSPORT_SUBMARINE: return MOVE_STATE_SHIP;  // Treat submarine like ship
                case TRANSPORT_LOWFLYING: return MOVE_STATE_LOWFLYING;
                case TRANSPORT_SPECIALPLANE: return MOVE_STATE_AIRSHIP;
                case TRANSPORT_YELLOWCHOCOBO:
                case TRANSPORT_BLACKCHOCOBO:
                case TRANSPORT_BOKO: return MOVE_STATE_CHOCOBO;
                default: return MOVE_STATE_WALK;
            }
        }

        /// <summary>
        /// Check if a state is a vehicle state (ship, chocobo, airship)
        /// </summary>
        private static bool IsVehicleState(int state)
        {
            return state == MOVE_STATE_SHIP || state == MOVE_STATE_CHOCOBO ||
                   state == MOVE_STATE_AIRSHIP || state == MOVE_STATE_LOWFLYING;
        }

        /// <summary>
        /// Announce movement state changes.
        /// FF2-specific: Ship could be the ocean ship or canoe depending on context,
        /// but the game uses the same MoveState.Ship for both.
        /// </summary>
        public static void AnnounceStateChange(int previousState, int newState)
        {
            string announcement = null;

            if (newState == MOVE_STATE_SHIP)
            {
                // FF2 uses Ship state for both ocean ship and canoe
                announcement = "On ship";
            }
            else if (newState == MOVE_STATE_CHOCOBO)
            {
                announcement = "On chocobo";
            }
            else if (newState == MOVE_STATE_AIRSHIP || newState == MOVE_STATE_LOWFLYING)
            {
                announcement = "On airship";
            }
            else if ((previousState == MOVE_STATE_SHIP || previousState == MOVE_STATE_CHOCOBO ||
                      previousState == MOVE_STATE_AIRSHIP || previousState == MOVE_STATE_LOWFLYING) &&
                     (newState == MOVE_STATE_WALK || newState == MOVE_STATE_DUSH))
            {
                announcement = "On foot";
            }

            if (announcement != null)
            {
                // Use deduplicator to prevent duplicate announcements
                if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_STATE, newState))
                {
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
        }

        /// <summary>
        /// Get current MoveState - uses cached state from event hooks.
        /// </summary>
        public static int GetCurrentMoveState()
        {
            // Return cached state (set by GetOn/GetOff/ChangeTransportation patches)
            return cachedMoveState;
        }

        /// <summary>
        /// Check if currently controlling ship (includes canoe in FF2)
        /// </summary>
        public static bool IsControllingShip()
        {
            return GetCurrentMoveState() == MOVE_STATE_SHIP;
        }

        /// <summary>
        /// Check if currently on foot (walking or dashing)
        /// </summary>
        public static bool IsOnFoot()
        {
            int state = GetCurrentMoveState();
            return state == MOVE_STATE_WALK || state == MOVE_STATE_DUSH;
        }

        /// <summary>
        /// Check if currently riding chocobo
        /// </summary>
        public static bool IsRidingChocobo()
        {
            return GetCurrentMoveState() == MOVE_STATE_CHOCOBO;
        }

        /// <summary>
        /// Check if currently controlling airship
        /// </summary>
        public static bool IsControllingAirship()
        {
            int state = GetCurrentMoveState();
            return state == MOVE_STATE_AIRSHIP || state == MOVE_STATE_LOWFLYING;
        }

        /// <summary>
        /// Check if currently in any vehicle (not on foot)
        /// </summary>
        public static bool IsInVehicle()
        {
            return !IsOnFoot();
        }

        /// <summary>
        /// Get pathfinding scope multiplier based on current MoveState.
        /// Used to expand entity scan range when on vehicles.
        /// </summary>
        public static float GetPathfindingMultiplier()
        {
            int moveState = GetCurrentMoveState();
            float multiplier;

            switch (moveState)
            {
                case MOVE_STATE_WALK:
                case MOVE_STATE_DUSH:
                    multiplier = 1.0f;  // Baseline (on foot)
                    break;

                case MOVE_STATE_SHIP:
                    multiplier = 2.5f;  // 2.5x scope for ship/canoe
                    break;

                case MOVE_STATE_CHOCOBO:
                    multiplier = 1.5f;  // Moderate increase for chocobo
                    break;

                case MOVE_STATE_AIRSHIP:
                case MOVE_STATE_LOWFLYING:
                    multiplier = 1.0f;  // Airship uses different navigation system
                    break;

                default:
                    multiplier = 1.0f;  // Default to baseline
                    break;
            }

            return multiplier;
        }

        /// <summary>
        /// Get human-readable name for MoveState
        /// </summary>
        public static string GetMoveStateName(int moveState)
        {
            switch (moveState)
            {
                case MOVE_STATE_WALK: return "Walking";
                case MOVE_STATE_DUSH: return "Dashing";
                case MOVE_STATE_SHIP: return "Ship";
                case MOVE_STATE_AIRSHIP: return "Airship";
                case MOVE_STATE_LOWFLYING: return "Low Flying";
                case MOVE_STATE_CHOCOBO: return "Chocobo";
                case MOVE_STATE_GIMMICK: return "Gimmick";
                case MOVE_STATE_UNIQUE: return "Unique";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Set cached dashFlag state (called from SetDashFlag patch).
        /// </summary>
        public static void SetCachedDashFlag(bool value)
        {
            cachedDashFlag = value;
            MelonLogger.Msg($"[MoveState] DashFlag set to: {value}");
        }

        /// <summary>
        /// Returns the effective running state by combining AutoDash config with F1 toggle.
        /// AutoDash XOR dashFlag gives the actual running state:
        /// - AutoDash ON + dashFlag false = Running
        /// - AutoDash ON + dashFlag true = Walking (toggled)
        /// - AutoDash OFF + dashFlag false = Walking
        /// - AutoDash OFF + dashFlag true = Running (toggled)
        /// Returns true if running, false if walking.
        /// </summary>
        public static bool GetDashFlag()
        {
            try
            {
                // Read AutoDash from ConfigSaveData via UserDataManager
                // UserDataManager.configSaveData at offset 0xB8
                // ConfigSaveData.isAutoDash at offset 0x40 (int: 0=off, 1=on)
                bool autoDash = false;
                var userData = Il2CppLast.Management.UserDataManager.Instance();

                if (userData != null)
                {
                    unsafe
                    {
                        IntPtr userDataPtr = userData.Pointer;
                        if (userDataPtr != IntPtr.Zero)
                        {
                            // Get configSaveData pointer at offset 0xB8
                            IntPtr configPtr = *(IntPtr*)((byte*)userDataPtr.ToPointer() + 0xB8);
                            if (configPtr != IntPtr.Zero)
                            {
                                // Read isAutoDash (int) at offset 0x40
                                int autoDashValue = *(int*)((byte*)configPtr.ToPointer() + 0x40);
                                autoDash = autoDashValue != 0;
                            }
                        }
                    }
                }

                // Use cached dashFlag from SetDashFlag patch
                bool dashFlag = cachedDashFlag;

                // Effective running state: XOR of autoDash and dashFlag
                bool result = autoDash != dashFlag;
                MelonLogger.Msg($"[DashDebug] autoDash={autoDash}, dashFlag={dashFlag}, result={result}");
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error reading dash state: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Reset state (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            cachedMoveState = MOVE_STATE_WALK;
            cachedTransportationType = 0;
            cachedDashFlag = false;
            AnnouncementDeduplicator.Reset(CONTEXT_STATE);
        }

        /// <summary>
        /// Called on map transitions to handle vehicle state.
        /// If entering an interior map while in a vehicle, switch to on-foot state.
        /// </summary>
        public static void OnMapTransition(bool isWorldMap)
        {
            // If entering an interior map (not world map) and currently in a vehicle,
            // switch to on-foot state (e.g., entering interior while in airship)
            if (!isWorldMap && IsVehicleState(cachedMoveState))
            {
                SetOnFoot();
            }
        }
    }
}
