#if UNITY_EDITOR
using Tsvrc.Editor.V2;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Open via Tsvrc > Configure.
    internal class TsvrcWindow : EditorWindow
    {
        private static readonly WindowTab[] Tabs =
        {
            new SingletonsTab(),
            new PoolTab(),
            new ConstructsTab(),
            new FactoriesTab(),
        };

        private static readonly string[] TabLabels = { "Singletons", "Pool", "Constructs", "Factories" };

        private int _tabIndex;
        private Vector2 _scroll;
        private TsvrcConfig _config;
        private SerializedObject _so;

        [MenuItem("Tsvrc/Configure")]
        private static void Open() => GetWindow<TsvrcWindow>("Tsvrc Configure").Show();

        private void OnEnable() => Reload();

        private void OnFocus()
        {
            if (_config == null)
                Reload();
        }

        private void Reload()
        {
            _config = AssetDatabase.LoadAssetAtPath<TsvrcConfig>(ScaffoldModule.ConfigPath);
            _so = _config != null ? new SerializedObject(_config) : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Tsvrc Configure", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_config == null)
            {
                DrawNoConfig();
                return;
            }

            int newIndex = GUILayout.Toolbar(_tabIndex, TabLabels);
            if (newIndex != _tabIndex) { _tabIndex = newIndex; _scroll = Vector2.zero; }

            EditorGUILayout.LabelField(Tabs[_tabIndex].Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Tabs[_tabIndex].OnGUI(_so);
            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Generate Now"))
            {
                TsvrcGenerator.ManualGenerate();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawNoConfig()
        {
            EditorGUILayout.HelpBox(
                $"No config asset found at {ScaffoldModule.ConfigPath}.\nCreate one to configure this world.",
                MessageType.Info);
            if (GUILayout.Button("Create Config Asset"))
            {
                var config = ScriptableObject.CreateInstance<TsvrcConfig>();
                AssetDatabase.CreateAsset(config, ScaffoldModule.ConfigPath);
                AssetDatabase.SaveAssets();
                Reload();
            }
        }
    }
}
#endif
