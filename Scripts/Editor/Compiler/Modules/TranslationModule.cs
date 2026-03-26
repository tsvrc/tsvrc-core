#if UNITY_EDITOR
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
    /// Compiler module that bakes all language JSON files into a single TextAsset and wires
    /// pre-resolved TMP text targets into CompiledTsvrc.
    ///
    /// Combined asset format (minified JSON stored at Assets/CompiledTsvrc/translations.txt):
    /// <code>
    /// {"en":{"_key_":"Value"},"jp":{"_key_":"値"}}
    /// </code>
    ///
    /// Runtime-generated members on CompiledTsvrc:
    ///   - public enum Language  { … }
    ///   - private TextAsset _translationsAsset
    ///   - private TextMeshProUGUI[] _translationTargets
    ///   - private string[] _languageKeys  (parallel with enum ordinals)
    ///   - public void SetLanguage(Language lang)
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
        private List<(string name, bool isUI)> _targetNames = new List<(string, bool)>();

        private List<TextMeshProUGUI> _targets = new List<TextMeshProUGUI>();

        internal override void Scan(TsvrcConfig config)
        {
            _languages.Clear();
            _targetNames.Clear();
            _targets.Clear();

            LoadLanguages();
            CollectTMPTargets();
        }

        private void LoadLanguages()
        {
            var translationConfig = AssetDatabase.LoadAssetAtPath<TranslationConfig>(TranslationConfigAssetPath);
            if (translationConfig == null)
            {
                Debug.Log($"[TranslationModule] No TranslationConfig asset found at '{TranslationConfigAssetPath}' — skipping translation generation.");
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
                    entries[kv.Key] = kv.Value?.ToString() ?? "";

                return new LanguageEntry
                {
                    Key = keyObj.ToString(),
                    Label = labelObj.ToString(),
                    Entries = entries,
                };
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[TranslationModule] Failed to parse language file '{assetName}': {ex.Message}");
                return null;
            }
        }

        private void CollectTMPTargets()
        {
            foreach (var tmp in Object.FindObjectsOfType<TextMeshProUGUI>())
                if (TargetPattern.IsMatch(tmp.gameObject.name))
                    _targets.Add(tmp);
        }

        internal override IEnumerable<string> GetUsings()
        {
            if (_languages.Count == 0) yield break;
            yield return "TMPro";
            yield return "VRC.SDK3.Data";
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

            // Language-key lookup array (parallel with enum ordinals)
            var keyLiterals = new List<string>();
            foreach (var lang in _languages)
                keyLiterals.Add($"\"{EscapeString(lang.Key)}\"");
            w.Line($"private string[] _languageKeys = new string[] {{ {string.Join(", ", keyLiterals)} }};");

            // Baked text asset
            w.Line("[HideInInspector] [SerializeField] private TextAsset _translationsAsset;");

            // TMP target array
            if (_targets.Count > 0)
                w.Line($"[HideInInspector] [SerializeField] private TMPro.TextMeshProUGUI[] _translationTargets;");

            w.EndRegion();
        }

        internal override void WriteMethods(CsWriter w)
        {
            if (_languages.Count == 0) return;

            using (w.Method("public void SetLanguage(Language lang)"))
            {
                w.Line("if (_translationsAsset == null) { Debug.LogError(\"[CompiledTsvrc] Translation asset is missing — recompile Tsvrc.\"); return; }");
                w.Line("VRC.SDK3.Data.DataToken _tsRoot;");
                w.Line("if (!VRC.SDK3.Data.VRCJson.TryDeserializeFromJson(_translationsAsset.text, out _tsRoot)) { Debug.LogError(\"[CompiledTsvrc] Failed to parse translation data.\"); return; }");
                w.Line("VRC.SDK3.Data.DataToken _tsLang;");
                w.Line("if (!_tsRoot.DataDictionary.TryGetValue(_languageKeys[(int)lang], out _tsLang)) { Debug.LogError($\"[CompiledTsvrc] Language key not found: {_languageKeys[(int)lang]}\"); return; }");
                w.Line("var _tsEntries = _tsLang.DataDictionary;");
                w.Line("VRC.SDK3.Data.DataToken _tsVal;");

                if (_targets.Count > 0)
                {
                    using (w.Block("foreach (var _tsTmp in _translationTargets)"))
                    {
                        w.Line("if (_tsEntries.TryGetValue(_tsTmp.gameObject.name, out _tsVal))");
                        w.Line("    _tsTmp.text = _tsVal.String;");
                    }
                }
            }
        }

        internal override void WriteStartBody(CsWriter w) { /* nothing to construct */ }

        internal override void Wire(SerializedObject target)
        {
            if (_languages.Count == 0) return;

            BakeCombinedAsset();

            var assetProp = target.FindProperty("_translationsAsset");
            if (assetProp != null)
            {
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(TranslationAssetRelPath);
                assetProp.objectReferenceValue = ta;
            }

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
            Directory.CreateDirectory(Path.GetDirectoryName(absPath));
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
                var name = seen.Contains(baseName)
                    ? baseName + "_" + SanitizeIdentifier(lang.Key)
                    : baseName;
                seen.Add(baseName); // prevent future labels from colliding with this base
                seen.Add(name);     // prevent duplicate after suffix
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
            return sb.ToString().Trim('_');
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
