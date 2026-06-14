namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Holds the on-demand detail (description/stats) for the currently-focused menu item
    /// so it stays reachable via the I key even when AutoDetail is off and the focus
    /// announcement is terse. Written by item/magic/equip focus handlers; read by the I key.
    /// Only one menu is focused at a time, so the latest write reflects the current item.
    /// (FF2 has no equip restrictions, so there is no "who can equip" U-key here.)
    /// </summary>
    public static class MenuDetailCache
    {
        /// <summary>Description/stats text the I key reads (null if none).</summary>
        public static string LastDetail { get; private set; }

        public static void Set(string detail)
        {
            LastDetail = detail;
        }

        public static void Clear()
        {
            LastDetail = null;
        }
    }
}
