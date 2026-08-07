#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared tab-drawing helpers for an Object[]-backed list. Every grouped module (Globals,
    // Pool, Constructs, Factories) draws its entries through TsGroupTreeGUI instead, which reuses
    // this class's row-rendering helpers (DeleteButton, DrawEntryHints) internally rather than
    // the top-level DrawObjectList entry points below - those remain available for any future
    // non-grouped Object[]-backed tab.
    internal static class ObjectListGUI
    {
        internal static bool DeleteButton() => GUILayout.Button("✕", GUILayout.Width(22));

        // True for a scene instance (invalid wherever a prefab asset is required - e.g. Pool),
        // false for a persisted asset or null. Pure so it's directly unit-testable.
        internal static bool IsSceneInstance(Object obj) => obj != null && !EditorUtility.IsPersistent(obj);

        // True if obj is non-null and already in seen (added as a side effect otherwise). Pure
        // aside from the seen-set mutation, so it's testable without a real SerializedProperty/array.
        internal static bool IsDuplicate(Object obj, HashSet<Object> seen) => obj != null && !seen.Add(obj);

        // Unity's PropertyField widget can only display a component's real type while the
        // project compiles - a "Missing (Mono Script)" reference always renders as a blank type,
        // with no way to tell what it points at. Resolves it anyway via
        // TsModule.TryResolveObjectType, the same ScriptIndex-backed fallback Wire()/GenerateCode()
        // already use. No-op when the live type already resolves fine on its own.
        private static void DrawResolvedTypeHint(Object obj)
        {
            if (!(obj is Component component)) return;
            if (component.GetType() != typeof(MonoBehaviour)) return;

            string hint = TsModule.TryResolveObjectType(obj, out string typeName, out _)
                ? $"detected as {typeName} (compile currently broken)"
                : "could not detect a type for this reference (compile currently broken)";
            EditorGUILayout.LabelField("   ↳ " + hint, EditorStyles.miniLabel);
        }

        // A null slot renders as a bare, empty ObjectField with no other signal. Nothing
        // distinguishes "never filled in" from "used to point at something that was deleted",
        // since SerializedProperty carries no history either way, so this can only flag that the
        // slot needs attention, not which case it is. The Console already logs a specific
        // "Null entry..." warning at regenerate time (TsModule.TryAcceptEntry); this surfaces the
        // same fact inline, while the user is already looking at the list.
        private static void DrawNullSlotHint(Object obj)
        {
            if (obj != null) return;
            EditorGUILayout.LabelField(
                "   ↳ empty - nothing assigned here (if this used to point at something, it may have been deleted)",
                EditorStyles.miniLabel);
        }

        // The per-row hint/warning block shared by DrawObjectList and TsGroupTreeGUI's own
        // grouped-entry rows: resolved-type hint, empty-slot hint, scene-instance warning
        // (assetsOnly), duplicate-reference warning (warnDuplicates, seen mutated as a side
        // effect exactly like DrawObjectList's own loop).
        internal static void DrawEntryHints(Object obj, bool assetsOnly, bool warnDuplicates, HashSet<Object> seen)
        {
            DrawResolvedTypeHint(obj);
            DrawNullSlotHint(obj);

            if (assetsOnly && IsSceneInstance(obj))
                TsEditorGUI.DrawStatusBox(
                    $"'{obj.name}' is a scene object, not a prefab asset - drag one in from the Project window instead.",
                    MessageType.Warning);

            if (warnDuplicates && IsDuplicate(obj, seen))
                TsEditorGUI.DrawStatusBox(
                    $"'{obj.name}' is already listed above - the duplicate will be dropped at regenerate.",
                    MessageType.Warning);
        }

        // Convenience overload for a top-level, non-grouped Object[] property.
        internal static void DrawObjectList(SerializedObject so, string propertyName, string emptyHint = null,
            bool assetsOnly = false, bool warnDuplicates = false)
            => DrawObjectList(so.FindProperty(propertyName), emptyHint, assetsOnly, warnDuplicates);

        // emptyHint: shown in place of an empty list so a first-time user sees what adding an entry does.
        // assetsOnly: warns inline on a scene-object reference, the same mistake TsGenerator.Run()
        // already catches - surfaced here at the moment it's made instead of only after a regenerate.
        // warnDuplicates: warns inline on a repeated reference. Opt-in since duplicates are only a
        // mistake for modules that dedupe at generate time (Globals, Constructs); Pool allows
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

                DrawEntryHints(element.objectReferenceValue, assetsOnly, warnDuplicates, seen);
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
            {
                // InsertArrayElementAtIndex on an Object[] array copies the last element's
                // reference into the new slot instead of leaving it empty (a well known
                // SerializedProperty quirk for reference-type arrays) - cleared explicitly so
                // "+ Add" always adds a genuinely empty slot, matching what FactoryModule's own
                // "+ Add Factory Group" button already does for its own array.
                int newIndex = prop.arraySize;
                prop.InsertArrayElementAtIndex(newIndex);
                prop.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
            }
        }
    }
}
#endif
