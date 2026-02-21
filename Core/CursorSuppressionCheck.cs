using FFII_ScreenReader.Patches;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Centralized cursor suppression check.
    /// Replaces scattered ShouldSuppress() calls in CursorNavigation_Postfix.
    /// </summary>
    internal static class CursorSuppressionCheck
    {
        internal class SuppressionResult
        {
            public bool ShouldSuppress { get; set; }
            public string StateName { get; set; }
            public bool IsPopup { get; set; }

            public static SuppressionResult None => new SuppressionResult
            {
                ShouldSuppress = false,
                StateName = null,
                IsPopup = false
            };

            public static SuppressionResult Suppressed(string stateName, bool isPopup = false) => new SuppressionResult
            {
                ShouldSuppress = true,
                StateName = stateName,
                IsPopup = isPopup
            };
        }

        /// <summary>
        /// Checks all menu states to determine if cursor reading should be suppressed.
        /// Returns suppression result with state name.
        ///
        /// Order matters:
        /// 1. Battle submenus checked FIRST (they have dedicated announcement patches)
        /// 2. Popup check (handled specially by caller with ReadCurrentButton)
        /// 3. Other menu states
        ///
        /// NOTE: Battle pause menu and battle context are handled by special cases in
        /// CursorNavigation_Postfix before this check runs.
        /// </summary>
        public static SuppressionResult Check()
        {
            // === BATTLE SUBMENUS ===

            if (BattleCommandState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleCommand");

            if (BattleTargetPatches.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleTarget");

            if (BattleItemMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleItem");

            if (BattleMagicMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleMagic");

            // === POPUP (special routing) ===

            if (PopupState.ShouldSuppress())
                return SuppressionResult.Suppressed("Popup", isPopup: true);

            // === OTHER MENUS ===

            if (ShopMenuTracker.ShouldSuppress())
                return SuppressionResult.Suppressed("Shop");

            if (ItemMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("ItemMenu");

            if (StatusMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("StatusMenu");

            if (EquipMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("EquipMenu");

            if (ConfigMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("ConfigMenu");

            if (MagicMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("MagicMenu");

            if (KeywordMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("KeywordMenu");

            if (WordsMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("WordsMenu");

            return SuppressionResult.None;
        }
    }
}
