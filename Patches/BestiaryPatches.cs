using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.OutGame.Library;
using Il2CppLast.UI.Common.Library;
using Il2CppLast.Data.Master;
using Il2CppLast.UI.Common;
using LibraryInfoController_KeyInput = Il2CppLast.UI.KeyInput.LibraryInfoController;
using LibraryMenuController_KeyInput = Il2CppLast.UI.KeyInput.LibraryMenuController;
using LibraryMenuListController_KeyInput = Il2CppLast.UI.KeyInput.LibraryMenuListController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks bestiary navigation state within the detail view.
    /// </summary>
    public class BestiaryNavigationTracker
    {
        private static BestiaryNavigationTracker instance = null;
        public static BestiaryNavigationTracker Instance
        {
            get
            {
                if (instance == null)
                    instance = new BestiaryNavigationTracker();
                return instance;
            }
        }

        public bool IsNavigationActive { get; set; }
        public MonsterData CurrentMonsterData { get; set; }
        public LibraryInfoController_KeyInput ActiveController { get; set; }

        private BestiaryNavigationTracker() { Reset(); }

        public void Reset()
        {
            IsNavigationActive = false;
            CurrentMonsterData = null;
            ActiveController = null;
            BestiaryNavigationReader.Reset();
        }

        public bool ValidateState()
        {
            return IsNavigationActive &&
                   CurrentMonsterData != null &&
                   ActiveController != null &&
                   ActiveController.gameObject != null &&
                   ActiveController.gameObject.activeInHierarchy;
        }
    }

    /// <summary>
    /// Tracks the current bestiary scene state.
    /// </summary>
    public static class BestiaryStateTracker
    {
        public static int CurrentState { get; set; } = -1;
        public static bool SuppressNextListEntry { get; set; } = false;
        public static int FullMapIndex { get; set; } = 0;
        public static string CachedEntryName { get; set; } = null;
        public static List<string> CachedHabitatNames { get; set; } = null;
        // SubSceneManagerExtraLibrary.State: Init=0, List=1, Field=2, Dungeon=3, Info=4, ArTop=5, ArBattle=6, GotoTitle=7

        public static bool IsInBestiary => CurrentState >= 1 && CurrentState <= 6;
        public static bool IsInList => CurrentState == 1;
        public static bool IsInDetail => CurrentState == 4;
        public static bool IsInFormation => CurrentState == 5;
        public static bool IsInMap => CurrentState == 2;

        public static void ClearState()
        {
            CurrentState = -1;
            SuppressNextListEntry = false;
            FullMapIndex = 0;
            CachedEntryName = null;
            CachedHabitatNames = null;
            MenuStateRegistry.Reset(
                MenuStateRegistry.BESTIARY_LIST,
                MenuStateRegistry.BESTIARY_DETAIL,
                MenuStateRegistry.BESTIARY_FORMATION,
                MenuStateRegistry.BESTIARY_MAP);
            BestiaryNavigationTracker.Instance.Reset();
            FormationAnnouncer.Cancel();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 1: State transitions — central dispatcher

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(SubSceneManagerExtraLibrary), nameof(SubSceneManagerExtraLibrary.ChangeState))]
    public static class SubSceneManagerExtraLibrary_ChangeState_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SubSceneManagerExtraLibrary __instance, int state)
        {

            try
            {
                int previousState = BestiaryStateTracker.CurrentState;
                BestiaryStateTracker.CurrentState = state;

                // Clear all bestiary menu states first
                MenuStateRegistry.Reset(
                    MenuStateRegistry.BESTIARY_LIST,
                    MenuStateRegistry.BESTIARY_DETAIL,
                    MenuStateRegistry.BESTIARY_FORMATION,
                    MenuStateRegistry.BESTIARY_MAP);

                switch (state)
                {
                    case 1: // List
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_LIST, true);
                        if (previousState <= 0) // Entering bestiary from outside
                        {
                            BestiaryNavigationTracker.Instance.Reset();
                            BestiaryStateTracker.SuppressNextListEntry = true;
                            CoroutineManager.StartManaged(AnnounceListOpen());
                        }
                        else // Returning from detail/map/formation
                        {
                            string reannounce = null;
                            if (previousState == 2 && !string.IsNullOrEmpty(BestiaryStateTracker.CachedEntryName))
                            {
                                // Returning from full map — use cached data (MonsterData is stale)
                                reannounce = string.Format(T("Map closed. {0}"), BestiaryStateTracker.CachedEntryName);
                                BestiaryStateTracker.CachedEntryName = null;
                                BestiaryStateTracker.CachedHabitatNames = null;
                            }
                            else
                            {
                                var data = BestiaryNavigationTracker.Instance.CurrentMonsterData;
                                if (data?.pictureBookData != null)
                                    reannounce = BestiaryReader.ReadListEntry(data.pictureBookData);
                            }
                            BestiaryNavigationTracker.Instance.Reset();
                            if (!string.IsNullOrEmpty(reannounce))
                                FFII_ScreenReaderMod.SpeakText(reannounce, true);
                        }
                        break;

                    case 2: // Field (Map) — opens from list (state 1->2)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_MAP, true);
                        BestiaryStateTracker.FullMapIndex = 0;
                        // Cache data BEFORE coroutine — MonsterData becomes stale after scene transition
                        var mapTracker = BestiaryNavigationTracker.Instance;
                        if (mapTracker.CurrentMonsterData != null)
                        {
                            if (mapTracker.CurrentMonsterData.pictureBookData != null)
                                BestiaryStateTracker.CachedEntryName = BestiaryReader.ReadListEntry(mapTracker.CurrentMonsterData.pictureBookData);
                            // Cache all habitat names as plain strings
                            var habitatList = mapTracker.CurrentMonsterData.HabitatNameList;
                            if (habitatList != null && habitatList.Count > 0)
                            {
                                BestiaryStateTracker.CachedHabitatNames = new List<string>();
                                for (int i = 0; i < habitatList.Count; i++)
                                    BestiaryStateTracker.CachedHabitatNames.Add(habitatList[i] ?? T("Unknown location"));
                            }
                        }
                        CoroutineManager.StartManaged(AnnounceMapView());
                        break;

                    case 4: // Info (Detail)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);
                        // Detail announcement handled by SetData patch
                        break;

                    case 5: // ArTop (Formation)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_FORMATION, true);
                        FormationAnnouncer.Begin();
                        break;

                    case 7: // GotoTitle — leaving bestiary
                        BestiaryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in ChangeState patch: {ex.Message}");
            }
        }

        internal static IEnumerator AnnounceListOpen()
        {
            yield return null;

            try
            {
                var client = PictureBookClient.Instance();
                if (client != null)
                {
                    var list = client.GetPictureBooks();
                    string summary = BestiaryReader.ReadEncounterSummary(list);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        FFII_ScreenReaderMod.SpeakText(summary);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing list summary: {ex.Message}");
            }

            // Extra yields: let list controller finish populating
            yield return null;
            yield return null;

            try
            {
                // Query list controller directly for initial focused entry
                var listController = UnityEngine.Object.FindObjectOfType<LibraryMenuListController_KeyInput>();
                if (listController != null)
                {
                    var data = listController.GetCurrentContent();
                    if (data != null)
                    {
                        BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
                        if (data.pictureBookData != null)
                        {
                            string entry = BestiaryReader.ReadListEntry(data.pictureBookData);
                            if (!string.IsNullOrEmpty(entry))
                                FFII_ScreenReaderMod.SpeakText(entry, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing list open: {ex.Message}");
            }
            finally
            {
                BestiaryStateTracker.SuppressNextListEntry = false;
            }
        }

        private static IEnumerator AnnounceMapView()
        {
            yield return null;
            yield return null;

            try
            {
                // Use cached habitat names — MonsterData is stale after scene transition
                var cached = BestiaryStateTracker.CachedHabitatNames;
                if (cached != null && cached.Count > 0)
                {
                    string announcement = string.Format(T("Map open: {0}"), cached[0]);
                    FFII_ScreenReaderMod.SpeakText(announcement);
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText(T("Map open"), true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing map: {ex.Message}");
            }
        }

        internal static void ReadCurrentFormation(ArBattleTopController controller)
        {
            try
            {
                var partyList = controller.monsterPartyList;
                int partyIndex = controller.selectMonsterPartyIndex;

                if (partyList == null || partyList.Count == 0)
                {
                    FFII_ScreenReaderMod.SpeakText(T("No formations available"), true);
                    return;
                }

                if (partyIndex < 0 || partyIndex >= partyList.Count)
                    partyIndex = 0;

                var party = partyList[partyIndex];
                string announcement = BestiaryReader.ReadFormation(partyIndex, party);

                announcement = MenuPosition.Format(announcement, partyIndex, partyList.Count);

                FFII_ScreenReaderMod.SpeakText(announcement, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error reading formation data: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText(T("Formation view"), true);
            }
        }

        /// <summary>
        /// Called externally to re-read the current formation (e.g., after reorganize).
        /// </summary>
        public static void ReannounceFormation()
        {
            if (!BestiaryStateTracker.IsInFormation) return;

            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<ArBattleTopController>();
                if (controller != null)
                {
                    ReadCurrentFormation(controller);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error re-announcing formation: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 2: List entry navigation — announces selected entry

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(LibraryMenuController_KeyInput), nameof(LibraryMenuController_KeyInput.Show))]
    public static class LibraryMenuController_Show_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LibraryMenuController_KeyInput __instance, MonsterData selectData, bool isInit)
        {

            try
            {
                if (selectData == null) return;

                // Always cache data — Show fires inside ChangeState before our
                // ChangeState postfix sets IsInList, so caching must be ungated
                BestiaryNavigationTracker.Instance.CurrentMonsterData = selectData;

                // Only announce when state has been set and not suppressed
                if (!BestiaryStateTracker.IsInList) return;
                if (BestiaryStateTracker.SuppressNextListEntry) return;

                var pbData = selectData.pictureBookData;
                if (pbData == null) return;

                string entry = BestiaryReader.ReadListEntry(pbData);
                if (!string.IsNullOrEmpty(entry))
                {
                    FFII_ScreenReaderMod.SpeakText(entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in list Show patch: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 2b: List cursor movement — announces entry on every cursor change

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(LibraryMenuController_KeyInput), "OnContentSelected")]
    public static class LibraryMenuController_OnContentSelected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int index, MonsterData monsterData)
        {

            try
            {
                if (monsterData == null) return;
                if (!BestiaryStateTracker.IsInList) return;

                BestiaryNavigationTracker.Instance.CurrentMonsterData = monsterData;

                // Suppress speech during initial entry — AnnounceListOpen handles it
                if (BestiaryStateTracker.SuppressNextListEntry) return;

                var pbData = monsterData.pictureBookData;
                if (pbData == null) return;

                string entry = BestiaryReader.ReadListEntry(pbData);
                if (!string.IsNullOrEmpty(entry))
                {
                    FFII_ScreenReaderMod.SpeakText(entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in list OnContentSelected patch: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 3: Detail view — build stat buffer and announce monster name

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(LibraryInfoController_KeyInput), nameof(LibraryInfoController_KeyInput.SetData))]
    public static class LibraryInfoController_SetData_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LibraryInfoController_KeyInput __instance, MonsterData data)
        {

            try
            {
                if (__instance == null || data == null) return;

                // Cache the controller
                GameObjectCache.Register(__instance);

                var tracker = BestiaryNavigationTracker.Instance;
                tracker.CurrentMonsterData = data;
                tracker.ActiveController = __instance;

                CoroutineManager.StartManaged(DelayedDetailAnnouncement(__instance, data));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in SetData patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedDetailAnnouncement(LibraryInfoController_KeyInput controller, MonsterData data)
        {
            // Wait for UI to update
            yield return null;
            yield return null;

            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                // Sole detail announcer (FF1 parity): the "Name: {monster}" buffer entry is read by
                // BuildAndInitializeStatBuffer. SetData also fires for monster switches and page flips
                // in the detail view, so those hooks only refresh CurrentMonsterData.
                BuildAndInitializeStatBuffer();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in delayed detail announcement: {ex.Message}");
            }
        }

        /// <summary>
        /// Find LibraryInfoContent and build the stat buffer.
        /// </summary>
        internal static void BuildAndInitializeStatBuffer()
        {
            try
            {
                var content = UnityEngine.Object.FindObjectOfType<LibraryInfoContent>();
                if (content == null)
                {
                    MelonLogger.Warning("[Bestiary] LibraryInfoContent not found");
                    return;
                }

                var tracker = BestiaryNavigationTracker.Instance;
                var entries = BestiaryReader.BuildStatBuffer(content, tracker.CurrentMonsterData);

                // The monster name is the top navigable entry — auto-read below as the single
                // "you're now viewing this monster" announcement.
                var pbData = tracker.CurrentMonsterData?.pictureBookData;
                string name = pbData != null && pbData.IsRelease ? pbData.MonsterName : T("Unknown");
                entries.Insert(0, new BestiaryStatEntry(T("Name"), name, BestiaryStatGroup.MonsterData));

                BestiaryNavigationReader.Initialize(entries);

                tracker.IsNavigationActive = entries.Count > 0;

                if (entries.Count > 0)
                    FFII_ScreenReaderMod.SpeakText(entries[0].ToString(), false);

                MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error building stat buffer: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 5: Monster switching in detail view (previous/next monster)

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Il2CppLast.Scene.ExtraLibraryInfo), "OnChangedMonster")]
    public static class ExtraLibraryInfo_OnChangedMonster_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MonsterData data)
        {

            try
            {
                if (data == null || !BestiaryStateTracker.IsInDetail) return;

                // Keep CurrentMonsterData fresh; the announce + buffer rebuild is driven solely by
                // LibraryInfoController.SetData (fires on this monster change too).
                BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in OnChangedMonster patch: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 6: Minimap enlarge / close in the list view
    // LibraryMenuController.ChangeState(State) is inlined (no direct callers). Its two key-help setups
    // mark the transitions: SetupEnlargedMapKeyHelp (unique RVA 0x972270; the inlined ChangeState
    // (EnlargedMap) in the list-input lambda <UpdateMonsterList>b__16_0 and the map-click lambda) and
    // SetupKeyHelp (unique RVA 0x9725E0; the inlined ChangeState(MonsterList) in <UpdateEnlargedMap>
    // b__17_0 and Show, plus InitSetup and OnContentSelected). Both run BEFORE the caller stores the new
    // selectState (@0x44), so the postfix still reads the old state: MonsterList → EnlargedMap = opened,
    // EnlargedMap → MonsterList = closed. Replaces the per-frame UpdateController postfix (rule 3).
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(LibraryMenuController_KeyInput), "SetupEnlargedMapKeyHelp")]
    public static class LibraryMenuController_SetupEnlargedMapKeyHelp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LibraryMenuController_KeyInput __instance)
            => MinimapToggleAnnouncer.OnKeyHelpSetup(__instance, enlarged: true);
    }

    [HarmonyPatch(typeof(LibraryMenuController_KeyInput), "SetupKeyHelp")]
    public static class LibraryMenuController_SetupKeyHelp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LibraryMenuController_KeyInput __instance)
            => MinimapToggleAnnouncer.OnKeyHelpSetup(__instance, enlarged: false);
    }

    internal static class MinimapToggleAnnouncer
    {
        // LibraryMenuController.selectState (private; State: MonsterList = 0, EnlargedMap = 1)
        private const int OFFSET_SELECT_STATE = 0x44;
        private const int STATE_ENLARGED_MAP = 1;

        internal static unsafe void OnKeyHelpSetup(LibraryMenuController_KeyInput instance, bool enlarged)
        {
            try
            {
                if (instance == null || !BestiaryStateTracker.IsInList) return;
                IntPtr instancePtr = instance.Pointer;
                if (instancePtr == IntPtr.Zero) return;
                int stateBefore = *(int*)((byte*)instancePtr.ToPointer() + OFFSET_SELECT_STATE);

                if (enlarged && stateBefore != STATE_ENLARGED_MAP)
                {
                    var tracker = BestiaryNavigationTracker.Instance;
                    // Cache entry name while MonsterData is still alive
                    if (tracker.CurrentMonsterData?.pictureBookData != null)
                        BestiaryStateTracker.CachedEntryName = BestiaryReader.ReadListEntry(tracker.CurrentMonsterData.pictureBookData);

                    string mapInfo = T("Minimap open");
                    if (tracker.CurrentMonsterData != null)
                    {
                        string mapName = BestiaryReader.ReadMapName(tracker.CurrentMonsterData, 0);
                        if (!string.IsNullOrEmpty(mapName))
                            mapInfo = string.Format(T("Minimap open: {0}"), mapName);
                    }
                    FFII_ScreenReaderMod.SpeakText(mapInfo, true);
                }
                else if (!enlarged && stateBefore == STATE_ENLARGED_MAP)
                {
                    string closeMsg = T("Minimap closed");
                    if (!string.IsNullOrEmpty(BestiaryStateTracker.CachedEntryName))
                        closeMsg += $". {BestiaryStateTracker.CachedEntryName}";
                    BestiaryStateTracker.CachedEntryName = null;
                    FFII_ScreenReaderMod.SpeakText(closeMsg, true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in minimap key-help patch: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Formation view (ArTop) open read. Event-driven (rule 3; replaces a 3-second FindObjectOfType
    // poll): ArBattleTopController.InitMonsterPartyList (private, unique RVA 0x52FF60; caller
    // ArBattleTopController.SetActive) fills monsterPartyList. The ArTop ChangeState arms the read and
    // tries once a frame later (the list may already be filled); otherwise InitMonsterPartyList's
    // postfix reads it a frame after it runs.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class FormationAnnouncer
    {
        private static bool _pending;
        private static int _gen;

        internal static void Begin()
        {
            _pending = true;
            CoroutineManager.StartManaged(ReadAfterFrame(null, ++_gen, listIsFinal: false));
        }

        internal static void OnPartyListReady(ArBattleTopController controller)
        {
            if (_pending && controller != null)
                CoroutineManager.StartManaged(ReadAfterFrame(controller, ++_gen, listIsFinal: true));
        }

        internal static void Cancel()
        {
            _pending = false;
            _gen++;
        }

        // yield stays outside the try (yield-in-try-with-catch is illegal).
        private static IEnumerator ReadAfterFrame(ArBattleTopController controller, int gen, bool listIsFinal)
        {
            yield return null;
            if (!_pending || gen != _gen) yield break;
            try
            {
                if (!BestiaryStateTracker.IsInFormation)
                {
                    _pending = false;
                    yield break;
                }
                if (controller == null)
                    controller = UnityEngine.Object.FindObjectOfType<ArBattleTopController>();
                if (controller == null)
                    yield break;   // InitMonsterPartyList reads it when it runs
                var partyList = controller.monsterPartyList;
                if (!listIsFinal && (partyList == null || partyList.Count == 0))
                    yield break;   // not filled yet: InitMonsterPartyList reads it
                _pending = false;
                SubSceneManagerExtraLibrary_ChangeState_Patch.ReadCurrentFormation(controller);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing formation: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ArBattleTopController), "InitMonsterPartyList")]
    public static class ArBattleTopController_InitMonsterPartyList_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArBattleTopController __instance)
            => FormationAnnouncer.OnPartyListReady(__instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 7: Formation rearrange — announce new formation after Q key

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(ArBattleTopController), nameof(ArBattleTopController.ChangeMonsterParty))]
    public static class ArBattleTopController_ChangeMonsterParty_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {

            if (!BestiaryStateTracker.IsInFormation) return;
            CoroutineManager.StartManaged(DelayedReannounce());
        }

        private static IEnumerator DelayedReannounce()
        {
            yield return null;
            SubSceneManagerExtraLibrary_ChangeState_Patch.ReannounceFormation();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 8: Full map cycling — NextMap/PreviousMap in map view (state 2)

    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Il2CppLast.Scene.ExtraLibraryField), "NextMap")]
    public static class ExtraLibraryField_NextMap_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {

            if (!BestiaryStateTracker.IsInMap) return;
            CoroutineManager.StartManaged(FullMapCycleHelper.AnnounceFullMapCycle(1));
        }
    }

    [HarmonyPatch(typeof(Il2CppLast.Scene.ExtraLibraryField), "PreviousMap")]
    public static class ExtraLibraryField_PreviousMap_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {

            if (!BestiaryStateTracker.IsInMap) return;
            CoroutineManager.StartManaged(FullMapCycleHelper.AnnounceFullMapCycle(-1));
        }
    }

    internal static class FullMapCycleHelper
    {
        internal static IEnumerator AnnounceFullMapCycle(int direction)
        {
            yield return null;

            try
            {
                // Use cached habitat names — MonsterData is stale after scene transition
                var cached = BestiaryStateTracker.CachedHabitatNames;
                if (cached == null || cached.Count == 0) yield break;

                int count = cached.Count;
                BestiaryStateTracker.FullMapIndex += direction;
                if (BestiaryStateTracker.FullMapIndex >= count)
                    BestiaryStateTracker.FullMapIndex = 0;
                else if (BestiaryStateTracker.FullMapIndex < 0)
                    BestiaryStateTracker.FullMapIndex = count - 1;

                string mapName = (BestiaryStateTracker.FullMapIndex < cached.Count)
                    ? cached[BestiaryStateTracker.FullMapIndex]
                    : null;
                if (!string.IsNullOrEmpty(mapName))
                    FFII_ScreenReaderMod.SpeakText(mapName, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing map cycle: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Config menu bestiary state handler
    // Maps SubSceneManagerMainGame states 18/19 to BestiaryStateTracker states
    // so all existing bestiary patches (list nav, detail view) work.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class ConfigBestiaryStateHandler
    {
        // SubSceneManagerMainGame.State (dump.cs:363931): FieldHelp=17, MenuLibraryUi=18 (list),
        // MenuLibraryInfo=19 (detail). Game-specific — FF1 uses 19/20.
        internal const int STATE_MENU_LIBRARY_UI = 18;
        internal const int STATE_MENU_LIBRARY_INFO = 19;

        private static int _previousState = -1;

        /// <summary>
        /// True while we're in the config menu bestiary (states 18 or 19).
        /// Used by GameStatePatches to detect exit.
        /// </summary>
        public static bool WasInConfigBestiary { get; private set; } = false;

        public static void HandleStateChange(int mainGameState)
        {
            try
            {
                int previousBestiaryState = BestiaryStateTracker.CurrentState;

                // Entering from the config menu: forget the config row that opened the bestiary so it
                // re-speaks when GameStatePatches arms the config re-announce on exit.
                if (!WasInConfigBestiary)
                    ConfigMenuState.ClearDedup();

                if (mainGameState == STATE_MENU_LIBRARY_UI) // list
                {
                    WasInConfigBestiary = true;
                    BestiaryStateTracker.CurrentState = 1; // Map to extras List state

                    // Clear and set menu state
                    MenuStateRegistry.Reset(
                        MenuStateRegistry.BESTIARY_LIST,
                        MenuStateRegistry.BESTIARY_DETAIL);
                    MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_LIST, true);

                    if (previousBestiaryState <= 0) // Entering from outside
                    {
                        BestiaryNavigationTracker.Instance.Reset();
                        BestiaryStateTracker.SuppressNextListEntry = true;
                        CoroutineManager.StartManaged(
                            SubSceneManagerExtraLibrary_ChangeState_Patch.AnnounceListOpen());
                    }
                    else if (previousBestiaryState == 4) // Returning from detail
                    {
                        BestiaryNavigationTracker.Instance.Reset();

                        // Re-announce current entry
                        var listController = UnityEngine.Object.FindObjectOfType<LibraryMenuListController_KeyInput>();
                        if (listController != null)
                        {
                            var data = listController.GetCurrentContent();
                            if (data != null)
                            {
                                BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
                                if (data.pictureBookData != null)
                                {
                                    string entry = BestiaryReader.ReadListEntry(data.pictureBookData);
                                    if (!string.IsNullOrEmpty(entry))
                                        FFII_ScreenReaderMod.SpeakText(entry, true);
                                }
                            }
                        }
                    }
                }
                else if (mainGameState == STATE_MENU_LIBRARY_INFO) // detail
                {
                    WasInConfigBestiary = true;
                    BestiaryStateTracker.CurrentState = 4; // Map to extras Info state

                    MenuStateRegistry.Reset(
                        MenuStateRegistry.BESTIARY_LIST,
                        MenuStateRegistry.BESTIARY_DETAIL);
                    MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);
                    // Detail announcement handled by existing SetData patch
                }

                _previousState = mainGameState;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in ConfigBestiaryStateHandler: {ex.Message}");
            }
        }

        public static void HandleExit()
        {
            try
            {
                WasInConfigBestiary = false;
                _previousState = -1;
                BestiaryStateTracker.ClearState();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in ConfigBestiaryStateHandler exit: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Patch 9: Monster switching in config menu detail view
    // MenuExtraLibraryInfo.OnChangedMonster has a different RVA than
    // ExtraLibraryInfo.OnChangedMonster, so needs its own patch.
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Il2CppLast.Scene.MenuExtraLibraryInfo), "OnChangedMonster", new Type[] { typeof(MonsterData) })]
    public static class MenuExtraLibraryInfo_OnChangedMonster_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MonsterData data)
        {
            try
            {
                if (data == null || !BestiaryStateTracker.IsInDetail) return;

                // Keep CurrentMonsterData fresh; the announce + buffer rebuild is driven solely by
                // LibraryInfoController.SetData (fires for the config path too).
                BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in config OnChangedMonster patch: {ex.Message}");
            }
        }
    }

}
