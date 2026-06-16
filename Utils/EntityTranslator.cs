using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Management;
using FFII_ScreenReader.Field;
using static FFII_ScreenReader.Utils.ModTextTranslator;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to the current game language using an embedded
    /// translation.json resource. Falls back to English when a language-specific
    /// translation is missing; returns the original name when no entry exists at all.
    /// Handles prefixes (e.g., "SC01:", "Sc E 0025:", "6:") and suffixes (trailing ASCII digits).
    /// Circled numbers (①②③) are preserved as part of the key.
    /// </summary>
    public static class EntityTranslator
    {
        // Nested translations: JapaneseKey -> { langCode -> localizedValue }
        private static Dictionary<string, Dictionary<string, string>> translations;
        private static bool isInitialized = false;

        private static string cachedLanguageCode = "en";
        private static bool hasLoggedLanguage = false;

        private static readonly Dictionary<int, string> LanguageCodeMap = new()
        {
            {1,"ja"},{2,"en"},{3,"fr"},{4,"it"},{5,"de"},{6,"es"},
            {7,"ko"},{8,"zht"},{9,"zhc"},{10,"ru"},{11,"th"},{12,"pt"}
        };

        // Matches numeric prefix (e.g., "6:") or SC prefix (e.g., "SC01:", "Sc E 0025:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC\s*E?\s*)?\d+:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches trailing ASCII or full-width digits (not circled numbers like ①②③).
        // Full-width (U+FF10–FF19) is normalized to ASCII in StripTrailingDigits.
        private static readonly Regex TrailingDigitsRegex = new Regex(
            @"([0-9０-９]+)$",
            RegexOptions.Compiled);

        // Matches a single leading circled number ①-⑨ (U+2460..U+2468). The game uses
        // these as per-instance disambiguators for duplicate NPC sprites; the entity
        // scanner output already includes positional info ("- NPC, 1 of 7") so the
        // circled number is dropped after stripping.
        private static readonly Regex CircledNumberPrefixRegex = new Regex(
            @"^[①-⑨]",
            RegexOptions.Compiled);

        /// <summary>
        /// Detects the current game language via MessageManager and returns a language code.
        /// </summary>
        public static string DetectLanguage()
        {
            try
            {
                var mgr = MessageManager.Instance;
                if (mgr != null)
                {
                    int langId = (int)mgr.currentLanguage;
                    if (LanguageCodeMap.TryGetValue(langId, out string code))
                    {
                        cachedLanguageCode = code;
                        if (!hasLoggedLanguage)
                        {
                            MelonLogger.Msg($"[EntityTranslator] Detected language: {cachedLanguageCode}");
                            hasLoggedLanguage = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedLanguage)
                    MelonLogger.Msg($"[EntityTranslator] DetectLanguage exception: {ex.Message}");
            }
            return cachedLanguageCode;
        }

        /// <summary>
        /// Loads translation.json from the embedded resource.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            translations = new Dictionary<string, Dictionary<string, string>>();

            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("translation.json");

                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string json = reader.ReadToEnd();

                    translations = ParseNestedJson(json);
                    MelonLogger.Msg($"[EntityTranslator] Loaded {translations.Count} entity translation entries");
                }
                else
                {
                    MelonLogger.Warning("[EntityTranslator] Embedded translation.json not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityTranslator] Error loading translations: {ex.Message}");
            }

            isInitialized = true;
        }

        /// <summary>
        /// Translates a Japanese entity name to the current game language.
        /// Returns original name if no translation found.
        /// Handles prefixes and trailing ASCII and full-width number suffixes.
        /// Skips translation entirely when the game language is Japanese.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            if (!isInitialized)
                Initialize();

            // Skip translation when game is set to Japanese
            string lang = DetectLanguage();
            if (lang == "ja")
                return japaneseName;

            // 1. Exact match first (handles entries with circled numbers like ①②③)
            string localized = LookupLocalized(japaneseName, lang);
            if (localized != null)
                return localized;

            // 2. Strip numeric/SC prefix and try lookup
            StripPrefix(japaneseName, out string prefix, out string afterPrefix);
            if (prefix != null)
            {
                string prefixTranslation = LookupLocalized(afterPrefix, lang);
                if (prefixTranslation != null)
                    return prefix + " " + prefixTranslation;
            }

            // 3. Strip trailing ASCII digits and try lookup (e.g., "ヒルダ2" -> "Hilda 2")
            StripTrailingDigits(japaneseName, out string baseName, out string suffix);
            if (suffix != null)
            {
                string suffixTranslation = LookupLocalized(baseName, lang);
                if (suffixTranslation != null)
                    return suffixTranslation + " " + suffix;
            }

            // 4. Try both prefix AND suffix stripping
            if (prefix != null)
            {
                StripTrailingDigits(afterPrefix, out string baseAfterPrefix, out string suffixAfterPrefix);
                if (suffixAfterPrefix != null)
                {
                    string bothTranslation = LookupLocalized(baseAfterPrefix, lang);
                    if (bothTranslation != null)
                        return prefix + " " + bothTranslation + " " + suffixAfterPrefix;
                }
            }

            // 5. Strip leading circled-number prefix (①-⑨) and look up the remainder.
            //    Drop the circled number — sequence info is in the announcement format.
            if (CircledNumberPrefixRegex.IsMatch(japaneseName))
            {
                string afterCircled = japaneseName.Substring(1);
                string circledTranslation = LookupLocalized(afterCircled, lang);
                if (circledTranslation != null)
                    return circledTranslation;

                // Combine with trailing-digit stripping
                StripTrailingDigits(afterCircled, out string baseAfterCircled, out string suffixAfterCircled);
                if (suffixAfterCircled != null)
                {
                    string both = LookupLocalized(baseAfterCircled, lang);
                    if (both != null)
                        return both + " " + suffixAfterCircled;
                }
            }

            // 6. Strip leading 真 ("true/real" — late-game variants like 真ヒルダ).
            //    Mark with "(true)" so the late-game distinction survives translation.
            if (japaneseName.Length > 1 && japaneseName[0] == '真')
            {
                string afterShin = japaneseName.Substring(1);
                string shinTranslation = LookupLocalized(afterShin, lang);
                if (shinTranslation != null)
                    return string.Format(T("{0} (true)"), shinTranslation);

                // Combine with trailing-digit stripping
                StripTrailingDigits(afterShin, out string baseAfterShin, out string suffixAfterShin);
                if (suffixAfterShin != null)
                {
                    string both = LookupLocalized(baseAfterShin, lang);
                    if (both != null)
                        return string.Format(T("{0} (true)"), both) + " " + suffixAfterShin;
                }
            }

            // Return original if no translation
            return japaneseName;
        }

        /// <summary>
        /// Looks up a Japanese key and returns the localized string for the given language.
        /// Falls back to English if the target language entry is missing.
        /// Returns null if no entry exists at all.
        /// </summary>
        private static string LookupLocalized(string japaneseKey, string lang)
        {
            if (translations == null || !translations.TryGetValue(japaneseKey, out var langDict))
                return null;

            // Try target language first
            if (langDict.TryGetValue(lang, out string localized) && !string.IsNullOrEmpty(localized))
                return localized;

            // Fall back to English
            if (lang != "en" && langDict.TryGetValue("en", out string english) && !string.IsNullOrEmpty(english))
                return english;

            // Return null to indicate no usable translation
            return null;
        }

        /// <summary>
        /// Checks if a string contains Japanese characters (hiragana, katakana, or kanji).
        /// </summary>
        private static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                // Hiragana: U+3040 - U+309F
                // Katakana: U+30A0 - U+30FF
                // Kanji: U+4E00 - U+9FFF (common CJK)
                if ((c >= '\u3040' && c <= '\u309F') ||  // Hiragana
                    (c >= '\u30A0' && c <= '\u30FF') ||  // Katakana
                    (c >= '\u4E00' && c <= '\u9FFF'))    // Common Kanji
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Strips a numeric or SC prefix from an entity name.
        /// Returns the prefix (e.g., "6:" or "SC01:" or "Sc E 0025:") and the remaining name.
        /// If no prefix is found, prefix will be null and baseName will equal the input.
        /// </summary>
        private static void StripPrefix(string name, out string prefix, out string baseName)
        {
            Match match = EntityPrefixRegex.Match(name);
            if (match.Success)
            {
                prefix = match.Groups[1].Value;
                baseName = name.Substring(prefix.Length);
            }
            else
            {
                prefix = null;
                baseName = name;
            }
        }

        /// <summary>
        /// Strips trailing ASCII digits from an entity name.
        /// Circled numbers (①②③) are NOT stripped - only ASCII 0-9.
        /// Returns the base name and the stripped suffix.
        /// If no suffix found, suffix will be null and baseName will equal the input.
        /// </summary>
        private static void StripTrailingDigits(string name, out string baseName, out string suffix)
        {
            Match match = TrailingDigitsRegex.Match(name);
            if (match.Success)
            {
                string raw = match.Groups[1].Value;
                baseName = name.Substring(0, name.Length - raw.Length);
                // Normalize full-width digits (U+FF10–FF19) to ASCII so "柵３" reads "Fence 3".
                var norm = new StringBuilder(raw.Length);
                foreach (char ch in raw)
                    norm.Append(ch >= '０' && ch <= '９' ? (char)('0' + (ch - '０')) : ch);
                suffix = norm.ToString();
            }
            else
            {
                suffix = null;
                baseName = name;
            }
        }

        // ─────────────────────────────────────────────
        //  JSON parsing for nested { key: { lang: value } } format
        //  (copied from ModTextTranslator pattern)
        // ─────────────────────────────────────────────

        internal static Dictionary<string, Dictionary<string, string>> ParseNestedJson(string json)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return result;

            string inner = json.Substring(1, json.Length - 2);

            int pos = 0;
            while (pos < inner.Length)
            {
                int keyStart = inner.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = FindClosingQuote(inner, keyStart + 1);
                if (keyEnd < 0) break;

                string entryKey = UnescapeJsonString(inner.Substring(keyStart + 1, keyEnd - keyStart - 1));

                int braceStart = inner.IndexOf('{', keyEnd);
                if (braceStart < 0) break;

                int braceEnd = FindMatchingBrace(inner, braceStart);
                if (braceEnd < 0) break;

                string innerJson = inner.Substring(braceStart + 1, braceEnd - braceStart - 1);
                result[entryKey] = ParseStringDictionary(innerJson);

                pos = braceEnd + 1;
            }

            return result;
        }

        private static Dictionary<string, string> ParseStringDictionary(string json)
        {
            var dict = new Dictionary<string, string>();
            int pos = 0;
            while (pos < json.Length)
            {
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = FindClosingQuote(json, keyStart + 1);
                if (keyEnd < 0) break;

                string key = UnescapeJsonString(json.Substring(keyStart + 1, keyEnd - keyStart - 1));

                int colonIdx = json.IndexOf(':', keyEnd);
                if (colonIdx < 0) break;

                int valStart = json.IndexOf('"', colonIdx);
                if (valStart < 0) break;
                int valEnd = FindClosingQuote(json, valStart + 1);
                if (valEnd < 0) break;

                string value = UnescapeJsonString(json.Substring(valStart + 1, valEnd - valStart - 1));
                dict[key] = value;

                pos = valEnd + 1;
            }
            return dict;
        }

        private static int FindClosingQuote(string s, int startAfterOpenQuote)
        {
            for (int i = startAfterOpenQuote; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '"') return i;
            }
            return -1;
        }

        private static int FindMatchingBrace(string s, int openBracePos)
        {
            int depth = 1;
            bool inString = false;
            for (int i = openBracePos + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string UnescapeJsonString(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        case '/': sb.Append('/'); i++; break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations?.Count ?? 0;
    }
}
