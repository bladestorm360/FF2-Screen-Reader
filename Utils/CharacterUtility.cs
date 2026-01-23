using System;
using MelonLoader;

// Type aliases for IL2CPP types
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized utility for character-related lookups.
    /// Consolidates duplicate GetCharacterRow implementations from:
    /// - Patches/ItemMenuPatches.cs
    /// - Patches/StatusMenuPatches.cs
    /// - Menus/CharacterSelectionReader.cs
    /// </summary>
    public static class CharacterUtility
    {
        /// <summary>
        /// Gets the character's row position (Front Row / Back Row) from Corps data.
        /// </summary>
        /// <param name="characterData">The character data to look up.</param>
        /// <returns>"Front Row", "Back Row", or null if not found.</returns>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                    return null;

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null && corps.CharacterId == characterId)
                    {
                        return corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CharacterUtility] Error getting character row: {ex.Message}");
            }

            return null;
        }
    }
}
