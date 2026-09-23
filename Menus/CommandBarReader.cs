using FFII_ScreenReader.Core;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Command-id enums (each command bar uses its own).
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;
using EquipmentCommandId = Il2CppLast.UI.EquipmentCommandId;

namespace FFII_ScreenReader.Menus
{
    /// <summary>
    /// Command-bar name maps + a shared announce. Each command bar's open-trigger
    /// (CommandBarPatches / MagicMenuPatches) reads the focused command's ID ENUM (placeholder-proof,
    /// unlike the async-localized on-screen Text) and announces it via <see cref="Announce"/>.
    /// No dedup/gate lives here — the per-bar arm flag in CommandBarPatches guarantees one announce
    /// per open. Shop also borrows <see cref="GetShopCommandName"/> for its own reader.
    /// </summary>
    public static class CommandBarReader
    {
        /// <summary>Speaks a resolved command name (no-op if empty).</summary>
        public static void Announce(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return;
            FFII_ScreenReaderMod.SpeakText(commandName, interrupt: true);
        }

        /// <summary>Speaks a command name with a positional "(n of N)" suffix (index is ZERO-BASED).</summary>
        public static void Announce(string commandName, int index, int count)
        {
            Announce(FFII_ScreenReader.Utils.MenuPosition.Format(commandName, index, count));
        }

        public static string GetShopCommandName(ShopCommandId id) => id switch
        {
            ShopCommandId.Buy => T("Buy"),
            ShopCommandId.Sell => T("Sell"),
            ShopCommandId.Equipment => T("Equipment"),
            ShopCommandId.Back => T("Back"),
            _ => null
        };

        public static string GetItemCommandName(ItemCommandId id) => id switch
        {
            ItemCommandId.Use => T("Use"),
            ItemCommandId.Organize => T("Sort"),
            ItemCommandId.Important => T("Key Items"),
            _ => null
        };

        public static string GetEquipmentCommandName(EquipmentCommandId id) => id switch
        {
            EquipmentCommandId.Equip => T("Equip"),
            EquipmentCommandId.Strongest => T("Strongest"),
            EquipmentCommandId.RemoveEverything => T("Remove All"),
            _ => null
        };
    }
}
