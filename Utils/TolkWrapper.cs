using System;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Wrapper for Tolk screen reader integration.
    /// Handles initialization, speaking text, and cleanup.
    /// Thread-safe with locking to prevent concurrent native calls.
    /// </summary>
    public class TolkWrapper
    {
        private readonly Tolk.Tolk tolk = new Tolk.Tolk();
        private readonly object tolkLock = new object();

        /// <summary>
        /// Loads Tolk and initializes screen reader support.
        /// </summary>
        public void Load()
        {
            try
            {
                tolk.Load();
                if (!tolk.IsLoaded())
                {
                    MelonLogger.Warning("No screen reader detected");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to initialize screen reader support: {ex.Message}");
            }
        }

        /// <summary>
        /// Unloads Tolk and frees resources.
        /// </summary>
        public void Unload()
        {
            try
            {
                tolk.Unload();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error unloading screen reader: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks text through the screen reader.
        /// Thread-safe: uses locking to prevent concurrent native calls.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        /// <param name="interrupt">Whether to interrupt current speech (true for user actions, false for game events).</param>
        public void Speak(string text, bool interrupt = true)
        {
            try
            {
                if (tolk.IsLoaded() && !string.IsNullOrEmpty(text))
                {
                    lock (tolkLock)
                    {
                        tolk.Output(text, interrupt);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error speaking text: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if Tolk is loaded and a screen reader is available.
        /// </summary>
        public bool IsLoaded() => tolk.IsLoaded();
    }
}
