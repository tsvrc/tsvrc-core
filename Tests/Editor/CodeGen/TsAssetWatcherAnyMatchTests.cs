using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsAssetWatcher.AnyMatch is `private static` — reflection is the only way in.
    public class TsAssetWatcherAnyMatchTests
    {
        private static bool AnyMatch(string[] paths, HashSet<string> watched)
        {
            MethodInfo method = typeof(TsAssetWatcher).GetMethod("AnyMatch", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "TsAssetWatcher.AnyMatch method changed or was removed.");
            return (bool)method.Invoke(null, new object[] { paths, watched });
        }

        [Test]
        public void AnyMatch_EmptyPathsArray_ReturnsFalse()
        {
            Assert.IsFalse(AnyMatch(new string[0], new HashSet<string> { "Assets/Foo.cs" }));
        }

        [Test]
        public void AnyMatch_EmptyWatchedSet_ReturnsFalse()
        {
            Assert.IsFalse(AnyMatch(new[] { "Assets/Foo.cs" }, new HashSet<string>()));
        }

        [Test]
        public void AnyMatch_SingleMatchAmongManyNonMatching_ReturnsTrue()
        {
            var watched = new HashSet<string> { "Assets/Watched.cs" };
            var paths = new[] { "Assets/A.cs", "Assets/B.cs", "Assets/Watched.cs", "Assets/C.cs" };

            Assert.IsTrue(AnyMatch(paths, watched));
        }

        [Test]
        public void AnyMatch_NoOverlap_ReturnsFalse()
        {
            var watched = new HashSet<string> { "Assets/Watched.cs" };
            var paths = new[] { "Assets/A.cs", "Assets/B.cs" };

            Assert.IsFalse(AnyMatch(paths, watched));
        }

        [Test]
        public void AnyMatch_CaseSensitivity_MatchesWatchedSetsOwnComparer()
        {
            // TsGenerator builds WatchedPaths with StringComparer.OrdinalIgnoreCase, and
            // AnyMatch's lookup (watched.Contains(path)) honors whatever comparer the caller's
            // HashSet was constructed with — confirm a differently-cased path still matches
            // when the set itself is case-insensitive, and does NOT match when the set is
            // constructed with the default (ordinal, case-sensitive) comparer. This pins which
            // behavior is actually in effect end-to-end (TsGenerator always supplies the
            // case-insensitive set in production).
            var caseInsensitive = new HashSet<string>(new[] { "Assets/Watched.cs" }, System.StringComparer.OrdinalIgnoreCase);
            Assert.IsTrue(AnyMatch(new[] { "assets/WATCHED.cs" }, caseInsensitive));

            var caseSensitive = new HashSet<string> { "Assets/Watched.cs" };
            Assert.IsFalse(AnyMatch(new[] { "assets/WATCHED.cs" }, caseSensitive));
        }
    }
}
