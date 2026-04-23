#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Editor window for configuring Tsvrc compiler validation behavior.
    /// Open via Tsvrc > Compiler Settings.
    /// </summary>
    internal class TsvrcCompilerSettingsWindow : EditorWindow
    {
        private SerializedObject _settingsObject;
        private SerializedProperty _poolValidation;
        private SerializedProperty _singletonValidation;
        private SerializedProperty _factoryValidation;
        private SerializedProperty _constructValidation;

        [MenuItem("Tsvrc/Compiler Settings")]
        private static void Open()
        {
            GetWindow<TsvrcCompilerSettingsWindow>("Tsvrc Compiler Settings").Show();
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnGUI()
        {
            if (_settingsObject == null)
            {
                if (GUILayout.Button("Load Compiler Settings"))
                    LoadSettings();

                EditorGUILayout.HelpBox(
                    "Compiler settings not found. Create one by clicking the button above.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Tsvrc Compiler Validation Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawValidationLevelField("Pool Validation", _poolValidation,
                "How strictly to validate [TsWirePool] declarations against TsvrcConfig registrations.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Future Module Validation", EditorStyles.boldLabel);

            DrawValidationLevelField("Singleton Validation", _singletonValidation,
                "Validation strictness for singleton declarations (planned).");
            DrawValidationLevelField("Factory Validation", _factoryValidation,
                "Validation strictness for factory declarations (planned).");
            DrawValidationLevelField("Construct Validation", _constructValidation,
                "Validation strictness for construct declarations (planned).");

            EditorGUILayout.Space();

            if (GUILayout.Button("Create/Reset to Defaults", GUILayout.Height(30)))
            {
                CreateDefaultSettings();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Settings are stored at: Assets/Tsvrc/Resources/CompilerSettings.asset\n\n" +
                "Validation Levels:\n" +
                "• Error: Compilation fails on validation failure\n" +
                "• Warn: Logs warning but compilation continues\n" +
                "• Silent: Validation is skipped",
                MessageType.Info);

            if (_settingsObject.hasModifiedProperties)
            {
                _settingsObject.ApplyModifiedProperties();
            }
        }

        private void DrawValidationLevelField(string label, SerializedProperty property, string tooltip)
        {
            EditorGUILayout.PropertyField(property,
                new GUIContent(label, tooltip),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private void LoadSettings()
        {
            var settings = TsvrcCompilerSettings.Instance;
            if (settings == null)
            {
                Debug.LogWarning("[TsvrcCompilerSettingsWindow] Failed to load compiler settings.");
                return;
            }

            _settingsObject = new SerializedObject(settings);
            _poolValidation = _settingsObject.FindProperty("PoolValidation");
            _singletonValidation = _settingsObject.FindProperty("SingletonValidation");
            _factoryValidation = _settingsObject.FindProperty("FactoryValidation");
            _constructValidation = _settingsObject.FindProperty("ConstructValidation");
        }

        private void CreateDefaultSettings()
        {
            // Ensure directory exists
            string resourcesPath = "Assets/Tsvrc/Resources";
            if (!System.IO.Directory.Exists(resourcesPath))
            {
                System.IO.Directory.CreateDirectory(resourcesPath);
                AssetDatabase.Refresh();
            }

            // Create settings asset
            var settings = CreateInstance<TsvrcCompilerSettings>();
            string assetPath = "Assets/Tsvrc/Resources/CompilerSettings.asset";

            if (System.IO.File.Exists(assetPath))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Overwrite Existing Settings?",
                    "A CompilerSettings.asset already exists. Overwrite it?",
                    "Overwrite", "Cancel", "");

                if (choice != 0) return;

                var existing = AssetDatabase.LoadAssetAtPath<TsvrcCompilerSettings>(assetPath);
                DestroyImmediate(existing, true);
            }

            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LoadSettings();
            Debug.Log($"[TsvrcCompilerSettingsWindow] Compiler settings created at {assetPath}");
        }
    }
}
#endif
