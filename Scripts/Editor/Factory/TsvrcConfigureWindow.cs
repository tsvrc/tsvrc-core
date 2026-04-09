#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Unified configuration window for Tsvrc.
    /// Open via Tsvrc > Configure.
    /// </summary>
    internal class TsvrcConfigureWindow : EditorWindow
    {
        private enum Tab { Singletons, Pool, Constructs, Factories }

        private static readonly string[] TabLabels = { "Singletons", "Pool", "Constructs", "Factories" };

        private static readonly string[] TabDescriptions =
        {
            "Register any scene object or component as a named field on _ts. After compiling, access it from any TsvrcBehaviour via _ts.FieldName. Example: drag your GameManager here, then use _ts.GameManager from any behaviour.",
            "Register TsvrcProcess behaviours whose slots are placed in the scene before play. VRChat assigns each slot a stable network ID, enabling network events. Use _ts.GetType() to borrow a slot and call TsRelease() when done. Only TsvrcProcess subclasses are valid entries. Example: add DataTransferer here, then call _ts.GetDataTransferer() at runtime.",
            "Register TsvrcBehaviours that are always active in the scene, not pooled. TsConstruct() is called once on each at startup. Example: add your HudManager here and it is initialized automatically when the world loads.",
            "Register prefabs organized into named groups. Generates a Create{Group}{Name}(Transform parent) method for each entry. WARNING: instantiated objects do not receive a VRChat network ID and cannot send or receive network events. Use Pool for networked objects.",
        };

        private Tab _tab = Tab.Singletons;
        private Vector2 _scroll;
        private TsvrcConfig _config;
        private SerializedObject _so;
        private readonly Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();

        [MenuItem("Tsvrc/Configure")]
        private static void Open() => GetWindow<TsvrcConfigureWindow>("Tsvrc Configure").Show();

        private void OnEnable() => Reload();
        private void OnFocus() => Reload();

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
            var newTab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels);
            if (newTab != _tab) { _tab = newTab; _scroll = Vector2.zero; }

            // Description: fixed header, never scrolls, wraps to window width
            EditorGUILayout.LabelField(TabDescriptions[(int)_tab], EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            // Scrollable content area
            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTab(_tab);
            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();

            // Compile button: always pinned at bottom
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Compile Tsvrc"))
                TsvrcCompiler.Compile();
        }

        // ── No config ──────────────────────────────────────────────────────────

        private void DrawNoConfig()
        {
            EditorGUILayout.HelpBox("No TsvrcConfig found in the active scene.", MessageType.Info);
            if (GUILayout.Button("Add TsvrcConfig to Scene"))
                AddConfigToScene();
        }

        private void AddConfigToScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcCompiler.TsvrcConfigPrefabPath);
            GameObject go = prefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
                : new GameObject("TsvrcConfig");

            if (go.GetComponent<TsvrcConfig>() == null)
                go.AddComponent<TsvrcConfig>();

            go.tag = "EditorOnly";
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Reload();
        }

        // ── Tabs ───────────────────────────────────────────────────────────────

        private void DrawTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Singletons: DrawObjectArray("Singletons"); break;
                case Tab.Pool: DrawObjectArray("TsvrcProcessPool"); break;
                case Tab.Constructs: DrawObjectArray("TsvrcBehaviourConstruct"); break;
                case Tab.Factories: DrawFactories(); break;
            }
        }

        // Generic editable list: one object-field row per element + ✕ + Add button.
        private void DrawObjectArray(string propertyName)
        {
            var prop = _so.FindProperty(propertyName);

            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element, GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    // Two-step removal required for UnityEngine.Object arrays
                    element.objectReferenceValue = null;
                    prop.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add"))
                prop.InsertArrayElementAtIndex(prop.arraySize);
        }

        // ── Factories tab ──────────────────────────────────────────────────────

        private void DrawFactories()
        {
            var factoriesProp = _so.FindProperty("Factories");

            for (int i = 0; i < factoriesProp.arraySize; i++)
            {
                var groupProp = factoriesProp.GetArrayElementAtIndex(i);
                var groupNameProp = groupProp.FindPropertyRelative("GroupName");
                var prefabsProp = groupProp.FindPropertyRelative("Prefabs");

                // Use a stable key: group name if set, otherwise fallback to index.
                // This keeps foldout state when groups are reordered or unnamed groups are added.
                string groupName = groupNameProp.stringValue;
                int prefabCount = prefabsProp.arraySize;
                if (!_foldouts.ContainsKey(i))
                    _foldouts[i] = false;
                string foldoutLabel = string.IsNullOrEmpty(groupName)
                    ? $"(unnamed)   ({prefabCount} prefab{(prefabCount == 1 ? "" : "s")})"
                    : $"{groupName}   ({prefabCount} prefab{(prefabCount == 1 ? "" : "s")})";

                EditorGUILayout.BeginHorizontal();
                _foldouts[i] = EditorGUILayout.Foldout(_foldouts[i], foldoutLabel, true);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    factoriesProp.DeleteArrayElementAtIndex(i);
                    _so.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (_foldouts[i])
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.PropertyField(groupNameProp, new GUIContent("Group Name"));

                    string preview = string.IsNullOrWhiteSpace(groupName)
                        ? "Create…"
                        : $"Create{Sanitize(groupName)}…";
                    EditorGUILayout.LabelField($"Prefix:  {preview}", EditorStyles.miniLabel);

                    EditorGUILayout.PropertyField(prefabsProp, new GUIContent("Prefabs"), true);

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);
            }

            _so.ApplyModifiedProperties();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add Factory Group"))
            {
                factoriesProp.InsertArrayElementAtIndex(factoriesProp.arraySize);
                // Clear the newly added element so it doesn't copy the previous one
                var newGroup = factoriesProp.GetArrayElementAtIndex(factoriesProp.arraySize - 1);
                newGroup.FindPropertyRelative("GroupName").stringValue = string.Empty;
                newGroup.FindPropertyRelative("Prefabs").ClearArray();
                _so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            raw = raw.Trim();
            return raw.Length > 0 ? char.ToUpper(raw[0]) + raw.Substring(1) : string.Empty;
        }
    }
}
#endif
