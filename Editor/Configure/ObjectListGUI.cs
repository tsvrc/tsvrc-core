#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared tab-drawing helpers for modules whose config is a simple Object[] array
    // (Singletons, Pool, Constructs). Factories has its own DrawTab - its data is a
    // nested array-of-groups, not a flat object list - but still uses DeleteButton.
    internal static class ObjectListGUI
    {
        internal static bool DeleteButton() => GUILayout.Button("✕", GUILayout.Width(22));

        internal static void DrawObjectList(SerializedObject so, string propertyName)
        {
            var prop = so.FindProperty(propertyName);

            int toDelete = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element, GUIContent.none);
                if (DeleteButton())
                    toDelete = i;
                EditorGUILayout.EndHorizontal();
            }

            // Deletion deferred outside the draw loop to avoid index invalidation.
            if (toDelete >= 0)
            {
                // Two-step removal required for UnityEngine.Object arrays.
                prop.GetArrayElementAtIndex(toDelete).objectReferenceValue = null;
                prop.DeleteArrayElementAtIndex(toDelete);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add"))
                prop.InsertArrayElementAtIndex(prop.arraySize);
        }
    }
}
#endif
