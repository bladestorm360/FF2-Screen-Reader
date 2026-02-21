using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Manages coroutines to prevent memory leaks and crashes.
    /// Limits concurrent coroutines and provides cleanup on mod unload.
    /// Completed coroutines self-remove via ManagedWrapper.
    /// </summary>
    public static class CoroutineManager
    {
        private static readonly List<IEnumerator> activeCoroutines = new List<IEnumerator>();
        private static readonly Dictionary<IEnumerator, IEnumerator> originalToWrapper = new Dictionary<IEnumerator, IEnumerator>();
        private static readonly Dictionary<IEnumerator, IEnumerator> wrapperToOriginal = new Dictionary<IEnumerator, IEnumerator>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        private class WrapperRef
        {
            public IEnumerator Wrapper;
            public IEnumerator Original;
        }

        /// <summary>
        /// Cleanup all active coroutines.
        /// Should be called when the mod is unloaded.
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
                    originalToWrapper.Clear();
                    wrapperToOriginal.Clear();
                }
            }
        }

        /// <summary>
        /// Start an untracked coroutine (fire-and-forget, no leak tracking).
        /// Use for short one-frame-delay coroutines that complete quickly.
        /// </summary>
        public static void StartUntracked(IEnumerator coroutine)
        {
            try { MelonCoroutines.Start(coroutine); }
            catch (Exception ex) { MelonLogger.Error($"Error starting coroutine: {ex.Message}"); }
        }

        /// <summary>
        /// Start a managed coroutine with automatic cleanup and limit enforcement.
        /// The coroutine is wrapped so it self-removes from tracking on completion.
        /// </summary>
        public static void StartManaged(IEnumerator coroutine)
        {
            lock (coroutineLock)
            {
                if (activeCoroutines.Count >= maxConcurrentCoroutines)
                {
                    var oldest = activeCoroutines[0];
                    activeCoroutines.RemoveAt(0);
                    if (wrapperToOriginal.TryGetValue(oldest, out var original))
                    {
                        originalToWrapper.Remove(original);
                        wrapperToOriginal.Remove(oldest);
                    }
                    try { MelonCoroutines.Stop(oldest); }
                    catch (Exception ex) { MelonLogger.Error($"Error stopping evicted coroutine: {ex.Message}"); }
                }

                var holder = new WrapperRef();
                var wrapper = ManagedWrapper(coroutine, holder);
                holder.Wrapper = wrapper;
                holder.Original = coroutine;

                try
                {
                    MelonCoroutines.Start(wrapper);
                    activeCoroutines.Add(wrapper);
                    originalToWrapper[coroutine] = wrapper;
                    wrapperToOriginal[wrapper] = coroutine;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stops a managed coroutine by its original IEnumerator reference.
        /// This correctly looks up and stops the wrapper that's actually running.
        /// </summary>
        public static void StopManaged(IEnumerator original)
        {
            if (original == null) return;

            lock (coroutineLock)
            {
                if (originalToWrapper.TryGetValue(original, out var wrapper))
                {
                    originalToWrapper.Remove(original);
                    wrapperToOriginal.Remove(wrapper);
                    activeCoroutines.Remove(wrapper);
                    try { MelonCoroutines.Stop(wrapper); }
                    catch (Exception ex) { MelonLogger.Error($"Error stopping managed coroutine: {ex.Message}"); }
                }
            }
        }

        private static IEnumerator ManagedWrapper(IEnumerator inner, WrapperRef holder)
        {
            try
            {
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally
            {
                lock (coroutineLock)
                {
                    if (holder.Wrapper != null)
                    {
                        activeCoroutines.Remove(holder.Wrapper);
                        wrapperToOriginal.Remove(holder.Wrapper);
                    }
                    if (holder.Original != null)
                        originalToWrapper.Remove(holder.Original);
                }
            }
        }
    }
}
