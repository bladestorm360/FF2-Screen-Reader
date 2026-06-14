using System;
using MelonLoader;
using static FFII_ScreenReader.Utils.ModTextTranslator;

namespace FFII_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Per-frame poller for the game's walk/run (auto-dash) state. The player can flip it from
    /// several sources — F1 / L3 in-game, the in-game config menu, the cheat menu — so the mod
    /// reads UserDataManager.Config.IsAutoDash directly (read-only) and announces only when the
    /// value changes inside active field gameplay. It never writes the value; it is a pure state
    /// tracker. Map transitions and menus silently re-seed so a context shift isn't mistaken for
    /// a toggle.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static int? lastAutoDash;

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
