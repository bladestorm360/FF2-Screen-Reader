namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Reduces boilerplate in state classes by encapsulating the standard pattern:
    /// IsActive property, ShouldAnnounce deduplication, and reset handler registration.
    ///
    /// Usage: Each state class creates a static MenuStateHelper instance and delegates to it.
    /// Custom logic (ShouldSuppress, extra fields, helper methods) stays in the state class.
    /// </summary>
    internal class MenuStateHelper
    {
        private readonly string _registryKey;
        private readonly string[] _dedupContexts;

        /// <summary>
        /// Creates a helper and registers a reset handler that clears the dedup contexts.
        /// </summary>
        /// <param name="registryKey">The MenuStateRegistry key (e.g., MenuStateRegistry.ITEM_MENU)</param>
        /// <param name="dedupContexts">Deduplication context strings to reset when state is cleared</param>
        public MenuStateHelper(string registryKey, params string[] dedupContexts)
        {
            _registryKey = registryKey;
            _dedupContexts = dedupContexts;
        }

        /// <summary>
        /// Registers the reset handler with MenuStateRegistry.
        /// Call this in the state class's static constructor AFTER setting up any extra reset logic.
        /// Pass an optional extra action for custom cleanup (clearing fields, etc.).
        /// </summary>
        public void RegisterResetHandler(System.Action extraCleanup = null)
        {
            MenuStateRegistry.RegisterResetHandler(_registryKey, () =>
            {
                if (_dedupContexts.Length > 0)
                    AnnouncementDeduplicator.Reset(_dedupContexts);
                extraCleanup?.Invoke();
            });
        }

        /// <summary>
        /// Gets or sets the active state via MenuStateRegistry.
        /// </summary>
        public bool IsActive
        {
            get => MenuStateRegistry.IsActive(_registryKey);
            set => MenuStateRegistry.SetActive(_registryKey, value);
        }

        /// <summary>
        /// Sets this menu as the exclusive active menu, clearing all others.
        /// </summary>
        public void SetActiveExclusive()
        {
            MenuStateRegistry.SetActiveExclusive(_registryKey);
        }

        /// <summary>
        /// Checks if a string announcement should be spoken (different from last).
        /// Uses the first dedup context. For multiple contexts, call AnnouncementDeduplicator directly.
        /// </summary>
        public bool ShouldAnnounce(string text)
        {
            return _dedupContexts.Length > 0
                && AnnouncementDeduplicator.ShouldAnnounce(_dedupContexts[0], text);
        }

        /// <summary>
        /// Checks if an index-based announcement should be spoken.
        /// Uses the specified context index (0-based into the dedupContexts array).
        /// </summary>
        public bool ShouldAnnounce(int contextIndex, int value)
        {
            return contextIndex < _dedupContexts.Length
                && AnnouncementDeduplicator.ShouldAnnounce(_dedupContexts[contextIndex], value);
        }

        /// <summary>
        /// The registry key for this menu state.
        /// </summary>
        public string RegistryKey => _registryKey;
    }
}
