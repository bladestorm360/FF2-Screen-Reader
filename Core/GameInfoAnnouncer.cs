using System;
using MelonLoader;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Announces game information: Gil amount, current map, character status.
    /// Extracted from FFII_ScreenReaderMod to reduce file size.
    /// </summary>
    internal static class GameInfoAnnouncer
    {
        public static void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int gil = userDataManager.OwendGil;
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("{0} Gil"), gil));
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting gil: {ex.Message}");
            }
            FFII_ScreenReaderMod.SpeakText(T("Gil not available"));
        }

        public static void AnnounceCurrentMap()
        {
            try
            {
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                FFII_ScreenReaderMod.SpeakText(mapName);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting map name: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText(T("Map name not available"));
            }
        }

        /// <summary>
        /// V key / field mod-mode A: current movement state (on foot, ship, airship...). Works
        /// anywhere, like FF1 — the state is cached from the boarding hooks.
        /// </summary>
        public static void AnnounceVehicleState()
        {
            try
            {
                int moveState = Utils.MoveStateHelper.GetCurrentMoveState();
                FFII_ScreenReaderMod.SpeakText(Utils.MoveStateHelper.GetMoveStateName(moveState));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing vehicle state: {ex.Message}");
            }
        }

        /// <summary>
        /// H key / battle mod-mode X: the active party member's HP, MP and statuses (FF1 parity —
        /// the actor whose command is being chosen, tracked by BattleCommandPatches). Battle only.
        /// </summary>
        public static void AnnounceCharacterStatus()
        {
            try
            {
                if (!FFII_ScreenReaderMod.IsInBattle)
                {
                    FFII_ScreenReaderMod.SpeakText(T("Party status only available in battle"), interrupt: true);
                    return;
                }

                var charData = Patches.BattleCommandPatches.CurrentActor;
                if (charData == null)
                {
                    FFII_ScreenReaderMod.SpeakText(T("No active character"), interrupt: true);
                    return;
                }

                var param = charData.Parameter;
                if (param == null)
                {
                    FFII_ScreenReaderMod.SpeakText(T("Character status not available"), interrupt: true);
                    return;
                }

                string line = string.Format(T("{0}: HP {1}/{2}, MP {3}/{4}"),
                    charData.Name, param.CurrentHP, param.ConfirmedMaxHp(), param.CurrentMP, param.ConfirmedMaxMp());
                line += Patches.BattleCommandPatches.BuildStatusSuffix(param);
                FFII_ScreenReaderMod.SpeakText(line, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting character status: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText(T("Character status not available"), interrupt: true);
            }
        }
    }
}
