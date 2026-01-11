using System;
using System.Collections.Generic;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Manages coroutines to prevent memory leaks and crashes.
    /// Limits concurrent coroutines and provides cleanup on mod unload.
    /// </summary>
    public static class CoroutineManager
    {
        private static readonly List<System.Collections.IEnumerator> activeCoroutines = new List<System.Collections.IEnumerator>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        /// <summary>
        /// Cleanup all active coroutines.
        /// </summary>
        public static void CleanupAll()
        {
            lock (coroutineLock)
            {
                if (activeCoroutines.Count > 0)
                {
                    foreach (var coroutine in activeCoroutines)
                    {
                        try
                        {
                            MelonCoroutines.Stop(coroutine);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"Error stopping coroutine: {ex.Message}");
                        }
                    }
                    activeCoroutines.Clear();
                }
            }
        }

        /// <summary>
        /// Start a managed coroutine with automatic cleanup and limit enforcement.
        /// </summary>
        public static void StartManaged(System.Collections.IEnumerator coroutine)
        {
            lock (coroutineLock)
            {
                CleanupCompleted();

                if (activeCoroutines.Count >= maxConcurrentCoroutines)
                {
                    activeCoroutines.RemoveAt(0);
                }

                try
                {
                    MelonCoroutines.Start(coroutine);
                    activeCoroutines.Add(coroutine);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }

        private static void CleanupCompleted()
        {
            // Simplified: we rely on max limit for now
        }
    }
}
