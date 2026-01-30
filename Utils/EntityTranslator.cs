using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Field;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to English using a JSON dictionary.
    /// Loaded from UserData/FFII_ScreenReader/FF2_translations.json
    /// </summary>
    public static class EntityTranslator
    {
        private static Dictionary<string, string> translations = new Dictionary<string, string>();
        private static bool isInitialized = false;
        private static string translationsPath;

        // Track untranslated names by map for dumping
        private static Dictionary<string, HashSet<string>> untranslatedNamesByMap = new Dictionary<string, HashSet<string>>();

        // Matches numeric prefix (e.g., "6:") or SC prefix (e.g., "SC01:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC)?\d+:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Initializes the translator by loading translations from JSON file.
        /// Creates empty file if not exists.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            try
            {
                // Build path: UserData/FFII_ScreenReader/translations.json
                // Get game root directory (parent of _Data folder)
                string gameDataPath = Application.dataPath; // e.g., .../FINAL FANTASY/FINAL FANTASY_Data
                string gameRoot = Path.GetDirectoryName(gameDataPath);
                string userDataPath = Path.Combine(gameRoot, "UserData", "FFII_ScreenReader");
                translationsPath = Path.Combine(userDataPath, "FF2_translations.json");

                // Create directory if needed
                if (!Directory.Exists(userDataPath))
                {
                    Directory.CreateDirectory(userDataPath);
                    MelonLogger.Msg($"[EntityTranslator] Created directory: {userDataPath}");
                }

                // Load or create translations file
                if (File.Exists(translationsPath))
                {
                    LoadTranslations();
                }
                else
                {
                    // Create empty translations file
                    File.WriteAllText(translationsPath, "{\n}");
                    MelonLogger.Msg($"[EntityTranslator] Created empty translations file: {translationsPath}");
                }

                isInitialized = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to initialize: {ex.Message}");
                isInitialized = true; // Prevent repeated init attempts
            }
        }

        /// <summary>
        /// Loads translations from the JSON file.
        /// </summary>
        private static void LoadTranslations()
        {
            try
            {
                string json = File.ReadAllText(translationsPath);
                translations = ParseJsonDictionary(json);
                MelonLogger.Msg($"[EntityTranslator] Loaded {translations.Count} translations from {translationsPath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityTranslator] Failed to load translations: {ex.Message}");
                translations = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Simple JSON dictionary parser (no external dependencies).
        /// Parses {"key": "value", ...} format.
        /// </summary>
        private static Dictionary<string, string> ParseJsonDictionary(string json)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(json))
                return result;

            // Remove outer braces and whitespace
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            json = json.Trim();

            if (string.IsNullOrEmpty(json))
                return result;

            // Parse key-value pairs
            int pos = 0;
            while (pos < json.Length)
            {
                // Find opening quote for key
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;

                // Find closing quote for key
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find colon
                int colonPos = json.IndexOf(':', keyEnd);
                if (colonPos < 0) break;

                // Find opening quote for value
                int valueStart = json.IndexOf('"', colonPos);
                if (valueStart < 0) break;

                // Find closing quote for value (handle escaped quotes)
                int valueEnd = valueStart + 1;
                while (valueEnd < json.Length)
                {
                    valueEnd = json.IndexOf('"', valueEnd);
                    if (valueEnd < 0) break;

                    // Check if escaped
                    int backslashes = 0;
                    int checkPos = valueEnd - 1;
                    while (checkPos >= valueStart && json[checkPos] == '\\')
                    {
                        backslashes++;
                        checkPos--;
                    }

                    if (backslashes % 2 == 0)
                        break; // Not escaped

                    valueEnd++;
                }

                if (valueEnd < 0) break;

                string value = json.Substring(valueStart + 1, valueEnd - valueStart - 1);

                // Unescape basic sequences
                value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");

                result[key] = value;

                // Move to next pair
                pos = valueEnd + 1;
            }

            return result;
        }

        /// <summary>
        /// Translates a Japanese entity name to English.
        /// Returns original name if no translation found.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            // Ensure initialized
            if (!isInitialized)
                Initialize();

            // 1. Exact match first (preserves existing behavior)
            if (translations.TryGetValue(japaneseName, out string englishName))
                return englishName;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(japaneseName, out string prefix, out string baseName);
            if (prefix != null && translations.TryGetValue(baseName, out string baseTranslation))
                return prefix + " " + baseTranslation;

            // 3. Track untranslated name by current map (use base name to deduplicate)
            string trackingName = prefix != null ? baseName : japaneseName;
            if (ContainsJapanese(trackingName))
            {
                string mapName = MapNameResolver.GetCurrentMapName();
                if (!string.IsNullOrEmpty(mapName))
                {
                    if (!untranslatedNamesByMap.ContainsKey(mapName))
                        untranslatedNamesByMap[mapName] = new HashSet<string>();
                    untranslatedNamesByMap[mapName].Add(trackingName);
                }
            }

            // Return original if no translation
            return japaneseName;
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
        /// Returns the prefix (e.g., "6:" or "SC01:") and the base name.
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
        /// Dumps untranslated entity names for the current map to EntityNames.json.
        /// Appends by map name with duplicate detection.
        /// Returns a status string for TTS feedback.
        /// </summary>
        public static string DumpUntranslatedNames()
        {
            try
            {
                string currentMap = MapNameResolver.GetCurrentMapName();
                if (string.IsNullOrEmpty(currentMap))
                    return "Could not determine current map.";

                string dumpPath = Path.Combine(
                    Path.GetDirectoryName(translationsPath),
                    "EntityNames.json"
                );

                // Load existing data from file
                var existingData = new Dictionary<string, Dictionary<string, string>>();
                if (File.Exists(dumpPath))
                {
                    string existingJson = File.ReadAllText(dumpPath);
                    existingData = ParseNestedJsonDictionary(existingJson);
                }

                // Check if map already exists in file
                if (existingData.ContainsKey(currentMap))
                    return "Entity data already exists for this map.";

                // Check if we have untranslated names for this map
                if (!untranslatedNamesByMap.ContainsKey(currentMap) || untranslatedNamesByMap[currentMap].Count == 0)
                    return "No untranslated names for this map.";

                // Add current map's names to data
                var mapNames = new Dictionary<string, string>();
                foreach (string name in untranslatedNamesByMap[currentMap])
                {
                    mapNames[name] = "";
                }
                existingData[currentMap] = mapNames;

                // Write nested JSON
                WriteNestedJson(dumpPath, existingData);

                int count = mapNames.Count;
                MelonLogger.Msg($"[EntityTranslator] Dumped {count} names for {currentMap} to: {dumpPath}");
                return $"Dumped {count} names for {currentMap}";
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to save EntityNames.json: {ex.Message}");
                return "Failed to dump entity names.";
            }
        }

        /// <summary>
        /// Parses nested JSON: { "MapName": { "Japanese": "", ... }, ... }
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> ParseNestedJsonDictionary(string json)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            if (string.IsNullOrWhiteSpace(json))
                return result;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return result;

            // Remove outer braces
            json = json.Substring(1, json.Length - 2).Trim();
            if (string.IsNullOrEmpty(json))
                return result;

            int pos = 0;
            while (pos < json.Length)
            {
                // Find map name key
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;

                int keyEnd = FindClosingQuote(json, keyStart + 1);
                if (keyEnd < 0) break;

                string mapKey = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                mapKey = mapKey.Replace("\\\"", "\"").Replace("\\\\", "\\");

                // Find the opening brace for this map's value
                int braceStart = json.IndexOf('{', keyEnd);
                if (braceStart < 0) break;

                // Find matching closing brace
                int braceEnd = FindMatchingBrace(json, braceStart);
                if (braceEnd < 0) break;

                // Parse inner dictionary
                string innerJson = json.Substring(braceStart, braceEnd - braceStart + 1);
                result[mapKey] = ParseJsonDictionary(innerJson);

                pos = braceEnd + 1;
            }

            return result;
        }

        /// <summary>
        /// Finds the closing quote for a JSON string, handling escaped quotes.
        /// </summary>
        private static int FindClosingQuote(string json, int startPos)
        {
            int pos = startPos;
            while (pos < json.Length)
            {
                pos = json.IndexOf('"', pos);
                if (pos < 0) return -1;

                // Count preceding backslashes
                int backslashes = 0;
                int checkPos = pos - 1;
                while (checkPos >= startPos - 1 && json[checkPos] == '\\')
                {
                    backslashes++;
                    checkPos--;
                }

                if (backslashes % 2 == 0)
                    return pos; // Not escaped

                pos++;
            }
            return -1;
        }

        /// <summary>
        /// Finds the matching closing brace for an opening brace.
        /// </summary>
        private static int FindMatchingBrace(string json, int openPos)
        {
            int depth = 0;
            bool inString = false;

            for (int i = openPos; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (c == '\\')
                    {
                        i++; // Skip escaped character
                        continue;
                    }
                    if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Writes a nested dictionary as formatted JSON to a file.
        /// </summary>
        private static void WriteNestedJson(string path, Dictionary<string, Dictionary<string, string>> data)
        {
            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("{");
                var mapKeys = new List<string>(data.Keys);
                for (int m = 0; m < mapKeys.Count; m++)
                {
                    string mapKey = mapKeys[m];
                    string escapedMapKey = mapKey.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    writer.WriteLine($"  \"{escapedMapKey}\": {{");

                    var names = new List<string>(data[mapKey].Keys);
                    for (int n = 0; n < names.Count; n++)
                    {
                        string escapedName = names[n].Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string escapedValue = data[mapKey][names[n]].Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string comma = (n < names.Count - 1) ? "," : "";
                        writer.WriteLine($"    \"{escapedName}\": \"{escapedValue}\"{comma}");
                    }

                    string mapComma = (m < mapKeys.Count - 1) ? "," : "";
                    writer.WriteLine($"  }}{mapComma}");
                }
                writer.WriteLine("}");
            }
        }

        /// <summary>
        /// Reloads translations from file.
        /// </summary>
        public static void Reload()
        {
            if (!string.IsNullOrEmpty(translationsPath) && File.Exists(translationsPath))
            {
                LoadTranslations();
            }
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations.Count;
    }
}
