using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
{
    // TsGroupTreeGUI's pure, non-rendering logic: AddGroup/GroupHasContents/
    // ReparentContentsAndRemoveGroup/IndexOfGroup and GroupTreeView.CollectSubtreeIds. These are
    // plain SerializedProperty manipulation with no OnGUI/TreeView rendering involved. Actual tree
    // rendering, search-match row filtering, and drag-and-drop are not covered here; this
    // codebase does not unit-test IMGUI drawing itself.
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
            Assert.IsTrue(state.RenamePending, "A freshly created group should be ready to rename immediately.");
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
            Assert.AreEqual(string.Empty, _config.GlobalEntries[1].Name, "A new entry must not inherit the previous entry's name.");
            Assert.IsNull(_config.GlobalEntries[1].Value);
            Assert.AreEqual(2, _config.GlobalEntries[1].GroupId, "The new entry belongs to the group it was added under.");
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
    }
}
