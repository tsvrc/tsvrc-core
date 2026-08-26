using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsGroupTreeGUI's pure, non-rendering logic: AddGroup/GroupHasContents/
    // ReparentContentsAndRemoveGroup/IndexOfGroup, GroupTreeView.CollectSubtreeIds, and the
    // search-matching helpers (ComputeDirectMatches/ComputeGroupDisplayPath) that decide *which*
    // groups a search matches and how to label them - all plain SerializedProperty/dictionary
    // logic with no OnGUI/TreeView rendering involved. Actual tree rendering and drag-and-drop
    // are not covered here; this codebase does not unit-test IMGUI drawing itself.
    public class TsGroupTreeGUITests
    {
        private TempSceneScope _scope;
        private TsConfig _config;
        private SerializedObject _so;
        private SerializedProperty _groupsProp;
        private SerializedProperty _entriesProp;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _config = _scope.CreateGameObject("Config").AddComponent<TsConfig>();
            _so = new SerializedObject(_config);
            _groupsProp = _so.FindProperty(nameof(TsConfig.GlobalGroups));
            _entriesProp = _so.FindProperty(nameof(TsConfig.GlobalEntries));
        }

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void AddGroup_RootLevel_AssignsIdFromCounterAndAdvancesIt()
        {
            var state = new TsGroupTreeGUI.State();

            TsGroupTreeGUI.AddGroup(_groupsProp, state, parentId: 0);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(1, _config.GlobalGroups.Length);
            Assert.AreEqual(1, _config.GlobalGroups[0].Id);
            Assert.AreEqual(0, _config.GlobalGroups[0].ParentId);
            Assert.AreEqual("New Group", _config.GlobalGroups[0].Name);
            Assert.AreEqual(2, _config.GlobalNextGroupId, "The counter must advance past the id it just assigned.");
            Assert.AreEqual(1, state.SelectedGroupId);
            Assert.AreEqual(1, state.PendingRenameGroupId, "A freshly created group should be ready to rename immediately.");
            CollectionAssert.AreEqual(new[] { 1 }, state.TreeViewState.selectedIDs,
                "The TreeView's own selection must follow the new group too, or the next Draw() sync would clobber it back.");
        }

        [Test]
        public void AddGroup_WithParentId_SetsParentIdOnTheNewGroup()
        {
            var state = new TsGroupTreeGUI.State();

            TsGroupTreeGUI.AddGroup(_groupsProp, state, parentId: 5);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(5, _config.GlobalGroups[0].ParentId);
        }

        [Test]
        public void AddGroup_CalledTwice_AssignsSequentialIds()
        {
            var state = new TsGroupTreeGUI.State();

            TsGroupTreeGUI.AddGroup(_groupsProp, state, parentId: 0);
            TsGroupTreeGUI.AddGroup(_groupsProp, state, parentId: 0);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(1, _config.GlobalGroups[0].Id);
            Assert.AreEqual(2, _config.GlobalGroups[1].Id);
        }

        [Test]
        public void AddEntry_NewEntryIsBlank_DoesNotInheritPreviousName()
        {
            var marker = _scope.CreateGameObject("Marker");
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 1, Name = "Container" } };
            _so.Update();

            TsGroupTreeGUI.AddEntry(_entriesProp, 2);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(2, _config.GlobalEntries.Length);
            Assert.AreEqual(string.Empty, _config.GlobalEntries[0].Name, "A new entry must not inherit an existing entry's name.");
            Assert.IsNull(_config.GlobalEntries[0].Value);
            Assert.AreEqual(2, _config.GlobalEntries[0].GroupId, "The new entry belongs to the group it was added under.");
        }

        [Test]
        public void AddEntry_ExistingEntry_IsPrepended_NotAppended()
        {
            // Landing at a fixed, predictable position (page 1, row 1) regardless of how many
            // entries already exist is what lets "+ Add" stay clickable in place for repeated
            // adds - see TsGroupTreeGUI.DrawContents' own comment on the button.
            var marker = _scope.CreateGameObject("Existing");
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 1, Name = "Existing" } };
            _so.Update();

            TsGroupTreeGUI.AddEntry(_entriesProp, 1);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(2, _config.GlobalEntries.Length);
            Assert.AreEqual(string.Empty, _config.GlobalEntries[0].Name, "The new entry must be first.");
            Assert.AreEqual("Existing", _config.GlobalEntries[1].Name, "The pre-existing entry must be pushed down, not overwritten.");
        }

        [Test]
        public void ComputeTreeSignature_SameData_IsStable()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "A" } };
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 1 } };
            _so.Update();

            int first = TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty);
            int second = TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void ComputeTreeSignature_ChangesOnRenameReparentAddAndSearch()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "A" } };
            _so.Update();
            int baseline = TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty);

            _config.GlobalGroups[0].Name = "B";
            _so.Update();
            Assert.AreNotEqual(baseline, TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty), "Rename must change the signature.");

            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 9, Name = "A" } };
            _so.Update();
            Assert.AreNotEqual(baseline, TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty), "Reparent must change the signature.");

            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "A" } };
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 1 } };
            _so.Update();
            Assert.AreNotEqual(baseline, TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, string.Empty), "Adding an entry must change the signature.");

            _config.GlobalEntries = System.Array.Empty<TsGroupedEntry>();
            _so.Update();
            Assert.AreNotEqual(baseline, TsGroupTreeGUI.ComputeTreeSignature(_groupsProp, _entriesProp, "query"), "A different search must change the signature.");
        }

        [Test]
        public void IndexOfGroup_PresentId_ReturnsItsArrayIndex()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 10, Name = "A" },
                new TsGroup { Id = 20, Name = "B" },
            };
            _so.Update();

            Assert.AreEqual(1, TsGroupTreeGUI.IndexOfGroup(_groupsProp, 20));
        }

        [Test]
        public void IndexOfGroup_AbsentId_ReturnsNegativeOne()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 10, Name = "A" } };
            _so.Update();

            Assert.AreEqual(-1, TsGroupTreeGUI.IndexOfGroup(_groupsProp, 999));
        }

        [Test]
        public void GroupHasContents_GroupDoesNotExist_ReturnsFalse()
        {
            bool found = TsGroupTreeGUI.GroupHasContents(_groupsProp, _entriesProp, 999, out bool hasContents);

            Assert.IsFalse(found);
            Assert.IsFalse(hasContents);
        }

        [Test]
        public void GroupHasContents_EmptyGroup_ReturnsTrueButHasContentsFalse()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, Name = "Empty" } };
            _so.Update();

            bool found = TsGroupTreeGUI.GroupHasContents(_groupsProp, _entriesProp, 1, out bool hasContents);

            Assert.IsTrue(found);
            Assert.IsFalse(hasContents);
        }

        [Test]
        public void GroupHasContents_GroupWithChildGroup_HasContentsTrue()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Parent" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
            };
            _so.Update();

            TsGroupTreeGUI.GroupHasContents(_groupsProp, _entriesProp, 1, out bool hasContents);

            Assert.IsTrue(hasContents);
        }

        [Test]
        public void GroupHasContents_GroupWithDirectEntry_HasContentsTrue()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, Name = "HasEntry" } };
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 1 } };
            _so.Update();

            TsGroupTreeGUI.GroupHasContents(_groupsProp, _entriesProp, 1, out bool hasContents);

            Assert.IsTrue(hasContents);
        }

        [Test]
        public void ReparentContentsAndRemoveGroup_ChildGroupAndEntry_BothMoveToDeletedGroupsParent()
        {
            var marker = _scope.CreateGameObject("Marker");
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Root" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
            };
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 1 } };
            _so.Update();

            int returnedParentId = TsGroupTreeGUI.ReparentContentsAndRemoveGroup(_groupsProp, _entriesProp, 1);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(0, returnedParentId);
            Assert.AreEqual(1, _config.GlobalGroups.Length, "Only the target group must be removed, never its contents.");
            Assert.AreEqual(2, _config.GlobalGroups[0].Id, "The surviving group is the one that used to be the child.");
            Assert.AreEqual(0, _config.GlobalGroups[0].ParentId, "The child group must be reparented to the deleted group's own parent.");
            Assert.AreEqual(1, _config.GlobalEntries.Length, "The entry itself must never be deleted, only reparented.");
            Assert.AreEqual(0, _config.GlobalEntries[0].GroupId, "The entry must be reparented to the deleted group's own parent.");
        }

        [Test]
        public void ReparentContentsAndRemoveGroup_LeafGroupWithNoContents_JustRemovesIt()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Root" },
                new TsGroup { Id = 2, ParentId = 0, Name = "Leaf" },
            };
            _so.Update();

            TsGroupTreeGUI.ReparentContentsAndRemoveGroup(_groupsProp, _entriesProp, 2);
            _so.ApplyModifiedProperties();

            Assert.AreEqual(1, _config.GlobalGroups.Length);
            Assert.AreEqual(1, _config.GlobalGroups[0].Id);
        }

        [Test]
        public void CollectSubtreeIds_ThreeLevelChain_ReturnsSelfAndEveryDescendant()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Root" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
                new TsGroup { Id = 3, ParentId = 2, Name = "Grandchild" },
                new TsGroup { Id = 4, ParentId = 0, Name = "UnrelatedSibling" },
            };
            _so.Update();

            var subtree = TsGroupTreeGUI.GroupTreeView.CollectSubtreeIds(_groupsProp, 1);

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, subtree);
            CollectionAssert.DoesNotContain(subtree, 4);
        }

        [Test]
        public void CollectSubtreeIds_LeafGroup_ReturnsOnlyItself()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Root" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Leaf" },
            };
            _so.Update();

            var subtree = TsGroupTreeGUI.GroupTreeView.CollectSubtreeIds(_groupsProp, 2);

            CollectionAssert.AreEquivalent(new[] { 2 }, subtree);
        }

        [Test]
        public void CollectSubtreeIds_UngroupedBucket_IncludesEveryRootLevelGroup()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "A" },
                new TsGroup { Id = 2, ParentId = 1, Name = "ChildOfA" },
                new TsGroup { Id = 3, ParentId = 0, Name = "B" },
            };
            _so.Update();

            var subtree = TsGroupTreeGUI.GroupTreeView.CollectSubtreeIds(_groupsProp, 0);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, subtree);
        }

        [Test]
        public void ComputeSessionKey_DifferentTargetTypes_SamePropertyName_ProduceDifferentKeys()
        {
            // TsConfig and TsBuiltinConfig both have a "GlobalGroups" property - without the
            // target type name in the key, TsWindow and TsBuiltinConfigInspector would share
            // selection/search state for what are actually two independent trees.
            string configKey = TsGroupTreeGUI.ComputeSessionKey("TsConfig", "GlobalGroups");
            string builtinKey = TsGroupTreeGUI.ComputeSessionKey("TsBuiltinConfig", "GlobalGroups");

            Assert.AreNotEqual(configKey, builtinKey);
        }

        [Test]
        public void ComputeSessionKey_SameInputs_IsStable()
        {
            Assert.AreEqual(
                TsGroupTreeGUI.ComputeSessionKey("TsConfig", "PoolGroups"),
                TsGroupTreeGUI.ComputeSessionKey("TsConfig", "PoolGroups"));
        }

        [Test]
        public void ApplyPendingRenameFocus_DifferentGroupSelected_ClearsPendingWithoutFocusing()
        {
            var state = new TsGroupTreeGUI.State { PendingRenameGroupId = 1, SelectedGroupId = 2 };

            TsGroupTreeGUI.ApplyPendingRenameFocus(state);

            Assert.IsNull(state.PendingRenameGroupId,
                "Navigating away from the group pending rename must cancel the pending focus request.");
        }

        [Test]
        public void ApplyPendingRenameFocus_SameGroupStillSelected_FocusNotYetObserved_StaysPending()
        {
            GUIUtility.keyboardControl = 0; // ensure "TsGroupRename" isn't already focused from a prior test
            var state = new TsGroupTreeGUI.State { PendingRenameGroupId = 1, SelectedGroupId = 1 };

            TsGroupTreeGUI.ApplyPendingRenameFocus(state);

            Assert.AreEqual(1, state.PendingRenameGroupId,
                "Must keep re-requesting focus every repaint until the rename control is actually observed " +
                "focused - a same-frame event stealing focus first must not silently drop the rename.");
        }

        [Test]
        public void ApplyPendingRenameFocus_NoPendingRename_IsNoOp()
        {
            var state = new TsGroupTreeGUI.State { PendingRenameGroupId = null, SelectedGroupId = 1 };

            Assert.DoesNotThrow(() => TsGroupTreeGUI.ApplyPendingRenameFocus(state));
            Assert.IsNull(state.PendingRenameGroupId);
        }

        [Test]
        public void ExpandedIds_SurviveReload_WhenCapturedAndRestored()
        {
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Parent" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
            };
            _so.Update();
            var treeState = new TreeViewState();
            var tree = new TsGroupTreeGUI.GroupTreeView(treeState, _groupsProp, _entriesProp);
            tree.SetExpanded(1, true);

            var expandedBefore = tree.GetExpanded();
            // Mirrors what TsGroupTreeGUI.Draw does around a signature-changing Reload(): capture,
            // reload, restore - the direct fix for group/subgroup collapse not persisting.
            tree.Reload();
            tree.SetExpanded(expandedBefore);

            CollectionAssert.Contains(tree.GetExpanded(), 1,
                "Group 1's expanded state must survive a Reload() triggered by an unrelated edit.");
        }

        [Test]
        public void DescribeSelectedGroup_UngroupedBucket_ReturnsUngroupedLabel()
        {
            Assert.AreEqual("(ungrouped)", TsGroupTreeGUI.DescribeSelectedGroup(_groupsProp, 0));
        }

        [Test]
        public void DescribeSelectedGroup_NamedGroup_ReturnsItsName()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "Enemies" } };
            _so.Update();

            Assert.AreEqual("Enemies", TsGroupTreeGUI.DescribeSelectedGroup(_groupsProp, 1));
        }

        [Test]
        public void DescribeSelectedGroup_BlankName_ReturnsUnnamedLabel()
        {
            _config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "" } };
            _so.Update();

            Assert.AreEqual("(unnamed)", TsGroupTreeGUI.DescribeSelectedGroup(_groupsProp, 1));
        }

        [Test]
        public void DescribeSelectedGroup_StaleGroupId_FallsBackToUngroupedLabel()
        {
            Assert.AreEqual("(ungrouped)", TsGroupTreeGUI.DescribeSelectedGroup(_groupsProp, 999));
        }

        [Test]
        public void ComputeDirectMatches_EntryNameMatches_FlagsOnlyItsOwnDirectGroup()
        {
            // The bug this replaced: a match on an entry deep in a subtree used to also flag
            // every ancestor group as a "match" (to keep them visible in the old nested-results
            // tree) - callers that only care about "which group directly has this" (the entries
            // pane, and the new flat search results) need just the one group back.
            var marker = _scope.CreateGameObject("Widget");
            _config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Parent" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
            };
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 2 } };
            _so.Update();

            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>
            {
                [1] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 1, ParentId = 0, Name = "Parent" },
                [2] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 2, ParentId = 1, Name = "Child" },
            };

            var matches = TsGroupTreeGUI.GroupTreeView.ComputeDirectMatches(groups, _entriesProp, "Widget");

            CollectionAssert.AreEquivalent(new[] { 2 }, matches,
                "Only the group directly containing the matching entry should be flagged - not its ancestor.");
        }

        [Test]
        public void ComputeDirectMatches_GroupNameMatches_FlagsThatGroup()
        {
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>
            {
                [1] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 1, ParentId = 0, Name = "Enemies" },
            };
            _so.Update();

            var matches = TsGroupTreeGUI.GroupTreeView.ComputeDirectMatches(groups, _entriesProp, "enem");

            CollectionAssert.Contains(matches, 1);
        }

        [Test]
        public void ComputeDirectMatches_UngroupedBucketWithMatchingEntry_FlagsZero()
        {
            var marker = _scope.CreateGameObject("Widget");
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 0 } };
            _so.Update();
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>();

            var matches = TsGroupTreeGUI.GroupTreeView.ComputeDirectMatches(groups, _entriesProp, "Widget");

            CollectionAssert.Contains(matches, 0);
        }

        [Test]
        public void ComputeDirectMatches_NoMatch_ReturnsEmpty()
        {
            var marker = _scope.CreateGameObject("Widget");
            _config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 0 } };
            _so.Update();
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>();

            var matches = TsGroupTreeGUI.GroupTreeView.ComputeDirectMatches(groups, _entriesProp, "NothingMatchesThis");

            Assert.IsEmpty(matches);
        }

        [Test]
        public void ComputeGroupDisplayPath_UngroupedBucket_ReturnsUngroupedLabel()
        {
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>();

            Assert.AreEqual("(ungrouped)", TsGroupTreeGUI.GroupTreeView.ComputeGroupDisplayPath(groups, 0));
        }

        [Test]
        public void ComputeGroupDisplayPath_NestedGroup_JoinsAncestorChainWithSlashes()
        {
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>
            {
                [1] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 1, ParentId = 0, Name = "World" },
                [2] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 2, ParentId = 1, Name = "Enemies" },
                [3] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 3, ParentId = 2, Name = "Bosses" },
            };

            Assert.AreEqual("World / Enemies / Bosses", TsGroupTreeGUI.GroupTreeView.ComputeGroupDisplayPath(groups, 3));
        }

        [Test]
        public void ComputeGroupDisplayPath_RootLevelGroup_ReturnsJustItsOwnName()
        {
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>
            {
                [1] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 1, ParentId = 0, Name = "World" },
            };

            Assert.AreEqual("World", TsGroupTreeGUI.GroupTreeView.ComputeGroupDisplayPath(groups, 1));
        }

        [Test]
        public void ComputeGroupDisplayPath_CyclicParentChain_DoesNotHangOrThrow()
        {
            // BreakGroupCycles is the real defensive backstop for this (see TsModule), but a
            // display helper reached before that runs must still terminate.
            var groups = new System.Collections.Generic.Dictionary<int, TsGroupTreeGUI.GroupTreeView.GroupInfo>
            {
                [1] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 1, ParentId = 2, Name = "A" },
                [2] = new TsGroupTreeGUI.GroupTreeView.GroupInfo { Id = 2, ParentId = 1, Name = "B" },
            };

            Assert.DoesNotThrow(() => TsGroupTreeGUI.GroupTreeView.ComputeGroupDisplayPath(groups, 1));
        }
    }
}
