#if UNITY_EDITOR
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Editor window for managing Tsvrc factory groups.
    /// Open via Tsvrc > Factory Groups.
    ///
    /// Each group lives as a child GameObject under the scene's TsvrcConfig and holds a
    /// named collection of prefabs. The compiler generates Create{GroupName}{PrefabName}(Transform)
    /// methods on CompiledTsvrc for every prefab registered here.
    /// </summary>
    internal class TsvrcFactoryWindow : EditorWindow
    {
        private const string NetworkingWarning =
            "Runtime-instantiated objects are NOT assigned a VRChat network ID.\n" +
            "They cannot send or receive VRC network events " +
            "(OnDeserialization, SendCustomNetworkEvent, OnPlayerJoined, etc.).\n\n" +
            "For networked objects, use the Pool instead — pool slots exist in the scene " +
            "before play and receive stable network IDs from VRChat.";

        private Vector2 _scroll;

        [MenuItem("Tsvrc/Factory Groups")]
        private static void Open() => GetWindow<TsvrcFactoryWindow>("Tsvrc Factories").Show();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Factory Groups", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(NetworkingWarning, MessageType.Warning);
            EditorGUILayout.Space(6);

            var config = FindConfig();
            if (config == null)
            {
                EditorGUILayout.HelpBox("No TsvrcConfig found in the scene. Open a scene that contains TsvrcConfig.", MessageType.Info);
                return;
            }

            var groups = config.GetComponentsInChildren<TsvrcFactoryGroup>();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in groups)
                DrawGroup(group);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add Factory Group"))
                AddGroup(config);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Compile Tsvrc"))
                TsvrcCompiler.Compile();
        }

        private void DrawGroup(TsvrcFactoryGroup group)
        {
            var so = new SerializedObject(group);
            so.Update();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row: group name field + delete button.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Group Name", GUILayout.Width(84));
            EditorGUILayout.PropertyField(so.FindProperty("GroupName"), GUIContent.none);
            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                so.ApplyModifiedProperties();
                Undo.DestroyObjectImmediate(group.gameObject);
                GUIUtility.ExitGUI();
                return;
            }
            EditorGUILayout.EndHorizontal();

            // Preview of generated method prefix.
            string prefix = string.IsNullOrWhiteSpace(group.GroupName) ? "(no prefix)" : $"Create{Sanitize(group.GroupName)}…";
            EditorGUILayout.LabelField($"Generated prefix:  {prefix}", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Prefab array.
            EditorGUILayout.PropertyField(so.FindProperty("Prefabs"), new GUIContent("Prefabs"), true);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);

            so.ApplyModifiedProperties();
        }

        private static void AddGroup(TsvrcConfig config)
        {
            var go = new GameObject("FactoryGroup");
            go.transform.SetParent(config.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Add Factory Group");
            go.AddComponent<TsvrcFactoryGroup>();
        }

        private static TsvrcConfig FindConfig()
            => Object.FindObjectOfType<TsvrcConfig>();

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            raw = raw.Trim();
            return raw.Length > 0 ? char.ToUpper(raw[0]) + raw.Substring(1) : string.Empty;
        }
    }
}
#endif
