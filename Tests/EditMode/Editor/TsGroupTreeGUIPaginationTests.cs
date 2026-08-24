using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsGroupTreeGUI.ComputePageCount/ClampPage - the pure logic behind the entries pane's
    // pagination (see TsGroupTreeGUI.DrawContents). Neither test drives OnGUI, matching this
    // suite's convention of not unit-testing IMGUI drawing itself.
    public class TsGroupTreeGUIPaginationTests
    {
        [Test]
        public void ComputePageCount_EmptySet_ReturnsOne()
        {
            Assert.AreEqual(1, TsGroupTreeGUI.ComputePageCount(0, 10),
                "An empty list still shows 'Page 1 of 1', not 'Page 1 of 0'.");
        }

        [Test]
        public void ComputePageCount_ExactMultipleOfPageSize_DoesNotAddAnEmptyTrailingPage()
        {
            Assert.AreEqual(2, TsGroupTreeGUI.ComputePageCount(20, 10));
        }

        [Test]
        public void ComputePageCount_PartialLastPage_RoundsUp()
        {
            Assert.AreEqual(3, TsGroupTreeGUI.ComputePageCount(21, 10));
        }

        [Test]
        public void ComputePageCount_FewerItemsThanOnePage_ReturnsOne()
        {
            Assert.AreEqual(1, TsGroupTreeGUI.ComputePageCount(3, 10));
        }

        [Test]
        public void ClampPage_WithinRange_IsUnchanged()
        {
            Assert.AreEqual(2, TsGroupTreeGUI.ClampPage(2, 5));
        }

        [Test]
        public void ClampPage_PastLastPage_ClampsToLastPage()
        {
            // The scenario that mattered in practice: the filtered set shrank (a delete, or
            // switching to a smaller group) and the previously-current page no longer exists.
            Assert.AreEqual(4, TsGroupTreeGUI.ClampPage(9, 5));
        }

        [Test]
        public void ClampPage_Negative_ClampsToZero()
        {
            Assert.AreEqual(0, TsGroupTreeGUI.ClampPage(-1, 5));
        }

        [Test]
        public void ClampPage_SinglePage_AlwaysZero()
        {
            Assert.AreEqual(0, TsGroupTreeGUI.ClampPage(7, 1));
        }
    }
}
