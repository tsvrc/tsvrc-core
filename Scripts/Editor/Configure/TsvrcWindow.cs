#if UNITY_EDITOR
using Tsvrc.Core;
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
        private bool _isDirty;
        private int _baseUndoGroup;

        // Carry dirty state and undo baseline across a Cancel-reopen.
        // The window instance is destroyed by Unity before delayCall fires,
        // so these must be static to survive the gap.
        private static bool s_reopenDirty;
        private static int s_reopenUndoGroup;

        [MenuItem("Tsvrc/Configure")]
        private static void Open() => GetWindow<TsvrcWindow>("Tsvrc Configure").Show();

        private void OnEnable()
        {
            if (s_reopenDirty)
            {
                // Restore live references without touching the undo stack or dirty flag.
                // Calling Reload() here would add a spurious IncrementCurrentGroup() between
                // the saved baseline and the current undo position.
                _config = Object.FindObjectOfType<TsvrcConfig>();
                _so = _config != null ? new SerializedObject(_config) : null;
                _isDirty = true;
                _baseUndoGroup = s_reopenUndoGroup;
                s_reopenDirty = false;
            }
            else
            {
                Reload();
            }
        }

        private void OnDestroy()
        {
            if (!_isDirty) return;

            int choice = EditorUtility.DisplayDialogComplex(
                "Tsvrc, Unsaved Changes",
                "You have unsaved config changes. Apply them now or discard?",
                "Apply",    // 0
                "Discard",  // 1
                "Cancel"    // 2
            );

            if (choice == 0)
            {
                // Defer compile, calling AssetDatabase.Refresh() synchronously during OnDestroy is unsafe.
                EditorApplication.delayCall += () => TsvrcCompiler.Compile();
            }
            else if (choice == 1)
            {
                Undo.RevertAllDownToGroup(_baseUndoGroup);
            }
            else
            {
                // Cancel: preserve dirty state and undo baseline, then reopen.
                s_reopenDirty = true;
                s_reopenUndoGroup = _baseUndoGroup;
                EditorApplication.delayCall += Open;
            }
        }

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
            _isDirty = false;
            // Mark a boundary so Discard can revert exactly the edits made since this reload.
            Undo.IncrementCurrentGroup();
            _baseUndoGroup = Undo.GetCurrentGroup();
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

            EditorGUILayout.LabelField(Tabs[_tabIndex].Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            _so.Update();
            EditorGUI.BeginChangeCheck();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Tabs[_tabIndex].OnGUI(_so);
            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();
            if (EditorGUI.EndChangeCheck())
                _isDirty = true;

            EditorGUILayout.Space(8);
            if (_isDirty)
            {
                EditorGUILayout.HelpBox("Config changed, Apply to compile, or Discard to revert all changes.", MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply"))
                {
                    TsvrcCompiler.Compile();
                    Reload();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Discard"))
                {
                    Undo.RevertAllDownToGroup(_baseUndoGroup);
                    EditorApplication.delayCall += Reload;
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
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
