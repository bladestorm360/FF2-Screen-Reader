using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Simple Yes/No confirmation dialog using Unity Input.GetKeyDown.
    /// Game input suppressed via ControllerRouter.SuppressGameInput + InputSystemManager patches.
    /// </summary>
    internal static class ConfirmationDialog
    {
        public static bool IsOpen { get; private set; }

        private static string prompt = "";
        private static Action onYesCallback;
        private static Action onNoCallback;
        private static bool selectedYes = true;

        public static void Open(string promptText, Action onYes, Action onNo = null)
        {
            if (IsOpen) return;

            IsOpen = true;
            prompt = promptText ?? "";
            onYesCallback = onYes;
            onNoCallback = onNo;
            selectedYes = true;

            CoroutineManager.StartManaged(DelayedPromptAnnouncement($"{prompt} {T("Yes or No")}"));
        }

        private static IEnumerator DelayedPromptAnnouncement(string text)
        {
            yield return new WaitForSeconds(0.1f);
            FFII_ScreenReaderMod.SpeakText(text, interrupt: true);
        }

        private static IEnumerator DelayedCloseAnnouncement(string text, Action callback)
        {
            Close();
            yield return new WaitForSeconds(0.1f);
            FFII_ScreenReaderMod.SpeakText(text, interrupt: true);
            callback?.Invoke();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            onYesCallback = null;
            onNoCallback = null;
        }

        /// <summary>
        /// Handles keyboard input via Unity Input.GetKeyDown.
        /// Game input suppressed via InputSystemManager patches when IsOpen.
        /// Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            if (GamepadManager.IsKeyCodePressed(KeyCode.Y))
            {
                var callback = onYesCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Yes"), callback));
                return true;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.N))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("No"), callback));
                return true;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.Escape))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Cancelled"), callback));
                return true;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.Return))
            {
                if (selectedYes)
                {
                    var callback = onYesCallback;
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Yes"), callback));
                }
                else
                {
                    var callback = onNoCallback;
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("No"), callback));
                }
                return true;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.LeftArrow) || GamepadManager.IsKeyCodePressed(KeyCode.RightArrow))
            {
                selectedYes = !selectedYes;
                string selection = selectedYes ? T("Yes") : T("No");
                FFII_ScreenReaderMod.SpeakText(selection, interrupt: true);
                return true;
            }

            return true; // Consume all input while dialog is open
        }
    }
}
