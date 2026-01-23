using System;
using MelonLoader;

// Type aliases for IL2CPP types
using MessageManager = Il2CppLast.Management.MessageManager;
using Condition = Il2CppLast.Data.Master.Condition;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized utility for localization and message lookups.
    /// Consolidates duplicate implementations from:
    /// - Patches/ItemMenuPatches.cs (GetConditionName, GetLocalizedCommand)
    /// - Patches/StatusMenuPatches.cs (GetConditionName)
    /// - Patches/MagicMenuPatches.cs (GetConditionName)
    /// - Various patches using MessageManager.Instance.GetMessage
    /// </summary>
    public static class LocalizationUtility
    {
        /// <summary>
        /// Gets a localized message by message ID.
        /// </summary>
        /// <param name="mesId">The message ID (e.g., "$menu_item_use", "COND_01").</param>
        /// <param name="stripIcons">Whether to strip icon markup from the result.</param>
        /// <returns>The localized message, or null if not found.</returns>
        public static string GetMessage(string mesId, bool stripIcons = true)
        {
            if (string.IsNullOrEmpty(mesId))
                return null;

            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return null;

                string text = messageManager.GetMessage(mesId, false);
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return stripIcons ? TextUtils.StripIconMarkup(text) : text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a localized condition/status effect name from a Condition object.
        /// </summary>
        /// <param name="condition">The Condition object.</param>
        /// <returns>The localized condition name, or null if not found.</returns>
        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            try
            {
                string mesId = condition.MesIdName;
                if (string.IsNullOrEmpty(mesId))
                    return null;

                return GetMessage(mesId, stripIcons: false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a localized command text (e.g., "Use", "Sort", "Key Items").
        /// Automatically strips icon markup.
        /// </summary>
        /// <param name="mesId">The message ID (e.g., "$menu_item_use").</param>
        /// <returns>The localized command text, or null if not found.</returns>
        public static string GetLocalizedCommand(string mesId)
        {
            return GetMessage(mesId, stripIcons: true);
        }

        /// <summary>
        /// Gets a localized enemy name from its message ID.
        /// </summary>
        /// <param name="mesIdName">The enemy's message ID for its name.</param>
        /// <returns>The localized enemy name, or null if not found.</returns>
        public static string GetEnemyName(string mesIdName)
        {
            if (string.IsNullOrEmpty(mesIdName))
                return null;

            return GetMessage(mesIdName, stripIcons: true);
        }

        /// <summary>
        /// Gets a localized ability name from an ability's message ID.
        /// </summary>
        /// <param name="mesIdName">The ability's MesIdName property.</param>
        /// <returns>The localized ability name, or null if not found.</returns>
        public static string GetAbilityName(string mesIdName)
        {
            if (string.IsNullOrEmpty(mesIdName))
                return null;

            return GetMessage(mesIdName, stripIcons: false);
        }

        /// <summary>
        /// Gets a localized ability description from an ability's message ID.
        /// </summary>
        /// <param name="mesIdDescription">The ability's MesIdDescription property.</param>
        /// <returns>The localized ability description, or null if not found.</returns>
        public static string GetAbilityDescription(string mesIdDescription)
        {
            if (string.IsNullOrEmpty(mesIdDescription))
                return null;

            return GetMessage(mesIdDescription, stripIcons: false);
        }
    }
}
