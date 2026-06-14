namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Reduces boilerplate in state classes: the IsActive property (backed by
    /// MenuStateRegistry) and reset-handler registration.
    ///
    /// Deduplication was removed mod-wide — menus announce directly on event-driven hooks,
    /// with a small local value/index guard only where a hooked method genuinely fires
    /// repeatedly (sliders, popup focus loops, etc.). Those guards live in their own patch
    /// classes, not here.
    /// </summary>
    internal class MenuStateHelper
    {
        private readonly string _registryKey;

        public MenuStateHelper(string registryKey)
        {
            _registryKey = registryKey;
        }

        /// <summary>
        /// Registers a reset handler with MenuStateRegistry, invoked when the state is cleared.
        /// Call this in the state class's static constructor; pass an optional cleanup action.
        /// </summary>
        public void RegisterResetHandler(System.Action extraCleanup = null)
        {
            MenuStateRegistry.RegisterResetHandler(_registryKey, () =>
            {
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
        /// The registry key for this menu state.
        /// </summary>
        public string RegistryKey => _registryKey;
    }
}
