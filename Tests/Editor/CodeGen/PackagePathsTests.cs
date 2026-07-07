using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // PackagePaths.Root is fixed at compile time via [CallerFilePath] to wherever
    // PackagePaths.cs physically lives on disk - it cannot be parameterized for a fully
    // isolated test. This pins the current, real value for this project's actual layout
    // (an Assets-folder install, not an embedded/registry package) as a regression guard.
    public class PackagePathsTests
    {
        [Test]
        public void Root_ResolvesToProjectRelativeAssetsTsvrcFolder()
        {
            Assert.AreEqual("Assets/Tsvrc", PackagePaths.Root);
        }

        [Test]
        public void Root_ReturnsSameValueOnRepeatedAccess()
        {
            // _root is cached (??=) after first access - confirm repeated access is stable.
            string first = PackagePaths.Root;
            string second = PackagePaths.Root;

            Assert.AreEqual(first, second);
        }
    }
}
