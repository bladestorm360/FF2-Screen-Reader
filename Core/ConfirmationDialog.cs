using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Simple Yes/No confirmation dialog using Windows API focus stealing.
    /// Used for waypoint deletion confirmations.
    /// </summary>
    public static class ConfirmationDialog
    {
        /// <summary>
        /// Whether the confirmation dialog is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static string prompt = "";
        private static Action onYesCallback;
        private static Action onNoCallback;
        private static bool selectedYes = true; // Default selection is Yes
        private static bool silentYesMode = false; // When true, Yes confirmation skips announcement

        /// <summary>
        /// Opens the confirmation dialog.
        /// </summary>
        /// <param name="promptText">Prompt to display to user (spoken via TTS)</param>
        /// <param name="onYes">Callback when user confirms Yes</param>
        /// <param name="onNo">Callback when user confirms No</param>
        /// <param name="silentYes">When true, Yes confirmation invokes callback immediately without announcement</param>
        public static void Open(string promptText, Action onYes, Action onNo = null, bool silentYes = false)
        {
            if (IsOpen) return;

            IsOpen = true;
            prompt = promptText ?? "";
            onYesCallback = onYes;
            onNoCallback = onNo;
            selectedYes = true; // Default to Yes
            silentYesMode = silentYes;

            // Initialize key states to prevent keys from triggering immediately
            WindowsFocusHelper.InitializeKeyStates(new[] {
                WindowsFocusHelper.VK_RETURN, WindowsFocusHelper.VK_ESCAPE,
                WindowsFocusHelper.VK_LEFT, WindowsFocusHelper.VK_RIGHT,
                WindowsFocusHelper.VK_Y, WindowsFocusHelper.VK_N
            });

            // Only steal focus if not in silent Yes mode (normal confirmation flow)
            if (!silentYes)
            {
                WindowsFocusHelper.StealFocus("FFII_ConfirmDialog");
            }

            // Announce prompt with delay to avoid NVDA window title interruption
            CoroutineManager.StartManaged(DelayedPromptAnnouncement($"{prompt} Yes or No"));
        }

        /// <summary>
        /// Announces the prompt after a short delay to avoid NVDA announcing the window title first.
        /// </summary>
        private static IEnumerator DelayedPromptAnnouncement(string text)
        {
            yield return new WaitForSeconds(0.1f);
            FFII_ScreenReaderMod.SpeakText(text, interrupt: true);
        }

        /// <summary>
        /// Closes the dialog, waits for NVDA to finish announcing window title, then speaks and invokes callback.
        /// </summary>
        private static IEnumerator DelayedCloseAnnouncement(string text, Action callback)
        {
            Close();
            yield return new WaitForSeconds(0.1f);
            FFII_ScreenReaderMod.SpeakText(text, interrupt: true);
            callback?.Invoke();
        }

        /// <summary>
        /// Closes the confirmation dialog and restores focus to game.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();

            // Clear callbacks
            onYesCallback = null;
            onNoCallback = null;
        }

        /// <summary>
        /// Closes the dialog and announces text after focus is restored.
        /// Uses a coroutine delay to prevent NVDA window title from interrupting.
        /// </summary>
        public static void CloseWithAnnouncement(string text)
        {
            if (!IsOpen) return;

            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();

            // Clear callbacks
            onYesCallback = null;
            onNoCallback = null;

            // Announce after focus restoration settles
            if (!string.IsNullOrEmpty(text))
            {
                CoroutineManager.StartManaged(DelayedPromptAnnouncement(text));
            }
        }

        /// <summary>
        /// Handles keyboard input for the confirmation dialog.
        /// Should be called from InputManager.Update() before any other input handling.
        /// Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            // Y key - confirm Yes immediately
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_Y))
            {
                var callback = onYesCallback;
                if (silentYesMode)
                {
                    Close();
                    callback?.Invoke();
                }
                else
                {
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement("Yes", callback));
                }
                return true;
            }

            // N key - confirm No immediately
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_N))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement("Cancelled", callback));
                return true;
            }

            // Escape - same as No
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_ESCAPE))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement("Cancelled", callback));
                return true;
            }

            // Enter - confirm current selection
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RETURN))
            {
                if (selectedYes)
                {
                    var callback = onYesCallback;
                    if (silentYesMode)
                    {
                        Close();
                        callback?.Invoke();
                    }
                    else
                    {
                        CoroutineManager.StartManaged(DelayedCloseAnnouncement("Yes", callback));
                    }
                }
                else
                {
                    var callback = onNoCallback;
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement("Cancelled", callback));
                }
                return true;
            }

            // Left/Right arrows - toggle selection
            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_LEFT) || WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RIGHT))
            {
                selectedYes = !selectedYes;
                string selection = selectedYes ? "Yes" : "No";
                FFII_ScreenReaderMod.SpeakText(selection, interrupt: true);
                return true;
            }

            return true; // Consume all input while dialog is open
        }
    }
}
