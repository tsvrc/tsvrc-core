#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Open via Tsvrc > Translation.
    internal class TsTranslationWindow : EditorWindow
    {
        private static readonly Regex TargetPattern = new Regex(@"^_[^_].*[^_]_$|^_[^_]_$", RegexOptions.Compiled);
        // Matches a quoted, underscore-wrapped JSON key (an "entries" key like "_welcome_"). Not
        // JSON-aware (same trade-off as KeyRegex/LabelRegex), but the _x_ naming convention keeps
        // a plain-text scan reliable in practice.
        private static readonly Regex EntryKeyRegex = new Regex(@"""(_[^""]*_)""\s*:", RegexOptions.Compiled);

        private TsTranslationConfig _config;
        private SerializedObject _so;
        private Vector2 _scroll;
        private int _tmpTargetCount = -1; // -1 = stale; invalidated by hierarchy changes
        private List<string> _missingKeys; // stale whenever _tmpTargetCount is stale
        // Caches PeekKeyLabel results per TextAsset instanceID, avoids re-running two regex
        // matches per entry per repaint. Cleared when the config is reloaded.
        private readonly Dictionary<int, (string key, string label)> _peekCache =
            new Dictionary<int, (string key, string label)>();

        private SerializedProperty LanguageFiles => _so?.FindProperty("LanguageFiles");

        [MenuItem("Tsvrc/Translation", priority = 2)]
        private static void Open()
        {
            var window = GetWindow<TsTranslationWindow>("Translation");
            window.minSize = new Vector2(460, 380);
            window.Show();
        }

        private void OnEnable()
        {
            Reload();
            EditorApplication.hierarchyChanged += InvalidateTmpCount;
            EditorApplication.projectChanged += _peekCache.Clear;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= InvalidateTmpCount;
            EditorApplication.projectChanged -= _peekCache.Clear;
        }

        private void InvalidateTmpCount() => _tmpTargetCount = -1;

        private void OnFocus()
        {
            if (_config == null)
                Reload();
        }

        private void Reload()
        {
            _config = AssetDatabase.LoadAssetAtPath<TsTranslationConfig>(TranslationModule.ConfigAssetPath);
            _so = _config != null ? new SerializedObject(_config) : null;
            _peekCache.Clear();
        }

        private void OnGUI()
        {
            if (_config == null)
            {
                TsEditorGUI.DrawStatusBox(
                    $"No translation config found at {TranslationModule.ConfigAssetPath}.\nCreate one to start configuring language files.",
                    MessageType.Info);
                if (TsEditorGUI.PrimaryButton("Create Translation Config"))
                    CreateConfig();
                return;
            }

            _so.Update();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Config: {TranslationModule.ConfigAssetPath}", EditorStyles.miniLabel);
            if (GUILayout.Button("Select", GUILayout.Width(54)))
                Selection.activeObject = _config;
            EditorGUILayout.EndHorizontal();

            DrawLanguageList();

            _so.ApplyModifiedProperties();

            DrawScenePreview();
        }

        private void DrawLanguageList()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Language Files", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "One JSON per language. Required fields: \"key\" (e.g. \"en\"), \"label\" (e.g. \"English\"), \"entries\" (key/value pairs).\n\nEach entry can be a plain string or an object with \"label\" (the translated text) and an optional \"description\" field (notes for translators, never included in the build).",
                MessageType.None);
            EditorGUILayout.Space(4);

            var prop = LanguageFiles;
            int toDelete = -1;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var newAsset = (TextAsset)EditorGUILayout.ObjectField(element.objectReferenceValue as TextAsset, typeof(TextAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    // Evict the replaced asset from the peek cache so stale entries don't accumulate.
                    if (element.objectReferenceValue is TextAsset replaced)
                        _peekCache.Remove(replaced.GetInstanceID());
                    element.objectReferenceValue = newAsset;
                }

                if (newAsset != null)
                {
                    int id = newAsset.GetInstanceID();
                    if (!_peekCache.TryGetValue(id, out var peek))
                    {
                        peek = PeekKeyLabel(newAsset);
                        _peekCache[id] = peek;
                    }
                    EditorGUILayout.LabelField(peek.key != null ? $"{peek.key}  –  {peek.label}" : "⚠ Invalid File",
                        EditorStyles.miniLabel, GUILayout.Width(180));
                }

                if (GUILayout.Button("✕", GUILayout.Width(22)))
                    toDelete = i;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // Deferred outside the draw loop, deleting inside BeginHorizontal would leak the layout group.
            // Two-step removal required for UnityEngine.Object arrays: Unity clears the reference
            // on the first call, then actually removes the slot on the second.
            if (toDelete >= 0)
            {
                var slot = prop.GetArrayElementAtIndex(toDelete);
                if (slot.objectReferenceValue is TextAsset removed)
                    _peekCache.Remove(removed.GetInstanceID());
                slot.objectReferenceValue = null;
                prop.DeleteArrayElementAtIndex(toDelete);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Language"))
            {
                // InsertArrayElementAtIndex on a TextAsset[] array copies the last element's
                // reference into the new slot instead of leaving it empty (a well known
                // SerializedProperty quirk for reference-type arrays) - cleared explicitly so
                // "+ Add Language" always adds a genuinely empty slot.
                int newIndex = prop.arraySize;
                prop.InsertArrayElementAtIndex(newIndex);
                prop.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Create Sample Language File",
                "Writes a starter JSON file with the required \"key\"/\"label\"/\"entries\" shape and adds it to the list below.")))
                ShowCreateSampleDialog();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawScenePreview()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Detected TMP Targets in Scene", EditorStyles.boldLabel);

            // Scoped to the linked scene (TsLinkedScene), the same scene TranslationModule's own
            // FindTmpTargets() reads from at generate time - otherwise this preview could show
            // counts from whatever scene happens to be open, not the one Wire() actually acts on.
            if (TsLinkedScene.IsConfiguredButNotLoaded)
            {
                TsEditorGUI.DrawStatusBox(
                    $"Linked scene '{TsLinkedScene.ScenePath}' is not open, so there's nothing to detect right now.",
                    MessageType.Info);
                return;
            }

            if (_tmpTargetCount < 0)
            {
                var sceneKeys = TsLinkedScene.FindAll<TMPro.TextMeshProUGUI>()
                    .Select(t => t.gameObject.name)
                    .Where(name => TargetPattern.IsMatch(name))
                    .ToList();
                _tmpTargetCount = sceneKeys.Count;

                var availableKeys = LanguageFiles == null
                    ? Enumerable.Empty<string>()
                    : Enumerable.Range(0, LanguageFiles.arraySize)
                        .Select(i => LanguageFiles.GetArrayElementAtIndex(i).objectReferenceValue as TextAsset)
                        .Where(ta => ta != null)
                        .SelectMany(ta => ExtractEntryKeys(ta.text));
                _missingKeys = FindMissingKeys(sceneKeys, availableKeys);
            }

            EditorGUILayout.LabelField($"TextMeshProUGUI (UI):   {_tmpTargetCount}", EditorStyles.miniLabel);

            if (_missingKeys.Count > 0)
                TsEditorGUI.DrawStatusBox(
                    $"{_missingKeys.Count} scene target(s) have no matching key in any language file " +
                    $"(likely a typo in the GameObject name): {string.Join(", ", _missingKeys)}",
                    MessageType.Warning);
        }

        // Every "_x_"-style entry key in the file's raw JSON text, regardless of nesting depth.
        internal static IEnumerable<string> ExtractEntryKeys(string json)
        {
            if (string.IsNullOrEmpty(json)) yield break;
            foreach (Match m in EntryKeyRegex.Matches(json))
                yield return m.Groups[1].Value;
        }

        // Scene target names (already filtered to the "_x_" convention) with no matching key in
        // any configured language file. Pure and internal so it's directly unit-testable.
        internal static List<string> FindMissingKeys(IEnumerable<string> sceneTargetKeys, IEnumerable<string> availableKeys)
        {
            var available = new HashSet<string>(availableKeys);
            return sceneTargetKeys
                .Where(k => !available.Contains(k))
                .Distinct()
                .OrderBy(k => k, System.StringComparer.Ordinal)
                .ToList();
        }

        private void CreateConfig()
        {
            var dir = Path.GetDirectoryName(TranslationModule.ConfigAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));

            var instance = CreateInstance<TsTranslationConfig>();
            AssetDatabase.CreateAsset(instance, TranslationModule.ConfigAssetPath);
            AssetDatabase.SaveAssets();
            Reload();
        }

        private void ShowCreateSampleDialog()
        {
            string absPath = EditorUtility.SaveFilePanel("Create Sample Language JSON", Application.dataPath, "language_en", "json");
            if (string.IsNullOrEmpty(absPath)) return;

            File.WriteAllText(absPath,
@"{
    ""key"": ""en"",
    ""label"": ""English"",
    ""entries"": {
        ""_welcome_"": ""Welcome!"",
        ""_start_"": {
            ""label"": ""Start"",
            ""description"": ""Button label to start the experience.""
        },
        ""_exit_"": ""Exit""
    }
}", System.Text.Encoding.UTF8);

            string relPath = FileUtil.GetProjectRelativePath(absPath);
            AssetDatabase.ImportAsset(relPath);
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(relPath);
            if (ta == null) return;

            _so.Update();
            var prop = LanguageFiles;
            prop.InsertArrayElementAtIndex(prop.arraySize);
            prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = ta;
            _so.ApplyModifiedProperties();
        }

        // Uses the same parser TranslationModule.LoadConfig() uses for the real regenerate pass,
        // so a file that will fail real parsing never shows a plausible-looking preview here, and
        // vice versa.
        private static (string key, string label) PeekKeyLabel(TextAsset asset)
        {
            var entry = TranslationModule.ParseLanguageJson(asset.name, asset.text);
            return entry.HasValue ? (entry.Value.Key, entry.Value.Label) : (null, null);
        }
    }
}
#endif
