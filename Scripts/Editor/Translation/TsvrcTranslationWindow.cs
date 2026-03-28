#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Editor window for managing Tsvrc translation language files.
    /// Open via Tsvrc > Setup Translation
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

        private SerializedProperty LanguageFiles => _so?.FindProperty("LanguageFiles");

        [MenuItem("Tsvrc/Setup Translation")]
        private static void Open() => GetWindow<TsvrcTranslationWindow>("Tsvrc Translation").Show();

        private void OnEnable() => Reload();

        private void Reload()
        {
            _config = AssetDatabase.LoadAssetAtPath<TranslationConfig>(ConfigAssetPath);
            _so = _config != null ? new SerializedObject(_config) : null;
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

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Compile Tsvrc"))
                TsvrcCompiler.Compile();
        }

        private void DrawLanguageList()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Language Files", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "One JSON per language. Required fields: \"key\" (e.g. \"en\"), \"label\" (e.g. \"English\"), \"entries\" (key/value pairs).\n\nEach entry can be a plain string or an object with \"label\" (the translated text) and an optional \"description\" (notes for translators — never included in the build).",
                MessageType.None);
            EditorGUILayout.Space(4);

            var prop = LanguageFiles;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = _languageFileElement(prop, i);
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var newAsset = (TextAsset)EditorGUILayout.ObjectField(element.objectReferenceValue as TextAsset, typeof(TextAsset), false);
                if (EditorGUI.EndChangeCheck())
                    element.objectReferenceValue = newAsset;

                if (newAsset != null)
                {
                    var (key, label) = PeekKeyLabel(newAsset.text);
                    EditorGUILayout.LabelField(key != null ? $"{key}  –  {label}" : "⚠ Invalid File",
                        EditorStyles.miniLabel, GUILayout.Width(180));
                }

                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    prop.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Language"))
                prop.InsertArrayElementAtIndex(prop.arraySize);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Sample JSON"))
                ShowCreateSampleDialog();
            EditorGUILayout.EndHorizontal();
        }

        private static SerializedProperty _languageFileElement(SerializedProperty prop, int i)
            => prop.GetArrayElementAtIndex(i);

        private void DrawScenePreview()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Detected TMP Targets in Scene", EditorStyles.boldLabel);

            int textElementsCount = 0;
            foreach (var t in FindObjectsOfType<TMPro.TextMeshProUGUI>())
                if (TargetPattern.IsMatch(t.gameObject.name)) textElementsCount++;

            EditorGUILayout.LabelField($"TextMeshProUGUI (UI):   {textElementsCount}", EditorStyles.miniLabel);
        }

        private void CreateConfig()
        {
            var dir = Path.GetDirectoryName(ConfigAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));

            _config = CreateInstance<TranslationConfig>();
            AssetDatabase.CreateAsset(_config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            _so = new SerializedObject(_config);
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

