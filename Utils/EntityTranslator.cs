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
    /// Translates Japanese entity names to English using an embedded dictionary.
    /// Handles prefixes (e.g., "SC01:", "6:") and suffixes (trailing ASCII numbers).
    /// Circled numbers (①②③) are preserved as part of the key.
    /// </summary>
    public static class EntityTranslator
    {
        // Embedded translations dictionary - no external file dependency
        // Complete set from FFII_translations.json
        private static Dictionary<string, string> translations = new Dictionary<string, string>
        {
            // Main characters
            {"ヒルダ", "Hilda"},
            {"ミンウ", "Minwu"},
            {"ポール", "Paul"},
            {"ゴードン", "Gordon"},
            {"トブール", "Tobul"},
            {"シド", "Cid"},
            {"ヨーゼフ", "Josef"},
            {"ネリー", "Nelly"},
            {"サージェント", "Sergeant"},
            {"ボーゲン", "Borghen"},
            {"ダークナイト", "Dark Knight"},
            {"ゴートス", "Gottos"},
            {"レイラ", "Leila"},
            {"リチャード", "Richard"},
            {"レオンハルト", "Leonhart"},
            {"皇帝", "Emperor"},
            {"皇帝（復活後）", "Emperor (Resurrected)"},

            // Tutorial elders
            {"武器老人", "Weapons Elder"},
            {"ステータス老人", "Status Elder"},
            {"熟練度老人", "Proficiency Elder"},
            {"防具老人", "Armor Elder"},
            {"にげる老人", "Escape Elder"},
            {"魔法老人", "Magic Elder"},
            {"宝箱老人", "Treasure Elder"},
            {"モンスター老人", "Monster Elder"},
            {"ことば老人", "Keywords Elder"},
            {"たいれつ老人", "Formation Elder"},

            // Shop keepers - weapon shops
            {"武器屋（赤い男性）", "Weapon Shop Clerk"},
            {"武器屋（赤いおやじ）", "Weapon Shop Clerk"},
            {"武器屋(赤いおやじ)", "Weapon Shop Clerk"},

            // Shop keepers - armor shops
            {"防具屋（緑のオヤジ）", "Armor Shop Clerk"},
            {"防具屋(緑のおやじ)", "Armor Shop Clerk"},
            {"武具屋（緑のオヤジ）", "Armor Shop Clerk"},
            {"武具屋(緑のおやじ)", "Armor Shop Clerk"},

            // Shop keepers - item shops
            {"道具屋（青いおやじ）", "Item Shop Clerk"},
            {"道具屋(青いおやじ)", "Item Shop Clerk"},

            // Shop keepers - inns
            {"宿屋（ピンクドレスの女性）", "Innkeeper"},
            {"宿屋(ピンクドレスの女性)", "Innkeeper"},

            // Shop keepers - magic and Fynn
            {"魔法屋", "Magic Shop"},
            {"フィンの町の店主", "Fynn Shop Clerk"},
            {"フィンの町の店主　色替え1", "Fynn Shop Clerk (Variant 1)"},
            {"フィンの町の店主色替え2", "Fynn Shop Clerk (Variant 2)"},

            // Generic NPCs - men
            {"男性（青服）", "Man (Blue Clothes)"},
            {"男性（青服①）", "Man (Blue Clothes)"},
            {"男性（青服②）", "Man (Blue Clothes)"},

            // Generic NPCs - women and children
            {"女性（青服）", "Woman (Blue Clothes)"},
            {"子供（金髪）", "Child (Blond)"},

            // Generic NPCs - elders and villagers
            {"老人", "Elder"},
            {"村人", "Villager"},
            {"村人（装備関係ショップ）", "Villager (Equipment Shop)"},
            {"村人アイテム関係ショップ）", "Villager (Item Shop)"},

            // Generic NPCs - mages
            {"黒魔導士(青)", "Black Mage (Blue)"},
            {"黒魔導士（青）", "Black Mage (Blue)"},
            {"黒魔導士（赤）", "Black Mage (Red)"},
            {"黒魔導士（緑）", "Black Mage (Green)"},
            {"黒魔術師（青）", "Black Mage (Blue)"},

            // Generic NPCs - adventurers
            {"ポニーテール女", "Adventurer (Ponytail)"},
            {"冒険者(ポニーテール女）", "Adventurer (Ponytail)"},
            {"冒険者（ポニーテール女）", "Adventurer (Ponytail)"},

            // Generic NPCs - soldiers and military
            {"パラメキア兵", "Palamecian Soldier"},
            {"帝国兵", "Imperial Soldier"},
            {"兵士（白い帽子、青マント）", "Soldier (White Hat, Blue Cape)"},
            {"兵士(白い帽子、青マント)", "Soldier (White Hat, Blue Cape)"},

            // Generic NPCs - seafarers
            {"海賊", "Pirate"},
            {"船乗り", "Sailor"},
            {"飛空艇乗り", "Airship Crew"},

            // Generic NPCs - other
            {"奴隷", "Slave"},
            {"仮面　色変え1", "Masked Figure"},

            // Vehicles (overworld spawn points)
            {"飛竜", "Wyvern"},
            {"飛竜(sc_e_0080用)", "Wyvern"},
            {"飛空艇", "Airship"},
            {"大戦艦", "Dreadnought"},

            // Enemies/Monsters
            {"ジャイアントビーバー", "Giant Beaver"},
            {"レッドソウル", "Red Soul"},
            {"キマイラ", "Chimera"},
            {"ベヒーモス", "Behemoth"},
            {"ビックホーン", "Big Horn"},
            {"ドッペルゲンガー", "Doppelganger"},
            {"ファイアギガース", "Fire Gigas"},
            {"アイスギガース", "Ice Gigas"},
            {"サンダギガース", "Thunder Gigas"},

            // Special NPCs - swallowed
            {"飲み込まれた男", "Swallowed Man"},
            {"飲み込まれた女", "Swallowed Woman"},
            {"飲み込まれた海賊", "Swallowed Pirate"},
            {"飲み込まれた老人", "Swallowed Elder"},

            // Objects and prefixed entries
            {"隠し通路の蓋", "Hidden Passage Lid"},
            {"Sc E 0025:隠し通路の蓋", "Hidden Passage Lid"},
            {"Sc E 0054:ベヒーモス", "Behemoth"},
            {"Sc E 0082:皇帝１", "Emperor"},
        };

        private static string translationsPath; // Still needed for dump functionality

        // Track untranslated names by map for dumping
        private static Dictionary<string, HashSet<string>> untranslatedNamesByMap = new Dictionary<string, HashSet<string>>();

        // Matches numeric prefix (e.g., "6:") or SC prefix (e.g., "SC01:", "Sc E 0025:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC\s*E?\s*)?\d+:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches trailing ASCII digits (not circled numbers like ①②③)
        private static readonly Regex TrailingDigitsRegex = new Regex(
            @"([0-9]+)$",
            RegexOptions.Compiled);

        /// <summary>
        /// Translates a Japanese entity name to English.
        /// Returns original name if no translation found.
        /// Handles prefixes and trailing ASCII number suffixes.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            // 1. Exact match first (handles entries with circled numbers like ①②③)
            if (translations.TryGetValue(japaneseName, out string englishName))
                return englishName;

            // 2. Strip numeric/SC prefix and try lookup
            StripPrefix(japaneseName, out string prefix, out string afterPrefix);
            if (prefix != null && translations.TryGetValue(afterPrefix, out string prefixTranslation))
                return prefix + " " + prefixTranslation;

            // 3. Strip trailing ASCII digits and try lookup (e.g., "ヒルダ2" -> "Hilda 2")
            StripTrailingDigits(japaneseName, out string baseName, out string suffix);
            if (suffix != null && translations.TryGetValue(baseName, out string suffixTranslation))
                return suffixTranslation + " " + suffix;

            // 4. Try both prefix AND suffix stripping
            if (prefix != null)
            {
                StripTrailingDigits(afterPrefix, out string baseAfterPrefix, out string suffixAfterPrefix);
                if (suffixAfterPrefix != null && translations.TryGetValue(baseAfterPrefix, out string bothTranslation))
                    return prefix + " " + bothTranslation + " " + suffixAfterPrefix;
            }

            // 5. Track untranslated name by current map (use base name to deduplicate)
            string trackingName = baseName ?? japaneseName;
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
                suffix = match.Groups[1].Value;
                baseName = name.Substring(0, name.Length - suffix.Length);
            }
            else
            {
                suffix = null;
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

                // Build path for dump file
                EnsureTranslationsPath();
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
                return $"Dumped {count} names for {currentMap}";
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to save EntityNames.json: {ex.Message}");
                return "Failed to dump entity names.";
            }
        }

        /// <summary>
        /// Ensures the translations path is set for dump functionality.
        /// </summary>
        private static void EnsureTranslationsPath()
        {
            if (!string.IsNullOrEmpty(translationsPath))
                return;

            string gameDataPath = Application.dataPath;
            string gameRoot = Path.GetDirectoryName(gameDataPath);
            string userDataPath = Path.Combine(gameRoot, "UserData", "FFII_ScreenReader");
            translationsPath = Path.Combine(userDataPath, "FF2_translations.json");

            // Create directory if needed
            if (!Directory.Exists(userDataPath))
            {
                Directory.CreateDirectory(userDataPath);
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
        /// Simple JSON dictionary parser for inner dictionaries.
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
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations.Count;
    }
}
