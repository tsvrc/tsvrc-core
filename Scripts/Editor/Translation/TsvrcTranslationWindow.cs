#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Editor window for managing Tsvrc translation language files.
    /// Open via Tsvrc > Translation
    /// </summary>
    internal class TsvrcTranslationWindow : EditorWindow
    {
        private const string ConfigAssetPath = "Assets/CompiledTsvrc/TranslationConfig.asset";
        private static readonly Regex TargetPattern = new Regex(@"^_[^_].*[^_]_$|^_[^_]_$", RegexOptions.Compiled);
        private static readonly Regex KeyRegex = new Regex(@"""key""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex LabelRegex = new Regex(@"""label""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

        private TranslationConfig _config;
        private SerializedObject _so;
        private Vector2 _scroll;
        private int _tmpTargetCount = -1; // -1 = stale; invalidated by hierarchy changes
        // Caches PeekKeyLabel results per TextAsset instanceID — avoids re-running two regex
        // matches per entry per repaint. Cleared when the config is reloaded.
        private readonly Dictionary<int, (string key, string label)> _peekCache =
            new Dictionary<int, (string key, string label)>();

        private SerializedProperty LanguageFiles => _so?.FindProperty("LanguageFiles");

        [MenuItem("Tsvrc/Translation")]
        private static void Open() => GetWindow<TsvrcTranslationWindow>("Tsvrc Translation").Show();

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
            _config = AssetDatabase.LoadAssetAtPath<TranslationConfig>(ConfigAssetPath);
            _so = _config != null ? new SerializedObject(_config) : null;
            _peekCache.Clear();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Tsvrc Translation", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_config == null)
            {
                EditorGUILayout.HelpBox("No TranslationConfig asset found.\nCreate one to start configuring language files.", MessageType.Info);
                if (GUILayout.Button("Create TranslationConfig"))
                    CreateConfig();
                return;
            }

            _so.Update();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Config: {ConfigAssetPath}", EditorStyles.miniLabel);
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
                        peek = PeekKeyLabel(newAsset.text);
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

            // Deferred outside the draw loop — deleting inside BeginHorizontal would leak the layout group.
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
                prop.InsertArrayElementAtIndex(prop.arraySize);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Sample JSON"))
                ShowCreateSampleDialog();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawScenePreview()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Detected TMP Targets in Scene", EditorStyles.boldLabel);

            if (_tmpTargetCount < 0)
            {
                _tmpTargetCount = 0;
                foreach (var t in FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
                    if (TargetPattern.IsMatch(t.gameObject.name)) _tmpTargetCount++;
            }

            EditorGUILayout.LabelField($"TextMeshProUGUI (UI):   {_tmpTargetCount}", EditorStyles.miniLabel);
        }

        private void CreateConfig()
        {
            var dir = Path.GetDirectoryName(ConfigAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));

            var instance = CreateInstance<TranslationConfig>();
            AssetDatabase.CreateAsset(instance, ConfigAssetPath);
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

        private static (string key, string label) PeekKeyLabel(string json)
        {
            var k = KeyRegex.Match(json);
            var l = LabelRegex.Match(json);
            return k.Success && l.Success ? (k.Groups[1].Value, l.Groups[1].Value) : (null, null);
        }
    }
}
#endif

