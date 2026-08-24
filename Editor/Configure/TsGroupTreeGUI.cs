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

            // Lazy: SearchField's constructor calls GUIUtility.GetPermanentControlID, which
            // Unity forbids during a ScriptableObject/Editor's constructor or field initializers.
            private SearchField _searchField;
            internal SearchField SearchField => _searchField ?? (_searchField = new SearchField());

            internal GroupTreeView TreeView;
            internal int SelectedGroupId; // 0 = the always-present "(ungrouped)" bucket
            internal string SearchText = string.Empty;

            // Group whose rename field should claim focus, or null once it has. Re-requested
            // every repaint (not consumed on the first) so a same-frame focus steal - e.g. the
            // TreeView's own selection sync - can't drop an in-progress rename. See DrawRenameField.
            internal int? PendingRenameGroupId;

            // Keys SelectedGroupId/SearchText into SessionState so they survive a domain reload.
            // Set once, the first time this State is drawn.
            internal string SessionKey;

            // Right pane: paginated, EntriesPerPage entries per page. Page resets to 0 whenever
            // the selected group or search text changes. See DrawContents.
            internal int EntriesPage;
            internal int LastEntriesPageGroupId = int.MinValue;
            internal string LastEntriesPageSearchText;

            // Signature of the data the tree renders from, so Reload() only runs when it actually
            // changed. Null until the first draw.
            internal int? LastTreeSignature;
        }

        // memberPrefix/memberSuffix enable the per-entry Name field and a live member preview:
        // "_ts." (Global/Construct) or "Create"/"(parent)" (Factory). Null (Pool) hides both.
        // prefixRespectsToggle mirrors BuildGroupPrefix: true honors each group's IncludeInName,
        // false always prefixes (Factory).
        internal static void Draw(SerializedObject so, string groupsPropertyName, string entriesPropertyName,
            State state, string emptyHint = null, bool assetsOnly = false, bool warnDuplicates = false,
            bool groupNaming = false, string memberPrefix = null, string memberSuffix = null,
            bool prefixRespectsToggle = true)
        {
            var groupsProp = so.FindProperty(groupsPropertyName);
            var entriesProp = so.FindProperty(entriesPropertyName);

            // Distinguishes TsWindow's TsConfig-backed trees from TsBuiltinConfigInspector's
            // TsBuiltinConfig-backed ones, which otherwise share property names ("GlobalGroups"
            // etc) and would collide on the same SessionState key.
            if (state.SessionKey == null)
            {
                state.SessionKey = ComputeSessionKey(so.targetObject.GetType().Name, groupsPropertyName);
                state.SelectedGroupId = SessionState.GetInt(state.SessionKey + ".Selected", 0);
                state.SearchText = SessionState.GetString(state.SessionKey + ".Search", string.Empty);
            }

            if (state.TreeView == null)
            {
                state.TreeView = new GroupTreeView(state.TreeViewState, groupsProp, entriesProp)
                {
                    searchString = state.SearchText,
                };
            }
            else
            {
                state.TreeView.SetProperties(groupsProp, entriesProp);
            }

            EditorGUILayout.BeginHorizontal();
            string newSearch = state.SearchField.OnToolbarGUI(state.SearchText, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            if (newSearch != state.SearchText)
            {
                state.SearchText = newSearch;
                state.TreeView.searchString = newSearch;
                SessionState.SetString(state.SessionKey + ".Search", newSearch);
            }

            int signature = ComputeTreeSignature(groupsProp, entriesProp, state.SearchText);
            if (state.LastTreeSignature != signature)
            {
                // Capture/restore expand state around Reload() so a structural edit
                // (rename/add/delete/reparent) doesn't collapse the whole tree.
                var expandedIds = state.TreeView.GetExpanded();
                state.TreeView.Reload();
                state.TreeView.SetExpanded(expandedIds);
                state.LastTreeSignature = signature;
            }

            EditorGUILayout.BeginHorizontal();

            // Toolbar/rename field drawn above the tree, not below: the tree reserves
            // ExpandHeight(true), so anything below it is only reachable by scrolling past it -
            // which could hide a freshly created group's auto-focused rename field entirely. A
            // "Groups" header plus a rule keeps the toolbar visually distinct from the tree rows.
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            DrawGroupToolbar(groupsProp, entriesProp, state, groupNaming);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
            var treeRect = GUILayoutUtility.GetRect(200, 220, GUILayout.ExpandHeight(true));
            state.TreeView.OnGUI(treeRect);
            if (state.TreeViewState.selectedIDs.Count > 0 &&
                state.TreeViewState.selectedIDs[0] != state.SelectedGroupId)
            {
                state.SelectedGroupId = state.TreeViewState.selectedIDs[0];
                SessionState.SetInt(state.SessionKey + ".Selected", state.SelectedGroupId);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DrawContents(entriesProp, groupsProp, state, emptyHint, assetsOnly, warnDuplicates,
                memberPrefix, memberSuffix, prefixRespectsToggle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // Deleting a group never deletes its contents: child groups and entries are reparented
        // one level up, only the group node itself is removed.
        private static void DrawGroupToolbar(SerializedProperty groupsProp, SerializedProperty entriesProp, State state,
            bool groupNaming)
        {
            bool realGroupSelected = state.SelectedGroupId != 0;

            EditorGUILayout.BeginHorizontal();
            // Always available, so adding a top-level group never requires selecting the ungrouped
            // bucket first. Sub-group and Delete act on the selected group.
            if (GUILayout.Button("+ Group"))
                AddGroup(groupsProp, state, parentId: 0);
            using (new EditorGUI.DisabledScope(!realGroupSelected))
            {
                if (GUILayout.Button("+ Sub-group"))
                    AddGroup(groupsProp, state, parentId: state.SelectedGroupId);
                if (GUILayout.Button("Delete", GUILayout.Width(56)))
                    DeleteGroup(groupsProp, entriesProp, state);
            }
            EditorGUILayout.EndHorizontal();

            // Boxed so the selected group's own settings read as distinct from the buttons above
            // and the tree below.
            if (realGroupSelected)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawRenameField(groupsProp, state);
                if (groupNaming)
                    DrawIncludeInNameToggle(groupsProp, state);
                EditorGUILayout.EndVertical();
            }
        }

        // Per-group opt-in that turns organizational nesting into namespacing. Shown only for the
        // reference tabs (Global, Construct); Factory always path-names and Pool is type-named.
        private static void DrawIncludeInNameToggle(SerializedProperty groupsProp, State state)
        {
            int index = IndexOfGroup(groupsProp, state.SelectedGroupId);
            if (index < 0) return;

            var includeProp = groupsProp.GetArrayElementAtIndex(index).FindPropertyRelative("IncludeInName");
            includeProp.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent("Namespace with group name",
                    "When on, this group's name (and any opted-in ancestor's) prefixes the generated member name " +
                    "of entries inside it - e.g. _ts.EnemiesSpawner instead of _ts.Spawner. Off keeps the group " +
                    "purely organizational. Renaming or reparenting an opted-in group renames the member."),
                includeProp.boolValue);
        }

        // Includes the target's type name so TsWindow's TsConfig-backed trees and
        // TsBuiltinConfigInspector's TsBuiltinConfig-backed ones never collide despite sharing
        // property names like "GlobalGroups". Pure so it's directly unit-testable.
        internal static string ComputeSessionKey(string targetTypeName, string groupsPropertyName) =>
            $"Tsvrc.TsGroupTree.{targetTypeName}.{groupsPropertyName}";

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
            state.TreeViewState.selectedIDs = new List<int> { newId };
            state.PendingRenameGroupId = newId;
            SessionState.SetInt(state.SessionKey + ".Selected", newId);
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
            state.TreeViewState.selectedIDs = new List<int> { state.SelectedGroupId };
            SessionState.SetInt(state.SessionKey + ".Selected", state.SelectedGroupId);
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

            ApplyPendingRenameFocus(state);
        }

        // Re-requests focus every repaint the pending group is the one being shown, rather than
        // consuming the request on the first repaint after creation - a same-frame event (e.g.
        // the TreeView's own selection sync) can steal focus before the user's first keystroke
        // lands. Only clears once "TsGroupRename" is actually observed focused, or once the user
        // navigates away from the pending group. Internal (not private) so its state-machine
        // transitions are directly unit-testable - see TsGroupTreeGUITests.
        internal static void ApplyPendingRenameFocus(State state)
        {
            if (!state.PendingRenameGroupId.HasValue) return;

            if (state.PendingRenameGroupId.Value != state.SelectedGroupId)
            {
                state.PendingRenameGroupId = null;
                return;
            }

            if (GUI.GetNameOfFocusedControl() == "TsGroupRename")
            {
                state.PendingRenameGroupId = null;
                return;
            }

            EditorGUI.FocusTextInControl("TsGroupRename");
        }

        // Right pane: entries belonging to the selected group, never widened to its subtree - see
        // the filter's own comment below for why.
        //
        // Paginated (EntriesPerPage rows drawn at a time) rather than virtualized: two earlier
        // virtualization attempts (a hand-rolled scroll view, then a second TreeView) each fought
        // IMGUI's event model in different ways with no fix that held up under real interaction.
        // Pagination sidesteps that class of bug: a small, fixed number of plain EditorGUILayout
        // rows per page needs no scroll view, no virtualization, no reasoning about IMGUI events.
        // 10 rows/page sits at the low end of the generally-recommended 10-50 range - appropriate
        // here since these are dense, multi-line editable rows in a compact side panel, closer to
        // a data-grid row than a content-list item.
        private const int EntriesPerPage = 10;

        private static void DrawContents(SerializedProperty entriesProp, SerializedProperty groupsProp, State state,
            string emptyHint, bool assetsOnly, bool warnDuplicates,
            string memberPrefix, string memberSuffix, bool prefixRespectsToggle)
        {
            bool showNames = memberPrefix != null;
            bool searching = !string.IsNullOrEmpty(state.SearchText);

            // Always the selected group's own direct entries, never widened to its subtree - the
            // tree's own search results are already flat (only groups that directly contain a
            // match), so widening here used to pull in sibling/descendant groups' entries too.
            bool scopeChanged = state.LastEntriesPageGroupId != state.SelectedGroupId ||
                state.LastEntriesPageSearchText != state.SearchText;
            var indices = new List<int>();
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var entry = entriesProp.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("GroupId").intValue != state.SelectedGroupId) continue;
                if (searching)
                {
                    var value = entry.FindPropertyRelative("Value").objectReferenceValue;
                    if (value == null || value.name.IndexOf(state.SearchText, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                indices.Add(i);
            }

            // Header naming what's listed, with "+ Add" beside it - always visible regardless of
            // scroll position. Disabled while searching, matching AddEntry's own requirement of
            // an unambiguous single target group.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DescribeSelectedGroup(groupsProp, state.SelectedGroupId), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(searching))
            if (GUILayout.Button("+ Add", GUILayout.Width(60)))
            {
                // Prepended (see AddEntry's own comment), so the new entry always lands at the
                // same fixed spot: page 1, row 1. No ExitGUI(): this runs nested inside
                // TsWindow's DrawTab(), and it's the OUTER ApplyModifiedProperties() (after
                // DrawTab() returns) that persists the insert - ExitGUI() would abort before that
                // ever runs, silently discarding it. Instead, patch `indices` in place: AddEntry
                // prepends at raw index 0, so every existing raw index shifts up by one.
                AddEntry(entriesProp, state.SelectedGroupId);
                for (int k = 0; k < indices.Count; k++) indices[k]++;
                indices.Insert(0, 0);
                state.EntriesPage = 0;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (indices.Count == 0 && !string.IsNullOrEmpty(emptyHint))
                TsEditorGUI.DrawStatusBox(emptyHint, MessageType.None);

            // Group or search changed: back to page 1, since the old page number may not exist
            // for the new set. Reuses scopeChanged from above - group/search don't change between
            // the two checks. (The Add button above already resets the page itself.)
            if (scopeChanged)
            {
                state.EntriesPage = 0;
                state.LastEntriesPageGroupId = state.SelectedGroupId;
                state.LastEntriesPageSearchText = state.SearchText;
            }

            int pageCount = ComputePageCount(indices.Count, EntriesPerPage);
            state.EntriesPage = ClampPage(state.EntriesPage, pageCount);

            // Duplicate status is a property of the whole filtered set, not just the current
            // page, so this still walks every filtered entry - but only reads a value, never
            // draws a control, a small fraction of what drawing every row would cost.
            bool[] isDuplicateByRow = null;
            if (warnDuplicates)
            {
                isDuplicateByRow = new bool[indices.Count];
                var seen = new HashSet<Object>();
                for (int row = 0; row < indices.Count; row++)
                {
                    var value = entriesProp.GetArrayElementAtIndex(indices[row]).FindPropertyRelative("Value").objectReferenceValue;
                    isDuplicateByRow[row] = ObjectListGUI.IsDuplicate(value, seen);
                }
            }

            int pageStart = state.EntriesPage * EntriesPerPage;
            int pageEnd = Mathf.Min(pageStart + EntriesPerPage, indices.Count);

            int toDelete = -1;
            for (int row = pageStart; row < pageEnd; row++)
            {
                int entryIndex = indices[row];
                bool isDuplicate = isDuplicateByRow != null && isDuplicateByRow[row];
                if (DrawEntryRow(entriesProp, entryIndex, groupsProp, isDuplicate, showNames, assetsOnly, warnDuplicates,
                        memberPrefix, memberSuffix, prefixRespectsToggle))
                    toDelete = entryIndex;
            }

            if (toDelete >= 0)
            {
                entriesProp.GetArrayElementAtIndex(toDelete).FindPropertyRelative("Value").objectReferenceValue = null;
                entriesProp.DeleteArrayElementAtIndex(toDelete);
            }

            if (pageCount > 1)
                DrawPaginationFooter(state, pageCount, indices.Count);
        }

        // "(ungrouped)" for the root bucket, the group's own name ("(unnamed)" if blank)
        // otherwise, falling back to "(ungrouped)" for a stale/removed id. Internal (not private)
        // so it's directly unit-testable.
        internal static string DescribeSelectedGroup(SerializedProperty groupsProp, int groupId)
        {
            if (groupId == 0) return "(ungrouped)";

            int index = IndexOfGroup(groupsProp, groupId);
            if (index < 0) return "(ungrouped)";

            string name = groupsProp.GetArrayElementAtIndex(index).FindPropertyRelative("Name").stringValue;
            return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }

        // One entry's full row: object field, optional name field, delete button, hints, member
        // preview. Returns true if delete was clicked (deferred - the caller mutates entriesProp
        // only after every row for this page has drawn, to avoid invalidating indices mid-loop).
        private static bool DrawEntryRow(SerializedProperty entriesProp, int entryIndex, SerializedProperty groupsProp,
            bool isDuplicate, bool showNames, bool assetsOnly, bool warnDuplicates,
            string memberPrefix, string memberSuffix, bool prefixRespectsToggle)
        {
            var entry = entriesProp.GetArrayElementAtIndex(entryIndex);
            var valueProp = entry.FindPropertyRelative("Value");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(valueProp, GUIContent.none);
            if (showNames)
            {
                var nameProp = entry.FindPropertyRelative("Name");
                nameProp.stringValue = EditorGUILayout.DelayedTextField(nameProp.stringValue, GUILayout.Width(120));
            }
            bool deleteClicked = ObjectListGUI.DeleteButton();
            EditorGUILayout.EndHorizontal();

            ObjectListGUI.DrawEntryHints(valueProp.objectReferenceValue, assetsOnly, warnDuplicates, isDuplicate);

            if (showNames)
                DrawMemberPreview(entry, groupsProp, memberPrefix, memberSuffix, prefixRespectsToggle);

            return deleteClicked;
        }

        // Always at least 1 (an empty list still shows "Page 1 of 1"). Pure so it's directly
        // unit-testable without driving OnGUI.
        internal static int ComputePageCount(int totalCount, int pageSize) =>
            Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)pageSize));

        // Keeps the current page in [0, pageCount) - needed whenever the filtered set shrinks
        // (a delete, or switching to a smaller group/search result). Pure, directly unit-testable.
        internal static int ClampPage(int page, int pageCount) => Mathf.Clamp(page, 0, pageCount - 1);

        private static void DrawPaginationFooter(State state, int pageCount, int totalCount)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(state.EntriesPage <= 0))
                if (GUILayout.Button("< Prev", GUILayout.Width(60)))
                    state.EntriesPage--;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Page {state.EntriesPage + 1} of {pageCount} ({totalCount} total)",
                EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(state.EntriesPage >= pageCount - 1))
                if (GUILayout.Button("Next >", GUILayout.Width(60)))
                    state.EntriesPage++;
            EditorGUILayout.EndHorizontal();
        }

        // Inserted at the front, not appended - gives "+ Add" a predictable, always-visible
        // landing spot (see DrawContents). Array position has no effect on generated output:
        // every module reads *Entries via a plain foreach (GlobalModule, ConstructModule,
        // FactoryModule) or groups by prefab type (PoolModule) - order only affects the cosmetic
        // sequence generated members appear in, never correctness.
        internal static void AddEntry(SerializedProperty entriesProp, int groupId)
        {
            const int newIndex = 0;
            entriesProp.InsertArrayElementAtIndex(newIndex);
            var element = entriesProp.GetArrayElementAtIndex(newIndex);
            // InsertArrayElementAtIndex copies a neighbouring element, so every field is reset
            // explicitly to give a genuinely empty new entry.
            element.FindPropertyRelative("Value").objectReferenceValue = null;
            element.FindPropertyRelative("GroupId").intValue = groupId;
            element.FindPropertyRelative("Name").stringValue = string.Empty;
        }

        // Dim line under an entry showing the member it generates: "_ts.Name" (Global/Construct)
        // or "CreateName(parent)" (Factory), prefixed by its group chain. A blank Name shows
        // "<auto>", since the derived default depends on the object's resolved type at generate
        // time.
        private static void DrawMemberPreview(SerializedProperty entry, SerializedProperty groupsProp,
            string memberPrefix, string memberSuffix, bool prefixRespectsToggle)
        {
            int groupId = entry.FindPropertyRelative("GroupId").intValue;
            string prefix = ComputeGroupPrefix(groupsProp, groupId, prefixRespectsToggle);
            string leaf = TsModule.SanitizeIdentifier(entry.FindPropertyRelative("Name").stringValue);
            string body = prefix + (leaf.Length > 0 ? leaf : "<auto>");
            EditorGUILayout.LabelField($"   ↳ {memberPrefix}{body}{memberSuffix}", EditorStyles.miniLabel);
        }

        // A cheap hash of everything the tree renders from: group structure/names, per-entry
        // group membership, and the search text (plus entry names while searching). Reloading
        // only when this changes keeps typing responsive and avoids reassigning IMGUI control
        // ids under a field being edited.
        internal static int ComputeTreeSignature(SerializedProperty groupsProp, SerializedProperty entriesProp, string search)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (search != null ? search.GetHashCode() : 0);
                bool searching = !string.IsNullOrEmpty(search);

                hash = hash * 31 + groupsProp.arraySize;
                for (int i = 0; i < groupsProp.arraySize; i++)
                {
                    var group = groupsProp.GetArrayElementAtIndex(i);
                    hash = hash * 31 + group.FindPropertyRelative("Id").intValue;
                    hash = hash * 31 + group.FindPropertyRelative("ParentId").intValue;
                    string name = group.FindPropertyRelative("Name").stringValue;
                    hash = hash * 31 + (name != null ? name.GetHashCode() : 0);
                }

                hash = hash * 31 + entriesProp.arraySize;
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var entry = entriesProp.GetArrayElementAtIndex(i);
                    hash = hash * 31 + entry.FindPropertyRelative("GroupId").intValue;
                    if (searching)
                    {
                        var value = entry.FindPropertyRelative("Value").objectReferenceValue;
                        hash = hash * 31 + (value != null ? value.name.GetHashCode() : 0);
                    }
                }
                return hash;
            }
        }

        // Same walk as TsModule.BuildGroupPrefix, over live SerializedProperty data, so the preview
        // matches the generated identifier.
        private static string ComputeGroupPrefix(SerializedProperty groupsProp, int groupId, bool respectToggle)
        {
            var chain = new List<string>();
            var visited = new HashSet<int>();
            int current = groupId;
            while (current != 0 && visited.Add(current))
            {
                int index = IndexOfGroup(groupsProp, current);
                if (index < 0) break;
                var group = groupsProp.GetArrayElementAtIndex(index);
                if (!respectToggle || group.FindPropertyRelative("IncludeInName").boolValue)
                    chain.Add(group.FindPropertyRelative("Name").stringValue ?? string.Empty);
                current = group.FindPropertyRelative("ParentId").intValue;
            }

            chain.Reverse();
            return string.Concat(chain.Select(TsModule.SanitizeIdentifier));
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
                var rows = new List<TreeViewItem>();

                if (!string.IsNullOrEmpty(searchString))
                {
                    // Flat results: only groups that *directly* match (own name, or a direct
                    // entry's name) - not the whole ancestor chain, and not a group shown only
                    // because some descendant matches. Each row's label carries its full path
                    // instead (ComputeGroupDisplayPath), since a flat list can't show location
                    // via nesting. Replaced showing the entire matching subtree, which used to
                    // surface several unrelated groups for one search.
                    var directMatches = ComputeDirectMatches(groups, _entriesProp, searchString);
                    if (directMatches.Contains(0))
                        rows.Add(new TreeViewItem(0, 0, DisplayName("(ungrouped)", entryCounts, 0)));
                    foreach (var group in groups.Values.Where(g => directMatches.Contains(g.Id))
                                 .OrderBy(g => g.Name, System.StringComparer.OrdinalIgnoreCase))
                    {
                        string path = ComputeGroupDisplayPath(groups, group.Id);
                        rows.Add(new TreeViewItem(group.Id, 0, DisplayName(path, entryCounts, group.Id)));
                    }
                }
                else
                {
                    var childrenOf = groups.Values.GroupBy(g => g.ParentId).ToDictionary(g => g.Key, g => g.ToList());
                    rows.Add(new TreeViewItem(0, 0, DisplayName("(ungrouped)", entryCounts, 0)));
                    AddChildren(0, 0, groups, childrenOf, entryCounts, rows);
                }

                if (rows.Count == 0)
                    rows.Add(new TreeViewItem(0, 0, "(ungrouped)"));

                SetupParentsAndChildrenFromDepths(root, rows);
                return rows;
            }

            private static void AddChildren(int parentId, int depth, Dictionary<int, GroupInfo> groups,
                Dictionary<int, List<GroupInfo>> childrenOf, Dictionary<int, int> entryCounts, List<TreeViewItem> rows)
            {
                if (!childrenOf.TryGetValue(parentId, out var children)) return;
                foreach (var group in children.OrderBy(g => g.Name, System.StringComparer.OrdinalIgnoreCase))
                {
                    string name = string.IsNullOrEmpty(group.Name) ? "(unnamed)" : group.Name;
                    rows.Add(new TreeViewItem(group.Id, depth, DisplayName(name, entryCounts, group.Id)));
                    AddChildren(group.Id, depth + 1, groups, childrenOf, entryCounts, rows);
                }
            }

            private static string DisplayName(string name, Dictionary<int, int> entryCounts, int groupId)
            {
                int count = entryCounts.TryGetValue(groupId, out int c) ? c : 0;
                return count > 0 ? $"{name} ({count})" : name;
            }

            // A group is a search result if its own name matches, or it has a direct entry whose
            // name matches - not "or a descendant matches" (see BuildRows' own comment). Internal
            // (not private) and takes searchText as a parameter rather than reading the instance
            // searchString, so it's directly unit-testable.
            internal static HashSet<int> ComputeDirectMatches(Dictionary<int, GroupInfo> groups,
                SerializedProperty entriesProp, string searchText)
            {
                var entryNamesByGroup = new Dictionary<int, List<string>>();
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var entry = entriesProp.GetArrayElementAtIndex(i);
                    int groupId = entry.FindPropertyRelative("GroupId").intValue;
                    var value = entry.FindPropertyRelative("Value").objectReferenceValue;
                    if (value == null) continue;
                    if (!entryNamesByGroup.TryGetValue(groupId, out var list))
                        entryNamesByGroup[groupId] = list = new List<string>();
                    list.Add(value.name);
                }

                bool Matches(int id, string name)
                {
                    bool self = !string.IsNullOrEmpty(name) && name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    bool ownEntry = entryNamesByGroup.TryGetValue(id, out var names) &&
                        names.Any(n => n.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    return self || ownEntry;
                }

                var result = new HashSet<int>();
                if (Matches(0, "(ungrouped)")) result.Add(0);
                foreach (var group in groups.Values)
                    if (Matches(group.Id, group.Name))
                        result.Add(group.Id);
                return result;
            }

            // Human-readable "Parent / Child / GroupName" path, labeling a flat search result row
            // so its location stays legible without nesting. Unrelated to ComputeGroupPrefix (the
            // generated-member-name preview), which only concatenates IncludeInName ancestors with
            // no separator. Internal (not private) so it's directly unit-testable.
            internal static string ComputeGroupDisplayPath(Dictionary<int, GroupInfo> groups, int groupId)
            {
                if (groupId == 0) return "(ungrouped)";

                var chain = new List<string>();
                var visited = new HashSet<int>();
                int current = groupId;
                while (current != 0 && visited.Add(current))
                {
                    if (!groups.TryGetValue(current, out var group)) break;
                    chain.Add(string.IsNullOrEmpty(group.Name) ? "(unnamed)" : group.Name);
                    current = group.ParentId;
                }

                chain.Reverse();
                return string.Join(" / ", chain);
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

            // Internal (not private) so ComputeDirectMatches/ComputeGroupDisplayPath can be
            // driven directly from tests without a real SerializedProperty.
            internal struct GroupInfo
            {
                internal int Id;
                internal int ParentId;
                internal string Name;
            }
        }
    }
}
#endif
