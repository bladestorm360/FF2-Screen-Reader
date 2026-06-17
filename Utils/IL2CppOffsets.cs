namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized IL2CPP memory offsets and state machine enum values.
    /// All offsets are derived from dump.cs analysis and verified at runtime.
    /// If the game updates, check these first for breakage.
    /// </summary>
    internal static class IL2CppOffsets
    {
        /// <summary>
        /// Common state machine reading offsets (used by StateReaderHelper).
        /// </summary>
        internal static class StateMachine
        {
            public const int OFFSET_CURRENT = 0x10;   // StateMachine.current pointer
            public const int OFFSET_TAG = 0x10;        // State.Tag (int)
        }

        /// <summary>
        /// BattleCommandSelectController state values.
        /// Used by BattleItemPatches and BattleMagicPatches for sub-state detection.
        /// </summary>
        internal static class BattleCommand
        {
            public const int STATE_NORMAL = 1;       // Main command menu (Attack, Magic, etc.)
            public const int STATE_EXTRA = 2;        // Sub-commands (White Magic, Black Magic, etc.)
        }

        /// <summary>
        /// BattleItemMenuController offsets.
        /// </summary>
        internal static class BattleItem
        {
            public const int OFFSET_DISPLAY_DATA_LIST = 0xE0;
        }

        /// <summary>
        /// BattleMagicMenuController offsets.
        /// </summary>
        internal static class BattleMagic
        {
            public const int OFFSET_SELECTED_PLAYER = 0x28;
            public const int OFFSET_DATA_LIST = 0x70;
            public const int OFFSET_CONTENT_LIST = 0x78;
            public const int OFFSET_CONTENT_GAUGE = 0x38;
            public const int OFFSET_GAUGE_IMAGE = 0x18;
        }

        /// <summary>
        /// BattlePauseController / BattleUIManager offsets.
        /// </summary>
        internal static class BattlePause
        {
            public const int OFFSET_SELECT_CURSOR = 0x68;
            public const int OFFSET_COMMAND_LIST = 0x70;
            public const int OFFSET_COMMAND_TEXT = 0x18;
        }

        /// <summary>
        /// EquipmentWindowController state values.
        /// </summary>
        internal static class Equipment
        {
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND = 1;
            public const int STATE_INFO = 2;
            public const int STATE_SELECT = 3;

            // Live equip detail panel (KeyInput.EquipmentDescriptionWindowController).
            // The game renders the active panel (stats OR description) into this single Text.
            public const int DescriptionView = 0x20;   // EquipmentDescriptionWindowController.view
            public const int DescriptionText = 0x18;   // EquipmentDescriptionWindowView.descriptionText
        }

        /// <summary>
        /// Live shop info panel (KeyInput.ShopInfoController). The game renders the active
        /// panel (stats OR description) into a single Text — read it directly (FF1 pattern).
        /// </summary>
        internal static class ShopInfo
        {
            public const int InfoView = 0x18;        // ShopInfoController.view
            public const int DescriptionText = 0x38; // ShopInfoView.descriptionText
        }

        /// <summary>
        /// Item-menu live info panel (Last.UI.ItemWindowView). descriptionText holds the
        /// item description (consumables / equipment description mode); equipment STATS are
        /// rendered into ItemEquipmentDetailView row controllers (read via visible Text).
        /// </summary>
        internal static class ItemPanel
        {
            public const int WindowViewDescriptionText = 0x18; // ItemWindowView.descriptionText
            public const int WindowViewIsFrontTextVisible = 0x48; // ItemWindowView.isFrontTextVisible (stats vs desc)
            public const int EquipmentDetailControllerView = 0x18; // ItemEquipmentDetailController.view
        }

        /// <summary>
        /// SubSceneManagerMainGame state values.
        /// </summary>
        internal static class GameState
        {
            public const int STATE_CHANGE_MAP = 1;
            public const int STATE_FIELD_READY = 2;
            public const int STATE_PLAYER = 3;
            public const int STATE_BATTLE = 13;
        }

        /// <summary>
        /// ItemWindowController state values.
        /// </summary>
        internal static class Item
        {
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND_SELECT = 1;
            public const int STATE_USE_SELECT = 2;
            public const int STATE_IMPORTANT_SELECT = 3;
            public const int STATE_ORGANIZE_SELECT = 4;
            public const int STATE_TARGET_SELECT = 5;
        }

        /// <summary>
        /// KeyInput.ItemUseController offsets and state values (dump.cs:451796).
        /// The inner controller that drives the "use item on a character" target list;
        /// its own state machine distinguishes target-selection from confirm/learning.
        /// </summary>
        internal static class ItemUse
        {
            public const int OFFSET_STATE_MACHINE = 0x70; // StateMachine<ItemUseController.State>
            public const int OFFSET_NEXT_STATE = 0x78;    // ItemUseController.State nextState (enum int)

            // ItemUseController.State enum values
            public const int STATE_NON = 0;
            public const int STATE_SINGLE = 1;                    // selecting a single target
            public const int STATE_ALL = 2;                       // selecting all targets
            public const int STATE_LEARNING_VERIFICATION = 3;     // confirm popup (Tome learn)
            public const int STATE_LEARNING_AND_SET_VERIFICATION = 4;
            public const int STATE_LEARNING_ERROR_POPUP = 5;
        }

        /// <summary>
        /// SecretWordController / WordsWindowController offsets.
        /// </summary>
        internal static class Keyword
        {
            // SecretWordController offsets
            public const int OFFSET_STATE_MACHINE = 0x20;          // SecretWordControllerBase.stateMachine
            public const int OFFSET_SELECT_CONTENT_CURSOR = 0x30;
            public const int OFFSET_SELECT_COMMAND_CURSOR = 0x38;  // SecretWordControllerBase.selectCommandCursor
            public const int OFFSET_WORD_DATA_LIST = 0x60;
            public const int OFFSET_ITEM_DATA_LIST = 0x68;
            public const int OFFSET_SFCD_NAME_MESSAGE_ID = 0x18;
            public const int OFFSET_SFCD_DESCRIPTION_MESSAGE_ID = 0x20;
            public const int OFFSET_ILCD_NAME = 0x20;
            public const int OFFSET_ILCD_DESCRIPTION = 0x28;
            public const int OFFSET_CURSOR_INDEX = 0x20;

            // SecretWordControllerBase.State enum values
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND_SELECT = 1;
            public const int STATE_COMMAND_SELECTING = 2;

            // WordsWindowController (KeyInput) offsets
            public const int OFFSET_WORDS_CONTENT_LIST = 0x28;
            public const int OFFSET_WORDS_SELECT_CURSOR = 0x30;
            public const int OFFSET_WORDS_KEYWORD_DICTIONARY = 0x38;
            public const int OFFSET_CCCC_NAME = 0x20;

            // WordsWindowController (Touch) offsets
            public const int OFFSET_TOUCH_WORDS_VIEW = 0x18;
            public const int OFFSET_TOUCH_WORDS_CONTENT_LIST = 0x20;
            public const int OFFSET_TOUCH_WORDS_SELECT_CURSOR = 0x28;
        }

        /// <summary>
        /// AbilityWindowController (magic menu) offsets and state values.
        /// </summary>
        internal static class Magic
        {
            public const int OFFSET_STATE_MACHINE = 0x88;
            public const int OFFSET_CONTENT_LIST = 0x50;
            public const int OFFSET_TARGET_CHARACTER = 0x78;
            public const int OFFSET_USE_CONTENT_LIST = 0x40;
            public const int OFFSET_USE_SELECT_CURSOR = 0x48;
            public const int OFFSET_GAUGE_IMAGE = 0x18;
            // AbilityContentListController.selectCursor (KeyInput, dump.cs:278749) — used to
            // announce the initially-focused spell when the Use/Forget list activates.
            public const int OFFSET_LIST_SELECT_CURSOR = 0x38;
        }

        /// <summary>
        /// MainMenuController offsets (KeyInput variant — the active controller for keyboard/gamepad on PC).
        /// </summary>
        internal static class MainMenu
        {
            public const int OFFSET_FOCUS_ID = 0x90;  // MenuCommandId focusId (KeyInput variant, dump.cs:443866)
        }

        /// <summary>
        /// MessageWindowManager offsets.
        /// </summary>
        internal static class MessageWindow
        {
            public const int OFFSET_MESSAGE_LIST = 0x88;
            public const int OFFSET_NEW_PAGE_LINE_LIST = 0xA0;
            public const int OFFSET_SPEAKER_VALUE = 0xA8;
            public const int OFFSET_CURRENT_PAGE_NUMBER = 0xF8;
        }

        /// <summary>
        /// CommonPopup and related popup offsets.
        /// </summary>
        internal static class Popup
        {
            // IconTextView
            public const int ICON_TEXT_VIEW_NAME_TEXT_OFFSET = 0x20;

            // CommonCommand
            public const int COMMON_COMMAND_TEXT_OFFSET = 0x18;

            // CommonPopupView
            public const int COMMON_TITLE_OFFSET = 0x38;
            public const int COMMON_MESSAGE_OFFSET = 0x40;
            public const int COMMON_CMDLIST_OFFSET = 0x70;
            public const int COMMON_SELECT_CURSOR_OFFSET = 0x68;   // CommonPopup.selectCursor (dump.cs:457709)

            // MagicStonePopupView
            public const int MAGICSTONE_NAME_OFFSET = 0x28;
            public const int MAGICSTONE_DESC_OFFSET = 0x30;
            public const int MAGICSTONE_CMDLIST_OFFSET = 0x58;
            public const int MAGICSTONE_SELECT_CURSOR_OFFSET = 0x50; // ChangeMagicStonePopup.selectCursor (dump.cs:457506)

            // GameOverPopupView
            public const int GAMEOVER_SELECT_CURSOR_OFFSET = 0x38;
            public const int GAMEOVER_CMDLIST_OFFSET = 0x40;

            // GameOverPopupLoadView
            public const int GAMEOVERLOAD_TITLE_OFFSET = 0x38;
            public const int GAMEOVERLOAD_MESSAGE_OFFSET = 0x40;
            public const int GAMEOVERLOAD_SELECT_CURSOR_OFFSET = 0x58;
            public const int GAMEOVERLOAD_CMDLIST_OFFSET = 0x60;

            // GameOverPopupController
            public const int GAMEOVERPOPUPCTRL_VIEW_OFFSET = 0x30;
            public const int GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET = 0x18;

            // InformationPopupView
            public const int INFO_TITLE_OFFSET = 0x28;
            public const int INFO_MESSAGE_OFFSET = 0x30;

            // InputPopupView / ChangeNamePopupView
            public const int INPUT_DESC_OFFSET = 0x30;
            public const int CHANGENAME_DESC_OFFSET = 0x30;
        }

        /// <summary>
        /// Save/Load popup offsets.
        /// </summary>
        internal static class SaveLoad
        {
            public const int SAVE_POPUP_MESSAGE_TEXT_OFFSET = 0x40;
            public const int SAVE_POPUP_SELECT_CURSOR_OFFSET = 0x58;
            public const int SAVE_POPUP_COMMAND_LIST_OFFSET = 0x60;
            public const int COMMON_COMMAND_TEXT_OFFSET = 0x18;
            public const int TITLE_LOAD_SAVE_POPUP_OFFSET = 0x58;
            public const int MAIN_MENU_SAVE_POPUP_OFFSET = 0x28;
            public const int INTERRUPTION_SAVE_POPUP_OFFSET = 0x38;
        }

        /// <summary>
        /// ShopController offsets and state values.
        /// </summary>
        internal static class Shop
        {
            // ShopController.State values
            public const int STATE_NONE = 0;
            public const int STATE_SELECT_COMMAND = 1;
            public const int STATE_SELECT_PRODUCT = 2;     // buy list
            public const int STATE_SELECT_SELL_ITEM = 3;   // sell list
            public const int OFFSET_SELECTED_COUNT = 0x3C;

            // ShopListMainContentController offsets
            public const int LIST_MAIN_SELECT_CURSOR = 0x48;
            public const int LIST_MAIN_PRODUCT_LIST = 0x68;
        }

        /// <summary>
        /// StatusDetailsControllerBase offsets.
        /// </summary>
        internal static class StatusDetails
        {
            public const int OFFSET_SKILL_VIEW = 0x18;
            public const int OFFSET_SKILL_WEAPON_TYPE = 0x20; // SkillLevelContentController.weaponType (SkillLevelTarget enum)
            public const int OFFSET_GAUGE_IMAGE = 0x18;
            public const int OFFSET_SKILL_LEVEL_CONTENT_LIST_KEYINPUT = 0x80;
            public const int OFFSET_CONTENT_LIST = 0x48;
            public const int OFFSET_PARAMETER_TYPE = 0x18;
            public const int OFFSET_PARAMETER_VIEW = 0x20;
            public const int OFFSET_MULTIPLIED_VALUE_TEXT = 0x28;
            public const int PARAMETER_TYPE_ACCURACY_RATE = 16;
        }

        /// <summary>
        /// Transport type values (TransportType enum in game).
        /// Used by MovementSpeechPatches and MoveStateHelper.
        /// </summary>
        internal static class Transport
        {
            public const int TRANSPORT_NONE = 0;
            public const int TRANSPORT_PLAYER = 1;
            public const int TRANSPORT_SHIP = 2;
            public const int TRANSPORT_PLANE = 3;
            public const int TRANSPORT_SYMBOL = 4;
            public const int TRANSPORT_CONTENT = 5;
            public const int TRANSPORT_SUBMARINE = 6;
            public const int TRANSPORT_LOWFLYING = 7;
            public const int TRANSPORT_SPECIALPLANE = 8;
            public const int TRANSPORT_YELLOWCHOCOBO = 9;
            public const int TRANSPORT_BLACKCHOCOBO = 10;
            public const int TRANSPORT_BOKO = 11;
        }
    }
}
