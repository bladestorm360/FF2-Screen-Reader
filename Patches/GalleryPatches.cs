using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks gallery scene state.
    /// Mirrors MusicPlayerStateTracker pattern.
    /// </summary>
    public static class GalleryStateTracker
    {
        public static bool IsInGallery { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;
        // Entry title spoken before the focused entry was known: the next SetFocusContent speaks it.
        public static bool PendingEntryRead { get; set; } = false;
        public static int PreviousState { get; set; } = 0;

        public static void ClearState()
        {
            IsInGallery = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            PendingEntryRead = false;
            PreviousState = 0;
            MenuStateRegistry.Reset(MenuStateRegistry.GALLERY);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 1: State transitions — SubSceneManagerExtraGallery.ChangeState
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(SubSceneManagerExtraGallery), nameof(SubSceneManagerExtraGallery.ChangeState))]
    public static class SubSceneManagerExtraGallery_ChangeState_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int state)
        {
            try
            {
                switch (state)
                {
                    case 1: // View
                        if (GalleryStateTracker.PreviousState == 0) // First entry from Init
                        {
                            GalleryStateTracker.IsInGallery = true;
                            GalleryStateTracker.SuppressContentChange = true;
                            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.GALLERY);
                            CoroutineManager.StartManaged(AnnounceGalleryEntry());
                        }
                        GalleryStateTracker.PreviousState = 1;
                        break;

                    case 2: // Details — image opened
                        FFII_ScreenReaderMod.SpeakText(T("Image open"), true);
                        GalleryStateTracker.PreviousState = 2;
                        break;

                    case 3: // GotoTitle — leaving gallery
                        GalleryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in ChangeState patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Entry read, event-driven (CLAUDE.md rule 3; replaces a 2-second poll of CachedFocusedPtr):
        /// one frame after the View state, the title; then the focused entry if its SetFocusContent
        /// already came (cached by the suppressed path), otherwise the next SetFocusContent speaks it
        /// (PendingEntryRead).
        /// </summary>
        private static IEnumerator AnnounceGalleryEntry()
        {
            yield return null;
            if (!GalleryStateTracker.IsInGallery) yield break;
            FFII_ScreenReaderMod.SpeakText(T("Gallery"), true);

            try
            {
                IntPtr focusedPtr = GalleryStateTracker.CachedFocusedPtr;
                if (focusedPtr != IntPtr.Zero && GalleryTopListController_SetFocusContent_Patch.SpeakEntry(focusedPtr, interrupt: false))
                {
                    GalleryStateTracker.SuppressContentChange = false;
                    yield break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error announcing entry item: {ex.Message}");
                GalleryStateTracker.SuppressContentChange = false;
                yield break;
            }

            // Focus not set yet: the entry's SetFocusContent speaks it (and lifts the suppression).
            GalleryStateTracker.PendingEntryRead = true;
        }

    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 2: List navigation — GalleryTopListController.SetFocusContent
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(GalleryTopListController), "SetFocusContent")]
    public static class GalleryTopListController_SetFocusContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GalleryTopListController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!GalleryStateTracker.IsInGallery) return;

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (GalleryStateTracker.SuppressContentChange)
                {
                    GalleryStateTracker.CachedFocusedPtr = ptr;

                    // The entry title is already spoken: this is the entry's focused item.
                    if (GalleryStateTracker.PendingEntryRead)
                    {
                        GalleryStateTracker.PendingEntryRead = false;
                        GalleryStateTracker.SuppressContentChange = false;
                        SpeakEntry(ptr, interrupt: false);
                    }
                    return;
                }

                SpeakEntry(ptr, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in SetFocusContent patch: {ex.Message}");
            }
        }

        /// <summary>Speaks the gallery entry of a list content pointer. Returns true when something was spoken.</summary>
        internal static bool SpeakEntry(IntPtr contentPtr, bool interrupt)
        {
            if (contentPtr == IntPtr.Zero || !GalleryReader.ReadContentFromPointer(contentPtr, out int number, out string name))
                return false;

            string entry = GalleryReader.ReadListEntry(number, name);
            if (string.IsNullOrEmpty(entry))
                return false;
            FFII_ScreenReaderMod.SpeakText(entry, interrupt);
            return true;
        }
    }

}
