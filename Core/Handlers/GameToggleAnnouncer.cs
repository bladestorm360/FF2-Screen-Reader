using System;
using HarmonyLib;
using MelonLoader;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using CheatSettingsClient = Il2CppLast.Management.CheatSettingsClient;
using ConfigClient = Il2CppLast.Management.ConfigClient;

namespace FFII_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Announces the two game-side toggles the player can flip from several sources, both event-driven
    /// (CLAUDE.md rule 3) and both spoken only for a real change made in active field gameplay (the
    /// config menu speaks its own row value; a save load restores the setting off-field):
    /// Walk/run (auto-dash): a hook on ConfigClient.SetIsAutoDash, the setter every source funnels
    /// through (FieldMap.UpdatePlayerStatePlay for F1 / L3, the config menu's details controller).
    /// Replaces the per-frame poll of Config.IsAutoDash.
    /// Random encounters: a hook on CheatSettingsClient.SetIsEnableEncount (field toggle, config menu,
    /// save load).
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        // Walk/run state captured by the SetIsAutoDash prefix, compared in the postfix.
        private static int autoDashBefore;

        // Encounter state captured by the SetIsEnableEncount prefix, compared in the postfix.
        private static bool encounterBefore;

        /// <summary>
        /// Hooks CheatSettingsClient.SetIsEnableEncount(bool) (real-bodied, unique RVA; callers are the
        /// field toggle in FieldMap.UpdatePlayerStatePlay, both config menus and the save loader).
        /// Never CheatSettingsData.set_IsEnableEncount — that setter's body is shared by 24 methods.
        /// </summary>
        internal static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(CheatSettingsClient), "SetIsEnableEncount", new Type[] { typeof(bool) });
                if (method == null)
                {
                    MelonLogger.Error("[GameToggleAnnouncer] CheatSettingsClient.SetIsEnableEncount not found");
                    return;
                }
                harmony.Patch(method,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(GameToggleAnnouncer), nameof(SetIsEnableEncount_Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(GameToggleAnnouncer), nameof(SetIsEnableEncount_Postfix))));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameToggleAnnouncer] Error patching SetIsEnableEncount: {ex.Message}");
            }

            // ConfigClient.SetIsAutoDash(int) (real-bodied, unique RVA 0x8AF7E0; callers FieldMap.
            // UpdatePlayerStatePlay, ConfigActualDetailsControllerBase.SetIsAutoDash and
            // SwitchArrowSelectTypeProcess). Config.set_IsAutoDash is only called from it.
            try
            {
                var method = AccessTools.Method(typeof(ConfigClient), "SetIsAutoDash", new Type[] { typeof(int) });
                if (method == null)
                {
                    MelonLogger.Error("[GameToggleAnnouncer] ConfigClient.SetIsAutoDash not found");
                    return;
                }
                harmony.Patch(method,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(GameToggleAnnouncer), nameof(SetIsAutoDash_Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(GameToggleAnnouncer), nameof(SetIsAutoDash_Postfix))));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameToggleAnnouncer] Error patching SetIsAutoDash: {ex.Message}");
            }
        }

        public static void SetIsEnableEncount_Prefix()
        {
            try { encounterBefore = Il2CppLast.Management.UserDataManager.Instance()?.CheatSettingsData?.IsEnableEncount ?? false; }
            catch { }
        }

        /// <summary>
        /// Announces a real change made on the field (the config menu speaks its own row value; a save
        /// load restores the setting off-field), so only field-active changes speak.
        /// </summary>
        public static void SetIsEnableEncount_Postfix(bool __0)
        {
            try
            {
                if (!ControllerRouter.IsFieldActive || __0 == encounterBefore) return;
                FFII_ScreenReaderMod.SpeakText(__0 ? T("Encounters on") : T("Encounters off"), interrupt: true);
            }
            catch { }
        }

        public static void SetIsAutoDash_Prefix()
        {
            try { autoDashBefore = Il2CppLast.Management.UserDataManager.Instance()?.Config?.IsAutoDash ?? 0; }
            catch { }
        }

        /// <summary>"Run" / "Walk" for a real change made on the field (F1 / L3).</summary>
        public static void SetIsAutoDash_Postfix(int __0)
        {
            try
            {
                if (!ControllerRouter.IsFieldActive || __0 == autoDashBefore) return;
                FFII_ScreenReaderMod.SpeakText(__0 != 0 ? T("Run") : T("Walk"), interrupt: true);
            }
            catch { }
        }
    }
}
