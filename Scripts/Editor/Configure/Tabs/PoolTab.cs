#if UNITY_EDITOR
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal sealed class PoolTab : ObjectListTab
    {
        internal override string Description =>
            "Register UdonSharpBehaviour prefabs to pool. " +
            "The system automatically instantiates all slots, initializes them, and wires every [WirePool] field across your behaviours at compile time. " +
            "No manual scene placement, no cross-behaviour drag-and-drop, and no broken references when you refactor.";

        protected override string PropertyName => "PooledObjects";

        internal override void OnGUI(SerializedObject so)
        {
            var prop = so.FindProperty(PropertyName);

            int toDelete = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                var current = element.objectReferenceValue;

                EditorGUILayout.BeginHorizontal();

                var selected = EditorGUILayout.ObjectField(current, typeof(UdonSharpBehaviour), false);
                if (selected != current)
                {
                    // If a whole GameObject was dropped, auto-extract the UdonSharpBehaviour component.
                    if (selected is GameObject go)
                        selected = go.GetComponent<UdonSharpBehaviour>();
                    element.objectReferenceValue = selected;
                }

                if (GUILayout.Button("\u2715", GUILayout.Width(22)))
                    toDelete = i;

                EditorGUILayout.EndHorizontal();

                if (current != null && current is not UdonSharpBehaviour)
                    EditorGUILayout.HelpBox(
                        $"'{current.name}' is not an UdonSharpBehaviour. Remove it or replace with a valid UdonSharpBehaviour prefab.",
                        MessageType.Error);
            }

            // Deletion deferred outside the draw loop to avoid index invalidation.
            if (toDelete >= 0)
            {
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
