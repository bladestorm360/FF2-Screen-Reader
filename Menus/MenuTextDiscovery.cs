using System;
using System.Collections;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using MelonLoader;
using UnityEngine;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Menus
{
    /// <summary>
    /// Core text discovery system that tries multiple strategies to find menu text.
    /// </summary>
    public static class MenuTextDiscovery
    {
        /// <summary>
        /// Coroutine to wait one frame then read cursor position.
        /// This delay is critical because the game updates cursor position asynchronously.
        /// </summary>
        public static IEnumerator WaitAndReadCursor(GameCursor cursor, string direction, int count, bool isLoop)
        {
            yield return null; // Wait one frame

            try
            {
                if (cursor == null || cursor.gameObject == null || cursor.transform == null)
                {
                    yield break;
                }

                int cursorIndex = cursor.Index;

                // Try multiple strategies to find menu text
                string menuText = TryAllStrategies(cursor);

                if (!string.IsNullOrEmpty(menuText))
                {
                    FFII_ScreenReaderMod.SpeakText(menuText);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in delayed cursor read: {ex.Message}");
            }
        }

        /// <summary>
        /// Try all text discovery strategies in sequence until one succeeds.
        /// </summary>
        private static string TryAllStrategies(GameCursor cursor)
        {
            string menuText = null;

            // Strategy 0: Check for save/load menu (priority for debugging location names)
            menuText = SaveSlotReader.TryReadSaveSlot(cursor.transform, cursor.Index);
            if (menuText != null) return menuText;

            // Strategy 1: Walk up parent hierarchy looking for direct text components
            menuText = TryDirectTextSearch(cursor.transform);
            if (menuText != null) return menuText;

            // Strategy 2: Try to find text in Content list by cursor index
            menuText = TryContentListSearch(cursor);
            if (menuText != null) return menuText;

            // Strategy 3: Fallback with GetComponentInChildren
            menuText = TryFallbackTextSearch(cursor.transform);
            if (menuText != null) return menuText;

            return null;
        }

        /// <summary>
        /// Strategy 1: Walk up parent hierarchy looking for direct text components.
        /// </summary>
        private static string TryDirectTextSearch(Transform cursorTransform)
        {
            Transform current = cursorTransform;
            int hierarchyDepth = 0;

            while (current != null && hierarchyDepth < 10)
            {
                try
                {
                    if (current.gameObject == null)
                        break;

                    // Look for text directly on this object (not children)
                    var text = current.GetComponent<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        string menuText = TextUtils.StripIconMarkup(text.text.Trim());
                        if (!string.IsNullOrEmpty(menuText))
                        {
                            return menuText;
                        }
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error walking hierarchy at depth {hierarchyDepth}: {ex.Message}");
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Strategy 2: Try to find text in Content list by cursor index.
        /// </summary>
        private static string TryContentListSearch(GameCursor cursor)
        {
            try
            {
                Transform current = cursor.transform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 15)
                {
                    // Try to find a Content list with indexed children
                    Transform contentList = FindContentList(current);
                    if (contentList != null && cursor.Index >= 0 && cursor.Index < contentList.childCount)
                    {
                        Transform selectedChild = contentList.GetChild(cursor.Index);

                        if (selectedChild != null)
                        {
                            // Look for text in this child
                            var text = selectedChild.GetComponentInChildren<UnityEngine.UI.Text>();
                            if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                            {
                                string menuText = TextUtils.StripIconMarkup(text.text.Trim());
                                if (!string.IsNullOrEmpty(menuText))
                                {
                                    return menuText;
                                }
                            }
                        }
                    }

                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in content list search: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Strategy 3: Final fallback with GetComponentInChildren.
        /// </summary>
        private static string TryFallbackTextSearch(Transform cursorTransform)
        {
            try
            {
                Transform current = cursorTransform;
                int hierarchyDepth = 0;

                while (current != null && hierarchyDepth < 10)
                {
                    if (current.gameObject == null)
                        break;

                    var text = current.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (text?.text != null && !string.IsNullOrEmpty(text.text.Trim()))
                    {
                        string menuText = TextUtils.StripIconMarkup(text.text.Trim());
                        if (!string.IsNullOrEmpty(menuText))
                        {
                            return menuText;
                        }
                    }
                    current = current.parent;
                    hierarchyDepth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in fallback text search: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find Content list under Scroll View.
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
    }
}
