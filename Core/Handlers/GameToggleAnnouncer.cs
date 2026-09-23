using System;
using HarmonyLib;
using MelonLoader;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using CheatSettingsClient = Il2CppLast.Management.CheatSettingsClient;

namespace FFII_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Announces the two game-side toggles the player can flip from several sources.
    /// Walk/run (auto-dash): a per-frame read-only poll of UserDataManager.Config.IsAutoDash (F1 / L3,
    /// config menu, cheat menu), announced only when the value changes inside active field gameplay;
    /// map transitions and menus silently re-seed so a context shift isn't mistaken for a toggle.
    /// Random encounters: event-driven — a hook on CheatSettingsClient.SetIsEnableEncount, the setter
    /// every source funnels through (field toggle, config menu, save load).
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static int? lastAutoDash;

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

        internal static void Poll()
        {
            try
            {
                // Out-of-context (loading, menus, battle, transition): silently re-seed so we
                // never report a state shift caused by entering/leaving a context.
                if (!ControllerRouter.IsFieldActive)
                {
                    lastAutoDash = null;
                    return;
                }

                var ud = Il2CppLast.Management.UserDataManager.Instance();
                if (ud == null) return;

                var cfg = ud.Config;
                if (cfg != null)
                {
                    int a = cfg.IsAutoDash;
                    if (lastAutoDash.HasValue && lastAutoDash.Value != a)
                        FFII_ScreenReaderMod.SpeakText(a != 0 ? T("Run") : T("Walk"), interrupt: true);
                    lastAutoDash = a;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] {ex.Message}");
            }
        }

        internal static void Reset()
        {
            lastAutoDash = null;
        }
    }
}
