using System.Collections;
using FFII_ScreenReader.Core;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Shared speech helper utilities for all patches.
    /// </summary>
    internal static class SpeechHelper
    {
        /// <summary>
        /// Coroutine that speaks text after one frame delay.
        /// Use with CoroutineManager.StartManaged().
        /// </summary>
        internal static IEnumerator DelayedSpeech(string text)
        {
            yield return null; // Wait one frame
            FFII_ScreenReaderMod.SpeakText(text);
        }

        /// <summary>
        /// Coroutine that speaks text after one frame delay without interrupting.
        /// </summary>
        internal static IEnumerator DelayedSpeechNoInterrupt(string text)
        {
            yield return null; // Wait one frame
            FFII_ScreenReaderMod.SpeakText(text, interrupt: false);
        }
    }
}
