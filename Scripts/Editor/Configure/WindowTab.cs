#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Base class for all tabs rendered inside <see cref="TsvrcWindow"/>.
    /// </summary>
    internal abstract class WindowTab
    {
        internal abstract string Description { get; }
        internal abstract void OnGUI(SerializedObject so);
    }

    /// <summary>
    /// Shared implementation for the three simple object-reference list tabs
    /// (Singletons, Pool, Constructs). Subclasses only need to supply the
    /// SerializedObject property name and display strings.
    /// </summary>
    internal abstract class ObjectListTab : WindowTab
    {
        protected abstract string PropertyName { get; }

        internal override void OnGUI(SerializedObject so)
        {
            var prop = so.FindProperty(PropertyName);

            int toDelete = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element, GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
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
