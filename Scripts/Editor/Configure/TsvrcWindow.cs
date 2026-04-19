#if UNITY_EDITOR
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Unified Tsvrc configuration window.
    /// Open via Tsvrc > Configure.
    /// </summary>
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
            // Only reload when the config reference is lost (e.g. scene unloaded / switched).
            // Rebuilding a live SerializedObject on every focus event would discard any
            // in-progress text edits (e.g. typing a factory group name).
            if (_config == null)
                Reload();
        }

        private void Reload()
        {
            _config = Object.FindObjectOfType<TsvrcConfig>();
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

            // Tab bar
            int newIndex = GUILayout.Toolbar(_tabIndex, TabLabels);
            if (newIndex != _tabIndex) { _tabIndex = newIndex; _scroll = Vector2.zero; }

            // Description: fixed header, never scrolls, wraps to window width.
            EditorGUILayout.LabelField(Tabs[_tabIndex].Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            // Scrollable tab content
            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Tabs[_tabIndex].OnGUI(_so);
            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();

            // Compile button always pinned at the bottom.
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Compile Tsvrc"))
                TsvrcCompiler.Compile();
        }

        private void DrawNoConfig()
        {
            EditorGUILayout.HelpBox("No TsvrcConfig found in the active scene.", MessageType.Info);
            if (GUILayout.Button("Add TsvrcConfig to Scene"))
            {
                TsvrcCompiler.AddTsvrcConfigToScene();
                Reload();
            }
        }
    }
}
#endif
