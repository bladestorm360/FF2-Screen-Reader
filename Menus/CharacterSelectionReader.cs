using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using FFII_ScreenReader.Utils;
using MenuManager = Il2CppLast.UI.MenuManager;
using StatusWindowContentControllerBase = Il2CppSerial.Template.UI.StatusWindowContentControllerBase;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;

namespace FFII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character information from character selection screens.
    /// Used in menus like Status, Magic, Equipment, Item target, etc.
    /// FF2 specific: No jobs, includes MP instead of spell charges.
    /// Format: "Name, Row, Level X, HP current/max, MP current/max"
    /// </summary>
    public static class CharacterSelectionReader
    {
        /// <summary>
        /// Try to read character information from the current cursor position.
        /// Returns a formatted string with character information, or null if not a character selection.
        /// </summary>
        public static string TryReadCharacterSelection(Transform cursorTransform, int cursorIndex)
        {
            try
            {
                // Safety check: Only read character data if we're in a menu or battle
                var sceneName = SceneManager.GetActiveScene().name;
                bool isBattleScene = sceneName != null && sceneName.Contains("Battle");
                bool isMenuOpen = false;

                try
                {
                    var menuManager = MenuManager.Instance;
                    if (menuManager != null)
                    {
                        isMenuOpen = menuManager.IsOpen;
                    }
                }
                catch { }

                if (!isBattleScene && !isMenuOpen)
                {
                    return null;
                }

                // Walk up the hierarchy to find character selection structures
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string lowerName = current.name.ToLower();

                    // Look for character selection menu structures
                    if (lowerName.Contains("character") || lowerName.Contains("chara") ||
                        lowerName.Contains("status") || lowerName.Contains("formation") ||
                        lowerName.Contains("party") || lowerName.Contains("member"))
                    {
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.childCount)
                        {
                            Transform characterSlot = contentList.GetChild(cursorIndex);
                            string characterInfo = ReadCharacterInformation(characterSlot);
                            if (characterInfo != null)
                            {
                                return characterInfo;
                            }
                        }
                    }

                    if (lowerName.Contains("info_content") || lowerName.Contains("status_info") ||
                        lowerName.Contains("chara_status"))
                    {
                        string characterInfo = ReadCharacterInformation(current);
                        if (characterInfo != null)
                        {
                            return characterInfo;
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CharacterSelectionReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find the Content transform within a ScrollView structure.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            try
            {
                var allTransforms = root.GetComponentsInChildren<Transform>();
                foreach (var t in allTransforms)
                {
                    if (t.name == "Content" && t.parent != null &&
                        (t.parent.name == "Viewport" || t.parent.parent?.name == "Scroll View"))
                    {
                        return t;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Read character information from a character slot transform.
        /// </summary>
        private static string ReadCharacterInformation(Transform slotTransform)
        {
            try
            {
                var contentController = slotTransform.GetComponent<StatusWindowContentControllerBase>();
                if (contentController == null)
                {
                    contentController = slotTransform.GetComponentInChildren<StatusWindowContentControllerBase>();
                }

                if (contentController != null)
                {
                    try
                    {
                        var characterData = contentController.CharacterData;
                        if (characterData != null)
                        {
                            string result = ReadFromCharacterData(characterData);
                            if (!string.IsNullOrEmpty(result))
                            {
                                return result;
                            }
                        }
                    }
                    catch { }
                }

                // Fallback to text components
                return ReadFromTextComponents(slotTransform);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character information: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read character information directly from OwnedCharacterData.
        /// FF2 format: "Name, Row, Level X, HP current/max, MP current/max"
        /// No jobs in FF2.
        /// </summary>
        private static string ReadFromCharacterData(OwnedCharacterData characterData)
        {
            try
            {
                var parts = new List<string>();

                // Character name
                string name = characterData.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    parts.Add(name);
                }

                // FF2: No jobs - skip job name

                // Row (Front/Back)
                string row = CharacterUtility.GetCharacterRow(characterData);
                if (!string.IsNullOrEmpty(row))
                {
                    parts.Add(row);
                }

                // Level, HP, MP from Parameter
                var parameter = characterData.Parameter;
                if (parameter != null)
                {
                    int level = parameter.BaseLevel;
                    if (level > 0)
                    {
                        parts.Add($"Level {level}");
                    }

                    int currentHp = parameter.currentHP;
                    int maxHp = parameter.ConfirmedMaxHp();
                    if (maxHp > 0)
                    {
                        parts.Add($"HP {currentHp}/{maxHp}");
                    }

                    // FF2 specific: Include MP (unlike FF3's spell charges)
                    int currentMp = parameter.currentMP;
                    int maxMp = parameter.ConfirmedMaxMp();
                    if (maxMp > 0)
                    {
                        parts.Add($"MP {currentMp}/{maxMp}");
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join(", ", parts);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CharacterSelectionReader: Error in ReadFromCharacterData: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read character information from text components (fallback).
        /// </summary>
        private static string ReadFromTextComponents(Transform slotTransform)
        {
            try
            {
                string characterName = null;
                string level = null;
                string currentHP = null;
                string maxHP = null;
                string currentMP = null;
                string maxMP = null;

                TextUtils.ForEachTextInChildren(slotTransform, text =>
                {
                    if (text == null || text.text == null) return;

                    string content = text.text.Trim();
                    if (string.IsNullOrEmpty(content)) return;

                    string textName = text.name.ToLower();

                    if ((textName.Contains("name") && !textName.Contains("area") && !textName.Contains("floor")) ||
                        textName == "nametext" || textName == "name_text")
                    {
                        if (content.Length > 1 && !content.Contains(":") &&
                            content != "HP" && content != "MP" && content != "Lv")
                        {
                            characterName = content;
                        }
                    }
                    else if ((textName.Contains("level") || textName.Contains("lv")) &&
                             !textName.Contains("label") && !textName.Contains("fixed"))
                    {
                        if (content != "Lv" && content != "Level" && content != "LV")
                        {
                            level = content;
                        }
                    }
                    else if (textName.Contains("hp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentHP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxHP = content;
                        }
                    }
                    else if (textName.Contains("mp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentMP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxMP = content;
                        }
                    }
                });

                // Build announcement
                string announcement = "";

                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = characterName;
                }

                // FF2: No jobs

                if (!string.IsNullOrEmpty(level))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ", Level " + level;
                    }
                    else
                    {
                        announcement = "Level " + level;
                    }
                }

                if (!string.IsNullOrEmpty(currentHP))
                {
                    if (!string.IsNullOrEmpty(maxHP))
                    {
                        announcement += $", HP {currentHP}/{maxHP}";
                    }
                    else
                    {
                        announcement += $", HP {currentHP}";
                    }
                }

                // FF2: Include MP
                if (!string.IsNullOrEmpty(currentMP))
                {
                    if (!string.IsNullOrEmpty(maxMP))
                    {
                        announcement += $", MP {currentMP}/{maxMP}";
                    }
                    else
                    {
                        announcement += $", MP {currentMP}";
                    }
                }

                if (!string.IsNullOrEmpty(announcement))
                {
                    return announcement;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character text components: {ex.Message}");
            }

            return null;
        }
    }
}
