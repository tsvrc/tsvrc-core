#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates the Language enum, SetLanguage(), Translate(), and _TsApplyTranslationBatch()
    // on TsvrcGenerated from JSON language files. Translation targets are discovered by
    // scanning the scene for TextMeshProUGUI components whose GameObject name matches the
    // _key_ pattern and appears in at least one language file.
    internal class TranslationModule : TsvrcModule
    {
        internal const string ConfigAssetPath = "Assets/TsvrcGenerated/TsvrcTranslationConfig.asset";
        private const int BatchSize = 20;

        // Matches TMP GameObjects named with a single underscore on each side, e.g. "_greeting_".
        private static readonly Regex TmpTargetPattern = new Regex(@"^_[^_].*[^_]_$|^_[^_]_$", RegexOptions.Compiled);

        private TsvrcTranslationConfig _config;
        private List<LanguageEntry> _languages = new List<LanguageEntry>();
        private HashSet<string> _translationKeys = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _effectiveKeys = new HashSet<string>(StringComparer.Ordinal);
        private List<TMPro.TextMeshProUGUI> _cachedTmpTargets = new List<TMPro.TextMeshProUGUI>();

        internal override string FileName => "TsvrcGeneratedTranslation.cs";

        internal override IEnumerable<string> WatchedAssets()
        {
            var paths = new List<string> { ConfigAssetPath };
            if (_config?.LanguageFiles != null)
                foreach (var asset in _config.LanguageFiles)
                    if (asset != null)
                        paths.Add(AssetDatabase.GetAssetPath(asset));
            return paths;
        }

        internal override void LoadConfig()
        {
            _config = AssetDatabase.LoadAssetAtPath<TsvrcTranslationConfig>(ConfigAssetPath);
            _languages = _config != null ? ParseLanguageFiles(_config) : new List<LanguageEntry>();
            _translationKeys = BuildTranslationKeys(_languages);
            _cachedTmpTargets = FindTmpTargets();
            _effectiveKeys = NamesOf(_cachedTmpTargets);
        }

        internal override string GenerateCode()
        {
            if (_languages.Count == 0)
                return BuildStub();

            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UnityEngine", "TMPro", "UdonSharp", "Tsvrc.Utils" });

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            {
                w.Line($"public enum Language {{ {string.Join(", ", BuildEnumNames())} }}");
                w.BlankLine();

                using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
                {
                    foreach (var lang in _languages)
                    {
                        var id = SanitizeIdentifier(lang.Key);
                        var keys = lang.Entries.Keys.Where(k => _effectiveKeys.Contains(k)).ToList();
                        var keyLits = keys.Select(k => $"\"{EscapeString(k)}\"");
                        var valLits = keys.Select(k => $"\"{EscapeString(lang.Entries[k])}\"");
                        w.Line($"private string[] _tsKeys_{id} = new string[] {{ {string.Join(", ", keyLits)} }};");
                        w.Line($"private string[] _tsVals_{id} = new string[] {{ {string.Join(", ", valLits)} }};");
                    }

                    w.Line("private string[] _tsCurrentKeys;");
                    w.Line("private string[] _tsCurrentVals;");
                    w.Line("private int _tsCurrentLang = -1;");
                    w.Line("[HideInInspector] [SerializeField] private TextMeshProUGUI[] _translationTargets;");
                    w.Line("private int _tsBatchIndex;");
                    w.Line("private bool _tsBatchRunning;");
                    w.Line("private UdonSharpBehaviour[] _tsLangListeners = new UdonSharpBehaviour[0];");
                    w.Line("private string[] _tsLangCallbacks = new string[0];");
                    w.BlankLine();

                    using (w.Method("public void SetLanguage(Language lang)"))
                    {
                        w.Line("int _tsIdx = (int)lang;");
                        w.Line("if (_tsIdx == _tsCurrentLang) return;");
                        for (int i = 0; i < _languages.Count; i++)
                        {
                            var id = SanitizeIdentifier(_languages[i].Key);
                            string prefix = i == 0 ? "if" : "else if";
                            w.Line($"{prefix} (_tsIdx == {i}) {{ _tsCurrentKeys = _tsKeys_{id}; _tsCurrentVals = _tsVals_{id}; }}");
                        }
                        w.Line($"else {{ Debug.LogError($\"[TsvrcGenerated] Language index {{{{_tsIdx}}}} is not available.\"); return; }}");
                        w.Line("_tsCurrentLang = _tsIdx;");
                        w.Line("_tsBatchIndex = 0;");
                        w.Line("_tsBatchRunning = true;");
                        w.Line("_TsApplyTranslationBatch();");
                        w.Line("for (int _tsLi = 0; _tsLi < _tsLangListeners.Length; _tsLi++) { if (_tsLangListeners[_tsLi] != null) _tsLangListeners[_tsLi].SendCustomEvent(_tsLangCallbacks[_tsLi]); }");
                    }

                    using (w.Method("public void SubscribeLanguageChanged(UdonSharpBehaviour listener, string callback)"))
                    {
                        w.Line("if (listener == null) return;");
                        w.Line("for (int _tsLi = 0; _tsLi < _tsLangListeners.Length; _tsLi++) if (_tsLangListeners[_tsLi] == listener && _tsLangCallbacks[_tsLi] == callback) return;");
                        w.Line("_tsLangListeners = TsArray.Add(_tsLangListeners, new UdonSharpBehaviour[] { listener });");
                        w.Line("_tsLangCallbacks = TsArray.Add(_tsLangCallbacks, new string[] { callback });");
                    }

                    using (w.Method("public string Translate(string key)"))
                    {
                        w.Line("if (_tsCurrentKeys == null) { Debug.LogWarning(\"[TsvrcGenerated] Translate called before SetLanguage.\"); return key; }");
                        using (w.Block("for (int _tsI = 0; _tsI < _tsCurrentKeys.Length; _tsI++)"))
                            w.Line("if (_tsCurrentKeys[_tsI] == key) return _tsCurrentVals[_tsI];");
                        w.Line("return key;");
                    }

                    using (w.Method("public string Translate(string key, string param)"))
                    {
                        w.Line("if (_tsCurrentKeys == null) { Debug.LogWarning(\"[TsvrcGenerated] Translate called before SetLanguage.\"); return key; }");
                        using (w.Block("for (int _tsI = 0; _tsI < _tsCurrentKeys.Length; _tsI++)"))
                            w.Line("if (_tsCurrentKeys[_tsI] == key) return _tsCurrentVals[_tsI].Replace(\"{value}\", param);");
                        w.Line("return key;");
                    }

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

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            {
                w.Line("public enum Language { }");
                w.BlankLine();
                using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
                {
                    using (w.Method("public void SetLanguage(Language lang)")) { }
                    using (w.Method("public string Translate(string key)")) { w.Line("return key;"); }
                    using (w.Method("public string Translate(string key, string param)")) { w.Line("return key;"); }
                    using (w.Method("public void SubscribeLanguageChanged(UdonSharpBehaviour listener, string callback)")) { }
                    using (w.Method("public void _TsApplyTranslationBatch()")) { }
                }
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable() => SyncEffectiveKeys();
        internal override bool OnSceneHierarchyChanged() => SyncEffectiveKeys();

        private bool SyncEffectiveKeys()
        {
            var fresh = FindTmpTargets();
            var live = NamesOf(fresh);
            if (live.SetEquals(_effectiveKeys)) return false;
            _cachedTmpTargets = fresh;
            _effectiveKeys = live;
            return true;
        }

        internal override void Wire()
        {
            if (_languages.Count == 0) return;

            var root = FindRoot();
            if (root == null) return;

            var so = new SerializedObject(root);
            var prop = so.FindProperty("_translationTargets");
            if (prop == null) return;
            prop.arraySize = _cachedTmpTargets.Count;
            for (int i = 0; i < _cachedTmpTargets.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = _cachedTmpTargets[i];
            ApplyAndMarkDirty(so, root);
        }

        private List<TMPro.TextMeshProUGUI> FindTmpTargets()
        {
            var result = new List<TMPro.TextMeshProUGUI>();
            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
                if (TmpTargetPattern.IsMatch(tmp.gameObject.name) && _translationKeys.Contains(tmp.gameObject.name))
                    result.Add(tmp);
            return result;
        }

        private static HashSet<string> NamesOf(List<TMPro.TextMeshProUGUI> targets)
            => new HashSet<string>(targets.Select(t => t.gameObject.name), StringComparer.Ordinal);

        private static HashSet<string> BuildTranslationKeys(List<LanguageEntry> languages)
            => new HashSet<string>(languages.SelectMany(l => l.Entries.Keys), StringComparer.Ordinal);

        private static List<LanguageEntry> ParseLanguageFiles(TsvrcTranslationConfig config)
        {
            var result = new List<LanguageEntry>();
            if (config.LanguageFiles == null) return result;

            foreach (var asset in config.LanguageFiles)
            {
                if (asset == null) continue;
                var entry = ParseLanguageJson(asset.name, asset.text);
                if (entry.HasValue) result.Add(entry.Value);
            }
            return result;
        }

        private static LanguageEntry? ParseLanguageJson(string assetName, string json)
        {
            try
            {
                var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                if (root == null || !root.TryGetValue("key", out var keyObj) || keyObj == null)
                {
                    Debug.LogError($"[TranslationModule] '{assetName}' is missing the 'key' field.");
                    return null;
                }
                if (!root.TryGetValue("label", out var labelObj) || labelObj == null)
                {
                    Debug.LogError($"[TranslationModule] '{assetName}' is missing the 'label' field.");
                    return null;
                }
                if (!root.TryGetValue("entries", out var entriesObj)
                    || !(entriesObj is Newtonsoft.Json.Linq.JObject jEntries))
                {
                    Debug.LogError($"[TranslationModule] '{assetName}' is missing the 'entries' object.");
                    return null;
                }

                var entries = new Dictionary<string, string>();
                foreach (var kv in jEntries)
                    entries[kv.Key] = kv.Value is Newtonsoft.Json.Linq.JObject obj
                        ? obj["label"]?.ToString() ?? ""
                        : kv.Value?.ToString() ?? "";

                return new LanguageEntry
                {
                    Key = keyObj.ToString(),
                    Label = labelObj.ToString(),
                    Entries = entries,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TranslationModule] Failed to parse '{assetName}': {ex.Message}");
                return null;
            }
        }

        private List<string> BuildEnumNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lang in _languages)
            {
                var baseName = SanitizeIdentifier(lang.Label);
                string name = baseName;
                if (seen.Contains(name))
                {
                    name = baseName + "_" + SanitizeIdentifier(lang.Key);
                    int n = 2;
                    while (seen.Contains(name))
                        name = baseName + "_" + SanitizeIdentifier(lang.Key) + n++;
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
            => s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

        private struct LanguageEntry
        {
            public string Key;
            public string Label;
            public Dictionary<string, string> Entries;
        }
    }
}
#endif
