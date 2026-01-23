using System.Collections.Generic;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized deduplication for screen reader announcements.
    /// Replaces scattered lastAnnouncement/lastAnnouncementTime static variables across patch files.
    /// Uses simple equality comparison (no time-based logic).
    /// Ported from FF3 screen reader.
    /// </summary>
    public static class AnnouncementDeduplicator
    {
        private static readonly Dictionary<string, string> _lastStrings = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _lastInts = new Dictionary<string, int>();
        private static readonly Dictionary<string, object> _lastObjects = new Dictionary<string, object>();

        /// <summary>
        /// Checks if a string announcement should be spoken (different from last).
        /// Updates tracking if announcement is new.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "Shop.Item", "BattleItem.Selection")</param>
        /// <param name="text">The announcement text</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (_lastStrings.TryGetValue(context, out var last) && last == text)
                return false;

            _lastStrings[context] = text;
            return true;
        }

        /// <summary>
        /// Checks if an index-based announcement should be spoken (different from last).
        /// Updates tracking if index is new.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "BattleCommand.Cursor")</param>
        /// <param name="index">The current index</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, int index)
        {
            if (_lastInts.TryGetValue(context, out var last) && last == index)
                return false;

            _lastInts[context] = index;
            return true;
        }

        /// <summary>
        /// Checks if a combined index+string announcement should be spoken.
        /// Both must match the previous values to be considered a duplicate.
        /// Updates tracking if either is new.
        /// </summary>
        /// <param name="context">Unique context key</param>
        /// <param name="index">The current index</param>
        /// <param name="text">The announcement text</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, int index, string text)
        {
            string intKey = context + ".index";

            bool indexMatch = _lastInts.TryGetValue(intKey, out var lastIdx) && lastIdx == index;
            bool textMatch = _lastStrings.TryGetValue(context, out var lastText) && lastText == text;

            if (indexMatch && textMatch)
                return false;

            _lastInts[intKey] = index;
            _lastStrings[context] = text ?? string.Empty;
            return true;
        }

        /// <summary>
        /// Checks if an object reference announcement should be spoken (different from last).
        /// Uses reference equality for comparison.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "BattleResult.Data")</param>
        /// <param name="obj">The object reference</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, object obj)
        {
            if (obj == null)
                return false;

            if (_lastObjects.TryGetValue(context, out var last) && ReferenceEquals(last, obj))
                return false;

            _lastObjects[context] = obj;
            return true;
        }

        /// <summary>
        /// Resets tracking for a specific context.
        /// Call this when a menu opens/closes or state changes.
        /// </summary>
        public static void Reset(string context)
        {
            _lastStrings.Remove(context);
            _lastInts.Remove(context);
            _lastInts.Remove(context + ".index");
            _lastObjects.Remove(context);
        }

        /// <summary>
        /// Resets tracking for multiple contexts at once.
        /// </summary>
        public static void Reset(params string[] contexts)
        {
            foreach (var context in contexts)
            {
                Reset(context);
            }
        }

        /// <summary>
        /// Clears all tracking. Call on major state transitions (e.g., battle end, scene change).
        /// </summary>
        public static void ResetAll()
        {
            _lastStrings.Clear();
            _lastInts.Clear();
            _lastObjects.Clear();
        }

        #region Context Constants
        // Battle contexts
        public const string CONTEXT_BATTLE_ACTION = "BattleAction";  // Object-based: per-actor action deduplication
        public const string CONTEXT_BATTLE_ITEM = "BattleItem.Selection";
        public const string CONTEXT_BATTLE_MAGIC = "BattleMagic.Selection";
        public const string CONTEXT_BATTLE_MESSAGE = "BattleMessage.Action";
        public const string CONTEXT_BATTLE_CONDITION = "BattleMessage.Condition";

        // Menu contexts
        public const string CONTEXT_EQUIP_MENU = "EquipMenu.Selection";
        public const string CONTEXT_ITEM_MENU = "ItemMenu.Selection";
        public const string CONTEXT_STATUS_MENU = "StatusMenu.Selection";
        public const string CONTEXT_SHOP_ITEM = "Shop.Item";
        public const string CONTEXT_SHOP_QUANTITY = "Shop.Quantity";

        // Keyword contexts
        public const string CONTEXT_KEYWORD_COMMAND = "Keyword.Command";
        public const string CONTEXT_KEYWORD_WORD = "Keyword.Word";
        public const string CONTEXT_WORDS_MENU = "WordsMenu.Selection";
        #endregion
    }
}
