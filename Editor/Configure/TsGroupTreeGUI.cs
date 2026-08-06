#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Master-detail widget for a TsGroup[]/TsGroupedEntry[] pair: left pane is a searchable
    // TreeView of nested groups, right pane is the selected group's direct contents, drawn via
    // ObjectListGUI's row rendering.
    internal static class TsGroupTreeGUI
    {
        // Per-caller UI state (tree expand/select/search), never written to TsConfig.
        internal class State
        {
            internal TreeViewState TreeViewState = new TreeViewState();

            // Lazy because SearchField's constructor calls GUIUtility.GetPermanentControlID,
            // which Unity forbids during a ScriptableObject/Editor's constructor or field
            // initializers.
            private SearchField _searchField;
            internal SearchField SearchField => _searchField ?? (_searchField = new SearchField());

            internal GroupTreeView TreeView;
            internal int SelectedGroupId; // 0 = the always-present "(ungrouped)" bucket
            internal string SearchText = string.Empty;
            internal bool RenamePending;
        }

        internal static void Draw(SerializedObject so, string groupsPropertyName, string entriesPropertyName,
            State state, string emptyHint = null, bool assetsOnly = false, bool warnDuplicates = false)
        {
            var groupsProp = so.FindProperty(groupsPropertyName);
            var entriesProp = so.FindProperty(entriesPropertyName);

            if (state.TreeView == null)
                state.TreeView = new GroupTreeView(state.TreeViewState, groupsProp, entriesProp);
            else
                state.TreeView.SetProperties(groupsProp, entriesProp);

            EditorGUILayout.BeginHorizontal();
            string newSearch = state.SearchField.OnToolbarGUI(state.SearchText, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            if (newSearch != state.SearchText)
            {
                state.SearchText = newSearch;
                state.TreeView.searchString = newSearch;
            }

            state.TreeView.Reload();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            var treeRect = GUILayoutUtility.GetRect(200, 220, GUILayout.ExpandHeight(true));
            state.TreeView.OnGUI(treeRect);
            if (state.TreeViewState.selectedIDs.Count > 0)
                state.SelectedGroupId = state.TreeViewState.selectedIDs[0];
            DrawGroupToolbar(groupsProp, entriesProp, state);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DrawContents(entriesProp, groupsProp, state, emptyHint, assetsOnly, warnDuplicates);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // Deleting a group never deletes its contents: child groups and entries are reparented
        // one level up, only the group node itself is removed.
        private static void DrawGroupToolbar(SerializedProperty groupsProp, SerializedProperty entriesProp, State state)
        {
            bool realGroupSelected = state.SelectedGroupId != 0;

            EditorGUILayout.BeginHorizontal();
            string addLabel = realGroupSelected ? "+ Sub-group" : "+ Add Group";
            if (GUILayout.Button(addLabel))
                AddGroup(groupsProp, state, parentId: realGroupSelected ? state.SelectedGroupId : 0);
            using (new EditorGUI.DisabledScope(!realGroupSelected))
            {
                if (GUILayout.Button("Delete", GUILayout.Width(56)))
                    DeleteGroup(groupsProp, entriesProp, state);
            }
            EditorGUILayout.EndHorizontal();

            if (realGroupSelected)
                DrawRenameField(groupsProp, state);
        }

        internal static void AddGroup(SerializedProperty groupsProp, State state, int parentId)
        {
            var so = groupsProp.serializedObject;
            var nextIdProp = FindNextGroupIdProperty(so, groupsProp);
            int newId = nextIdProp != null ? nextIdProp.intValue : NextFreeId(groupsProp);
            if (nextIdProp != null) nextIdProp.intValue = newId + 1;

            int index = groupsProp.arraySize;
            groupsProp.InsertArrayElementAtIndex(index);
            var element = groupsProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("Id").intValue = newId;
            element.FindPropertyRelative("ParentId").intValue = parentId;
            element.FindPropertyRelative("Name").stringValue = "New Group";

            state.SelectedGroupId = newId;
            state.RenamePending = true;
        }

        // Every *Groups field has a sibling *NextGroupId counter, named by convention.
        private static SerializedProperty FindNextGroupIdProperty(SerializedObject so, SerializedProperty groupsProp)
        {
            string groupsFieldName = groupsProp.name;
            string prefix = groupsFieldName.EndsWith("Groups")
                ? groupsFieldName.Substring(0, groupsFieldName.Length - "Groups".Length)
                : groupsFieldName;
            return so.FindProperty(prefix + "NextGroupId");
        }

        // Only reached if the sibling *NextGroupId property is missing.
        private static int NextFreeId(SerializedProperty groupsProp)
        {
            int max = 0;
            for (int i = 0; i < groupsProp.arraySize; i++)
                max = Mathf.Max(max, groupsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue);
            return max + 1;
        }

        private static void DeleteGroup(SerializedProperty groupsProp, SerializedProperty entriesProp, State state)
        {
            int deletedId = state.SelectedGroupId;
            if (!GroupHasContents(groupsProp, entriesProp, deletedId, out bool hasContents)) return;

            if (hasContents && !EditorUtility.DisplayDialog(
                    "Delete non-empty group?",
                    "This group contains sub-groups or objects. They will be moved up to this group's " +
                    "parent, not deleted. Continue?",
                    "Delete Group", "Cancel"))
                return;

            state.SelectedGroupId = ReparentContentsAndRemoveGroup(groupsProp, entriesProp, deletedId);
        }

        // Returns false if groupId no longer exists. hasContents is true if it has any direct
        // child group or entry. Kept separate from ReparentContentsAndRemoveGroup so both can be
        // unit-tested without going through EditorUtility.DisplayDialog.
        internal static bool GroupHasContents(SerializedProperty groupsProp, SerializedProperty entriesProp, int groupId, out bool hasContents)
        {
            hasContents = false;
            if (IndexOfGroup(groupsProp, groupId) < 0) return false;

            for (int i = 0; i < groupsProp.arraySize; i++)
                if (groupsProp.GetArrayElementAtIndex(i).FindPropertyRelative("ParentId").intValue == groupId)
                    hasContents = true;
            for (int i = 0; i < entriesProp.arraySize; i++)
                if (entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("GroupId").intValue == groupId)
                    hasContents = true;
            return true;
        }

        // Reparents every direct child group and entry to groupId's own parent, then removes
        // groupId. Returns the parent id contents were moved to, so selection can follow them.
        internal static int ReparentContentsAndRemoveGroup(SerializedProperty groupsProp, SerializedProperty entriesProp, int groupId)
        {
            int index = IndexOfGroup(groupsProp, groupId);
            int parentId = groupsProp.GetArrayElementAtIndex(index).FindPropertyRelative("ParentId").intValue;

            for (int i = 0; i < groupsProp.arraySize; i++)
            {
                var childParent = groupsProp.GetArrayElementAtIndex(i).FindPropertyRelative("ParentId");
                if (childParent.intValue == groupId) childParent.intValue = parentId;
            }
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var groupIdProp = entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("GroupId");
                if (groupIdProp.intValue == groupId) groupIdProp.intValue = parentId;
            }

            index = IndexOfGroup(groupsProp, groupId);
            groupsProp.DeleteArrayElementAtIndex(index);
            return parentId;
        }

        internal static int IndexOfGroup(SerializedProperty groupsProp, int id)
        {
            for (int i = 0; i < groupsProp.arraySize; i++)
                if (groupsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue == id)
                    return i;
            return -1;
        }

        // A delayed text field rather than TreeView's built-in double-click rename overlay, which
        // needs per-row event plumbing for the same result. Auto-focused right after a new group
        // is created.
        private static void DrawRenameField(SerializedProperty groupsProp, State state)
        {
            int index = IndexOfGroup(groupsProp, state.SelectedGroupId);
            if (index < 0) return;

            var nameProp = groupsProp.GetArrayElementAtIndex(index).FindPropertyRelative("Name");
            GUI.SetNextControlName("TsGroupRename");
            string committed = EditorGUILayout.DelayedTextField(nameProp.stringValue);
            if (committed != nameProp.stringValue)
                nameProp.stringValue = committed;

            if (state.RenamePending)
            {
                EditorGUI.FocusTextInControl("TsGroupRename");
                state.RenamePending = false;
            }
        }

        // Right pane: entries belonging to the selected group. While searching, widens to the
        // selected group's entire subtree so a broad search from a parent group surfaces
        // everything nested under it.
        private static void DrawContents(SerializedProperty entriesProp, SerializedProperty groupsProp, State state,
            string emptyHint, bool assetsOnly, bool warnDuplicates)
        {
            bool searching = !string.IsNullOrEmpty(state.SearchText);
            var allowedGroupIds = searching
                ? GroupTreeView.CollectSubtreeIds(groupsProp, state.SelectedGroupId)
                : new HashSet<int> { state.SelectedGroupId };

            var indices = new List<int>();
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var entry = entriesProp.GetArrayElementAtIndex(i);
                if (!allowedGroupIds.Contains(entry.FindPropertyRelative("GroupId").intValue)) continue;
                if (searching)
                {
                    var value = entry.FindPropertyRelative("Value").objectReferenceValue;
                    if (value == null || value.name.IndexOf(state.SearchText, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                indices.Add(i);
            }

            if (indices.Count == 0 && !string.IsNullOrEmpty(emptyHint))
                TsEditorGUI.DrawStatusBox(emptyHint, MessageType.None);

            var seen = warnDuplicates ? new HashSet<Object>() : null;
            int toDelete = -1;
            foreach (int i in indices)
            {
                var entry = entriesProp.GetArrayElementAtIndex(i);
                var valueProp = entry.FindPropertyRelative("Value");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(valueProp, GUIContent.none);
                if (ObjectListGUI.DeleteButton())
                    toDelete = i;
                EditorGUILayout.EndHorizontal();

                ObjectListGUI.DrawEntryHints(valueProp.objectReferenceValue, assetsOnly, warnDuplicates, seen);
            }

            if (toDelete >= 0)
            {
                entriesProp.GetArrayElementAtIndex(toDelete).FindPropertyRelative("Value").objectReferenceValue = null;
                entriesProp.DeleteArrayElementAtIndex(toDelete);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(searching))
            if (GUILayout.Button("+ Add"))
            {
                int newIndex = entriesProp.arraySize;
                entriesProp.InsertArrayElementAtIndex(newIndex);
                var element = entriesProp.GetArrayElementAtIndex(newIndex);
                element.FindPropertyRelative("Value").objectReferenceValue = null;
                element.FindPropertyRelative("GroupId").intValue = state.SelectedGroupId;
            }
        }

        // Left-pane tree. Rebuilt from the live SerializedProperty data on every Reload() rather
        // than cached, so it stays correct across undo/redo and external edits.
        internal class GroupTreeView : TreeView
        {
            private SerializedProperty _groupsProp;
            private SerializedProperty _entriesProp;

            internal GroupTreeView(TreeViewState state, SerializedProperty groupsProp, SerializedProperty entriesProp)
                : base(state)
            {
                SetProperties(groupsProp, entriesProp);
                showAlternatingRowBackgrounds = true;
                Reload();
            }

            internal void SetProperties(SerializedProperty groupsProp, SerializedProperty entriesProp)
            {
                _groupsProp = groupsProp;
                _entriesProp = entriesProp;
            }

            protected override TreeViewItem BuildRoot() => new TreeViewItem { id = -1, depth = -1, displayName = "root" };

            protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
            {
                var groups = ReadGroups(_groupsProp);
                var entryCounts = CountEntriesByGroup(_entriesProp);
                var childrenOf = groups.Values.GroupBy(g => g.ParentId).ToDictionary(g => g.Key, g => g.ToList());

                bool searching = !string.IsNullOrEmpty(searchString);
                var subtreeMatch = searching ? ComputeSubtreeMatches(groups, childrenOf, entryCounts, _entriesProp) : null;

                var rows = new List<TreeViewItem>();
                if (!searching || (subtreeMatch != null && subtreeMatch.TryGetValue(0, out bool m0) && m0))
                    rows.Add(new TreeViewItem(0, 0, DisplayName("(ungrouped)", entryCounts, 0)));

                AddChildren(0, 0, groups, childrenOf, entryCounts, subtreeMatch, rows);

                if (rows.Count == 0)
                    rows.Add(new TreeViewItem(0, 0, "(ungrouped)"));

                SetupParentsAndChildrenFromDepths(root, rows);
                return rows;
            }

            private static void AddChildren(int parentId, int depth, Dictionary<int, GroupInfo> groups,
                Dictionary<int, List<GroupInfo>> childrenOf, Dictionary<int, int> entryCounts,
                Dictionary<int, bool> subtreeMatch, List<TreeViewItem> rows)
            {
                if (!childrenOf.TryGetValue(parentId, out var children)) return;
                foreach (var group in children.OrderBy(g => g.Name, System.StringComparer.OrdinalIgnoreCase))
                {
                    if (subtreeMatch != null && !(subtreeMatch.TryGetValue(group.Id, out bool m) && m)) continue;
                    string name = string.IsNullOrEmpty(group.Name) ? "(unnamed)" : group.Name;
                    rows.Add(new TreeViewItem(group.Id, depth, DisplayName(name, entryCounts, group.Id)));
                    AddChildren(group.Id, depth + 1, groups, childrenOf, entryCounts, subtreeMatch, rows);
                }
            }

            private static string DisplayName(string name, Dictionary<int, int> entryCounts, int groupId)
            {
                int count = entryCounts.TryGetValue(groupId, out int c) ? c : 0;
                return count > 0 ? $"{name} ({count})" : name;
            }

            // A group matches if its own name matches, one of its direct entries matches, or any
            // child's subtree matches. Computed bottom-up so a match anywhere in a subtree keeps
            // every ancestor visible.
            private Dictionary<int, bool> ComputeSubtreeMatches(Dictionary<int, GroupInfo> groups,
                Dictionary<int, List<GroupInfo>> childrenOf, Dictionary<int, int> entryCounts, SerializedProperty entriesProp)
            {
                var result = new Dictionary<int, bool>();
                var entryNamesByGroup = new Dictionary<int, List<string>>();
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var entry = entriesProp.GetArrayElementAtIndex(i);
                    int groupId = entry.FindPropertyRelative("GroupId").intValue;
                    var value = entry.FindPropertyRelative("Value").objectReferenceValue;
                    if (!entryNamesByGroup.TryGetValue(groupId, out var list))
                        entryNamesByGroup[groupId] = list = new List<string>();
                    if (value != null) list.Add(value.name);
                }

                bool Matches(int id, string name)
                {
                    if (result.TryGetValue(id, out bool cached)) return cached;
                    bool self = !string.IsNullOrEmpty(name) && name.IndexOf(searchString, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    bool ownEntry = entryNamesByGroup.TryGetValue(id, out var names) &&
                        names.Any(n => n.IndexOf(searchString, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    bool childMatch = childrenOf.TryGetValue(id, out var children) &&
                        children.Any(c => Matches(c.Id, c.Name));
                    bool value = self || ownEntry || childMatch;
                    result[id] = value;
                    return value;
                }

                Matches(0, "(ungrouped)");
                foreach (var group in groups.Values)
                    Matches(group.Id, group.Name);

                return result;
            }

            // Every group id reachable from startId, itself plus every descendant.
            internal static HashSet<int> CollectSubtreeIds(SerializedProperty groupsProp, int startId)
            {
                var groups = ReadGroups(groupsProp);
                var childrenOf = groups.Values.GroupBy(g => g.ParentId).ToDictionary(g => g.Key, g => g.ToList());
                var result = new HashSet<int> { startId };
                var stack = new Stack<int>();
                stack.Push(startId);
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    if (!childrenOf.TryGetValue(id, out var children)) continue;
                    foreach (var child in children)
                        if (result.Add(child.Id))
                            stack.Push(child.Id);
                }
                return result;
            }

            private static Dictionary<int, GroupInfo> ReadGroups(SerializedProperty groupsProp)
            {
                var result = new Dictionary<int, GroupInfo>();
                if (groupsProp == null) return result;
                for (int i = 0; i < groupsProp.arraySize; i++)
                {
                    var element = groupsProp.GetArrayElementAtIndex(i);
                    var info = new GroupInfo
                    {
                        Id = element.FindPropertyRelative("Id").intValue,
                        ParentId = element.FindPropertyRelative("ParentId").intValue,
                        Name = element.FindPropertyRelative("Name").stringValue,
                    };
                    result[info.Id] = info;
                }
                return result;
            }

            private static Dictionary<int, int> CountEntriesByGroup(SerializedProperty entriesProp)
            {
                var result = new Dictionary<int, int>();
                if (entriesProp == null) return result;
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    int groupId = entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("GroupId").intValue;
                    result[groupId] = result.TryGetValue(groupId, out int c) ? c + 1 : 1;
                }
                return result;
            }

            // Dropping a group onto its own descendant is rejected here; BreakGroupCycles is the
            // defensive backstop for a cycle that reaches serialized data some other way.
            protected override bool CanStartDrag(CanStartDragArgs args) => args.draggedItem.id != 0;

            protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("TsGroupTreeDragId", args.draggedItemIDs[0]);
                DragAndDrop.StartDrag("Ts Group");
            }

            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
            {
                if (!(DragAndDrop.GetGenericData("TsGroupTreeDragId") is int draggedId)) return DragAndDropVisualMode.None;
                int targetId = args.parentItem?.id ?? 0;
                if (draggedId == targetId) return DragAndDropVisualMode.None;
                if (CollectSubtreeIds(_groupsProp, draggedId).Contains(targetId)) return DragAndDropVisualMode.None;

                if (args.performDrop)
                {
                    int index = IndexOfGroup(_groupsProp, draggedId);
                    if (index >= 0)
                        _groupsProp.GetArrayElementAtIndex(index).FindPropertyRelative("ParentId").intValue = targetId;
                    _groupsProp.serializedObject.ApplyModifiedProperties();
                }
                return DragAndDropVisualMode.Move;
            }

            private struct GroupInfo
            {
                internal int Id;
                internal int ParentId;
                internal string Name;
            }
        }
    }
}
#endif
