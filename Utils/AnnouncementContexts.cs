namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized announcement deduplication context strings.
    /// Each context represents a distinct UI element or state that tracks
    /// what was last announced to avoid repeating the same text.
    /// </summary>
    public static class AnnouncementContexts
    {
        // Battle contexts
        public const string BATTLE_ACTION = "BattleAction";
        public const string BATTLE_ITEM = "BattleItem.Selection";
        public const string BATTLE_MAGIC = "BattleMagic.Selection";
        public const string BATTLE_MESSAGE = "BattleMessage.Action";
        public const string BATTLE_CONDITION = "BattleMessage.Condition";

        // Menu contexts
        public const string EQUIP_MENU = "EquipMenu.Selection";
        public const string ITEM_MENU = "ItemMenu.Selection";
        public const string STATUS_MENU = "StatusMenu.Selection";
        public const string SHOP_ITEM = "Shop.Item";
        public const string SHOP_QUANTITY = "Shop.Quantity";

        // Keyword contexts (FF2-specific)
        public const string KEYWORD_COMMAND = "Keyword.Command";
        public const string KEYWORD_WORD = "Keyword.Word";
        public const string WORDS_MENU = "WordsMenu.Selection";

        // Extras menu contexts
        public const string GALLERY_LIST_ENTRY = "Gallery.ListEntry";
        public const string MUSIC_LIST_ENTRY = "MusicPlayer.ListEntry";
        public const string BESTIARY_LIST_ENTRY = "Bestiary.ListEntry";
        public const string BESTIARY_DETAIL_STAT = "Bestiary.DetailStat";
        public const string BESTIARY_FORMATION = "Bestiary.Formation";
        public const string BESTIARY_MAP = "Bestiary.Map";
        public const string BESTIARY_STATE = "Bestiary.State";
        public const string TITLE_MENU_COMMAND = "TitleMenu.Command";
    }
}
