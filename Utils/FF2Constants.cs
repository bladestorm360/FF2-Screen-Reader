namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Game-specific constants for FF2.
    /// Centralizes magic numbers and state values used across multiple files.
    /// </summary>
    public static class FF2Constants
    {
        /// <summary>
        /// Size of one map cell in world units. One step = one cell = 16 world units.
        /// </summary>
        public const float TILE_SIZE = 16f;

        /// <summary>
        /// Battle start condition state values (PreeMptiveState enum).
        /// Used by BattleMessagePatches to announce encounter types.
        /// </summary>
        public static class BattleStartStates
        {
            public const int STATE_NON = -1;
            public const int STATE_NORMAL = 0;
            public const int STATE_PREEMPTIVE = 1;
            public const int STATE_BACK_ATTACK = 2;
            public const int STATE_ENEMY_PREEMPTIVE = 3;
            public const int STATE_ENEMY_SIDE_ATTACK = 4;
            public const int STATE_SIDE_ATTACK = 5;
        }

        /// <summary>
        /// AbilityWindowController (magic menu) state machine values.
        /// </summary>
        public static class MagicMenuStates
        {
            public const int STATE_NONE = 0;
            public const int STATE_USE_LIST = 1;
            public const int STATE_USE_TARGET = 2;
            public const int STATE_FORGET = 3;
            public const int STATE_COMMAND = 4;
            public const int STATE_POPUP = 5;
            public const int STATE_ORDERLY = 6;
            public const int STATE_SELF_ORDERLY = 7;
            public const int STATE_SELF_ORDERLY_TARGET = 8;
        }
    }
}
