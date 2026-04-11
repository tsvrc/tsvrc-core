#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Tsvrc.Core;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Compiler module that bakes all language data directly into the generated C# source as flat
    /// parallel string arrays — no JSON asset loaded, no DataDictionary, zero GC pressure at runtime.
    ///
    /// Generated members on CompiledTsvrc:
    ///   - public enum Language  { … }
    ///   - private string[] _tsKeys_{lang}, _tsVals_{lang}  — one pair per language, baked as literals
    ///   - private string[] _tsCurrentKeys, _tsCurrentVals  — pointers to the active language arrays
    ///   - private int _tsCurrentLang                       — guard against redundant SetLanguage calls
    ///   - private TextMeshProUGUI[] _translationTargets    — scene-wired TMPs (serialized)
    ///   - public void SetLanguage(Language lang)           — switches pointers; starts batched target update
    ///   - public string Translate(string key)              — O(n) scan, n ≈ number of keys (small)
    ///   - public string Translate(string key, string param)— same with {value} substitution
    ///   - public void _TsApplyTranslationBatch()           — batched per-frame update of static targets
    /// </summary>
    internal class TranslationModule : TsvrcModule
    {
        private const string TranslationAssetRelPath = "Assets/CompiledTsvrc/translations.txt";
        private const string TranslationConfigAssetPath = "Assets/CompiledTsvrc/TranslationConfig.asset";
        private static readonly Regex TargetPattern = new Regex(@"^_[^_].*[^_]_$|^_[^_]_$", RegexOptions.Compiled);

        private struct LanguageEntry
        {
            public string Key;    // e.g. "en"
            public string Label;  // e.g. "English"  (used for enum member name)
            public Dictionary<string, string> Entries; // textKey -> translated string
        }

        private List<LanguageEntry> _languages = new List<LanguageEntry>();
        private List<TextMeshProUGUI> _targets = new List<TextMeshProUGUI>();

        private const int BatchSize = 20;

        internal override void Scan(TsvrcConfig config)
        {
            _languages.Clear();
            _targets.Clear();

            LoadLanguages();
            CollectTMPTargets();
        }

        private void LoadLanguages()
        {
            var translationConfig = AssetDatabase.LoadAssetAtPath<TranslationConfig>(TranslationConfigAssetPath);
            if (translationConfig == null)
            {
                Debug.Log($"[TranslationModule] No TranslationConfig asset found at '{TranslationConfigAssetPath}'. Skipping translation generation.");
                return;
            }

            foreach (var asset in translationConfig.LanguageFiles)
            {
                if (asset == null) continue;
                var entry = ParseLanguageJson(asset.name, asset.text);
                if (entry.HasValue)
                    _languages.Add(entry.Value);
            }
        }

        private static LanguageEntry? ParseLanguageJson(string assetName, string json)
        {
            try
            {
                var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                if (root == null || !root.TryGetValue("key", out var keyObj) || keyObj == null)
                {
                    Debug.LogError($"[TranslationModule] Language file '{assetName}' is missing the 'key' field.");
                    return null;
                }
                if (!root.TryGetValue("label", out var labelObj) || labelObj == null)
                {
                    Debug.LogError($"[TranslationModule] Language file '{assetName}' is missing the 'label' field.");
                    return null;
                }
                if (!root.TryGetValue("entries", out var entriesObj)
                    || !(entriesObj is Newtonsoft.Json.Linq.JObject jEntries))
                {
                    Debug.LogError($"[TranslationModule] Language file '{assetName}' is missing the 'entries' object.");
                    return null;
                }

                var entries = new Dictionary<string, string>();
                foreach (var kv in jEntries)
                {
                    if (kv.Value is Newtonsoft.Json.Linq.JObject entryObj)
                    {
                        var labelToken = entryObj["label"];
                        entries[kv.Key] = labelToken?.ToString() ?? "";
                    }
                    else
                    {
                        entries[kv.Key] = kv.Value?.ToString() ?? "";
                    }
                }

                return new LanguageEntry
                {
                    Key = keyObj.ToString(),
                    Label = labelObj.ToString(),
                    Entries = entries,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TranslationModule] Failed to parse language file '{assetName}': {ex.Message}");
                return null;
            }
        }

        private void CollectTMPTargets()
        {
            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true))
                if (TargetPattern.IsMatch(tmp.gameObject.name))
                    _targets.Add(tmp);
        }

        internal override IEnumerable<string> GetUsings()
        {
            if (_languages.Count == 0) yield break;
            yield return "TMPro";
        }

        internal override void WriteBeforeClass(CsWriter w)
        {
            if (_languages.Count == 0) return;
            w.Line($"public enum Language {{ {string.Join(", ", BuildEnumNames())} }}");
            w.BlankLine();
        }

        internal override void WriteFields(CsWriter w)
        {
            if (_languages.Count == 0) return;

            w.Region("Translation");

            // Bake key/value pairs per language as flat parallel arrays.
            // All data is known at compile time — no TextAsset, no DataDictionary, no JSON parsing at runtime.
            foreach (var lang in _languages)
            {
                var id = SanitizeIdentifier(lang.Key);
                var keyLits = new List<string>();
                var valLits = new List<string>();
                foreach (var kv in lang.Entries)
                {
                    keyLits.Add($"\"{EscapeString(kv.Key)}\"");
                    valLits.Add($"\"{EscapeString(kv.Value)}\"");
                }
                w.Line($"private string[] _tsKeys_{id} = new string[] {{ {string.Join(", ", keyLits)} }};");
                w.Line($"private string[] _tsVals_{id} = new string[] {{ {string.Join(", ", valLits)} }};");
            }

            // Active language — just two pointers, switched in O(1) by SetLanguage.
            w.Line("private string[] _tsCurrentKeys;");
            w.Line("private string[] _tsCurrentVals;");
            // Guard against redundant SetLanguage calls (-1 = none set).
            w.Line("private int _tsCurrentLang = -1;");

            if (_targets.Count > 0)
            {
                // Scene-wired TMP targets — serialized references baked at compile time.
                w.Line("[HideInInspector] [SerializeField] private TMPro.TextMeshProUGUI[] _translationTargets;");
                // Batched visual update state.
                w.Line("private int _tsBatchIndex;");
                w.Line("private bool _tsBatchRunning;");
            }

            w.EndRegion();
        }

        internal override void WriteMethods(CsWriter w)
        {
            if (_languages.Count == 0) return;

            // SetLanguage — switches active array pointers; O(1), no allocation.
            using (w.Method("public void SetLanguage(Language lang)"))
            {
                w.Line("int _tsIdx = (int)lang;");
                w.Line("if (_tsIdx == _tsCurrentLang) return;");

                // Emit if/else chain to assign the correct baked arrays.
                for (int i = 0; i < _languages.Count; i++)
                {
                    var id = SanitizeIdentifier(_languages[i].Key);
                    string prefix = i == 0 ? "if" : "else if";
                    w.Line($"{prefix} (_tsIdx == {i}) {{ _tsCurrentKeys = _tsKeys_{id}; _tsCurrentVals = _tsVals_{id}; }}");
                }
                w.Line($"else {{ Debug.LogError($\"[CompiledTsvrc] Language index {{_tsIdx}} is not available.\"); return; }}");
                w.Line("_tsCurrentLang = _tsIdx;");

                if (_targets.Count > 0)
                {
                    w.Line("_tsBatchIndex = 0;");
                    w.Line("_tsBatchRunning = true;");
                    w.Line("_TsApplyTranslationBatch();");
                }
            }

            // Translate — linear scan across baked keys. With ~20 keys this is negligible.
            using (w.Method("public string Translate(string key)"))
            {
                w.Line("if (_tsCurrentKeys == null) { Debug.LogWarning(\"[CompiledTsvrc] Translate called before SetLanguage.\"); return key; }");
                using (w.Block("for (int _tsI = 0; _tsI < _tsCurrentKeys.Length; _tsI++)"))
                    w.Line("if (_tsCurrentKeys[_tsI] == key) return _tsCurrentVals[_tsI];");
                w.Line("return key;");
            }

            // Translate with {value} substitution.
            using (w.Method("public string Translate(string key, string param)"))
            {
                w.Line("if (_tsCurrentKeys == null) { Debug.LogWarning(\"[CompiledTsvrc] Translate called before SetLanguage.\"); return key; }");
                using (w.Block("for (int _tsI = 0; _tsI < _tsCurrentKeys.Length; _tsI++)"))
                    w.Line("if (_tsCurrentKeys[_tsI] == key) return _tsCurrentVals[_tsI].Replace(\"{value}\", param);");
                w.Line("return key;");
            }

            // Batched scene-target update — only generated when there are static targets.
            if (_targets.Count > 0)
            {
                using (w.Method("public void _TsApplyTranslationBatch()"))
                {
                    w.Line("if (!_tsBatchRunning || _tsCurrentKeys == null) return;");
                    w.Line($"int _tsEnd = Mathf.Min(_tsBatchIndex + {BatchSize}, _translationTargets.Length);");
                    using (w.Block("for (int _tsI = _tsBatchIndex; _tsI < _tsEnd; _tsI++)"))
                    {
                        w.Line("if (_translationTargets[_tsI] == null) continue;");
                        w.Line("string _tsName = _translationTargets[_tsI].gameObject.name;");
                        using (w.Block("for (int _tsJ = 0; _tsJ < _tsCurrentKeys.Length; _tsJ++)"))
                        {
                            using (w.Block("if (_tsCurrentKeys[_tsJ] == _tsName)"))
                            {
                                w.Line("_translationTargets[_tsI].text = _tsCurrentVals[_tsJ];");
                                w.Line("break;");
                            }
                        }
                    }
                    w.Line("_tsBatchIndex = _tsEnd;");
                    using (w.Block("if (_tsBatchIndex < _translationTargets.Length)"))
                        w.Line("SendCustomEventDelayedFrames(\"_TsApplyTranslationBatch\", 1);");
                    w.Line("else _tsBatchRunning = false;");
                }
            }
        }

        internal override void WriteStartBody(CsWriter w) { /* arrays are baked — no runtime init needed */ }

        internal override void Wire(SerializedObject target)
        {
            if (_languages.Count == 0) return;

            // Still bake the combined JSON for developer reference / debugging only — not used at runtime.
            BakeCombinedAsset();

            if (_targets.Count > 0)
            {
                var targetsProp = target.FindProperty("_translationTargets");
                if (targetsProp != null)
                {
                    targetsProp.arraySize = _targets.Count;
                    for (int i = 0; i < _targets.Count; i++)
                        targetsProp.GetArrayElementAtIndex(i).objectReferenceValue = _targets[i];
                }
            }
        }

        private void BakeCombinedAsset()
        {
            var sb = new StringBuilder("{");
            bool firstLang = true;
            foreach (var lang in _languages)
            {
                if (!firstLang) sb.Append(',');
                firstLang = false;

                sb.Append($"\"{EscapeString(lang.Key)}\":{{");
                bool firstEntry = true;
                foreach (var kv in lang.Entries)
                {
                    if (!firstEntry) sb.Append(',');
                    firstEntry = false;
                    sb.Append($"\"{EscapeString(kv.Key)}\":\"{EscapeString(kv.Value)}\"");
                }
                sb.Append('}');
            }
            sb.Append('}');

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absPath = Path.Combine(projectRoot, TranslationAssetRelPath.Replace('/', Path.DirectorySeparatorChar));
            string absDir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(absDir))
                Directory.CreateDirectory(absDir);
            File.WriteAllText(absPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(TranslationAssetRelPath);
        }

        private List<string> BuildEnumNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lang in _languages)
            {
                var baseName = SanitizeIdentifier(lang.Label);
                string name;
                if (!seen.Contains(baseName))
                {
                    name = baseName;
                }
                else
                {
                    name = baseName + "_" + SanitizeIdentifier(lang.Key);
                    int counter = 2;
                    while (seen.Contains(name))
                        name = baseName + "_" + SanitizeIdentifier(lang.Key) + counter++;
                }
                seen.Add(name);
                names.Add(name);
            }
            return names;
        }

        private static string SanitizeIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0) sb.Append('_');
            }
            if (sb.Length == 0) return "_";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
#endif
