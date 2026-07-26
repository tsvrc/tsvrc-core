#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared tab-drawing helpers for any Object[]-backed list: Singletons/Pool/Constructs draw a
    // top-level property directly via the SerializedObject overload; FactoryModule's Prefabs array
    // is nested inside each group element, drawn via the SerializedProperty overload instead
    // (FactoryModule otherwise has its own DrawTab for the group/foldout structure around it).
    internal static class ObjectListGUI
    {
        internal static bool DeleteButton() => GUILayout.Button("✕", GUILayout.Width(22));

        // True for a scene instance (invalid wherever a prefab asset is required - e.g. Pool),
        // false for a persisted asset or null. Pure so it's directly unit-testable.
        internal static bool IsSceneInstance(Object obj) => obj != null && !EditorUtility.IsPersistent(obj);

        // True if obj is non-null and already in seen (added as a side effect otherwise). Pure
        // aside from the seen-set mutation, so it's testable without a real SerializedProperty/array.
        internal static bool IsDuplicate(Object obj, HashSet<Object> seen) => obj != null && !seen.Add(obj);

        // Convenience overload for a top-level Object[] property (Singletons, Pool, Constructs).
        internal static void DrawObjectList(SerializedObject so, string propertyName, string emptyHint = null,
            bool assetsOnly = false, bool warnDuplicates = false)
            => DrawObjectList(so.FindProperty(propertyName), emptyHint, assetsOnly, warnDuplicates);

        // emptyHint: shown in place of an empty list so a first-time user sees what adding an entry does.
        // assetsOnly: warns inline on a scene-object reference, the same mistake TsGenerator.Run()
        // already catches - surfaced here at the moment it's made instead of only after a regenerate.
        // warnDuplicates: warns inline on a repeated reference. Opt-in since duplicates are only a
        // mistake for modules that dedupe at generate time (Singletons, Constructs); Pool allows
        // repeated slots of the same prefab type.
        internal static void DrawObjectList(SerializedProperty prop, string emptyHint = null,
            bool assetsOnly = false, bool warnDuplicates = false)
        {
            if (prop.arraySize == 0 && !string.IsNullOrEmpty(emptyHint))
                TsEditorGUI.DrawStatusBox(emptyHint, MessageType.None);

            var seen = warnDuplicates ? new HashSet<Object>() : null;
            int toDelete = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(element, GUIContent.none);
                if (DeleteButton())
                    toDelete = i;
                EditorGUILayout.EndHorizontal();

                if (assetsOnly && IsSceneInstance(element.objectReferenceValue))
                    TsEditorGUI.DrawStatusBox(
                        $"'{element.objectReferenceValue.name}' is a scene object, not a prefab asset - drag one in from the Project window instead.",
                        MessageType.Warning);

                if (warnDuplicates && IsDuplicate(element.objectReferenceValue, seen))
                    TsEditorGUI.DrawStatusBox(
                        $"'{element.objectReferenceValue.name}' is already listed above - the duplicate will be dropped at regenerate.",
                        MessageType.Warning);
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
