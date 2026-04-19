#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal sealed class FactoriesTab : WindowTab
    {
        // Keyed by array index. Using int (not group name) means the key is stable while
        // the user is typing — the foldout never collapses mid-edit.
        private readonly Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();

        private static readonly GUIContent LabelGroupName = new GUIContent("Group Name");
        private static readonly GUIContent LabelPrefabs = new GUIContent("Prefabs");

        internal override string Description =>
            "Register prefabs organized into named groups. Generates a Create{Group}{Name}(Transform parent) method for each entry. WARNING: instantiated objects do not receive a VRChat network ID and cannot send or receive network events. Use Pool for networked objects.";

        internal override void OnGUI(SerializedObject so)
        {
            var factoriesProp = so.FindProperty("Factories");

            int toDelete = -1;
            for (int i = 0; i < factoriesProp.arraySize; i++)
            {
                var groupProp = factoriesProp.GetArrayElementAtIndex(i);
                var groupNameProp = groupProp.FindPropertyRelative("GroupName");
                var prefabsProp = groupProp.FindPropertyRelative("Prefabs");

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
                    toDelete = i;
                EditorGUILayout.EndHorizontal();

                if (_foldouts[i])
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(groupNameProp, LabelGroupName);
                    string preview = string.IsNullOrWhiteSpace(groupName)
                        ? "Create…"
                        : $"Create{FactoryModule.Sanitize(groupName)}…";
                    EditorGUILayout.LabelField($"Prefix:  {preview}", EditorStyles.miniLabel);
                    EditorGUILayout.PropertyField(prefabsProp, LabelPrefabs, true);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);
            }

            // Deletion deferred outside the draw loop to avoid index invalidation.
            if (toDelete >= 0)
            {
                factoriesProp.DeleteArrayElementAtIndex(toDelete);
                ShiftFoldoutsAfterDelete(toDelete);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add Factory Group"))
            {
                int newIndex = factoriesProp.arraySize;
                factoriesProp.InsertArrayElementAtIndex(newIndex);
                var newGroup = factoriesProp.GetArrayElementAtIndex(newIndex);
                newGroup.FindPropertyRelative("GroupName").stringValue = string.Empty;
                newGroup.FindPropertyRelative("Prefabs").ClearArray();
                // Auto-expand the new group so the user can immediately name it.
                _foldouts[newIndex] = true;
            }
        }

        private void ShiftFoldoutsAfterDelete(int deletedIndex)
        {
            _foldouts.Remove(deletedIndex);
            // Shift all entries above the deleted index down by one.
            var keys = new List<int>(_foldouts.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                if (key > deletedIndex)
                {
                    bool val = _foldouts[key];
                    _foldouts.Remove(key);
                    _foldouts[key - 1] = val;
                }
            }
        }
    }
}
#endif
