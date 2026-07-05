using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcAssetWatcher.OnPostprocessAllAssets - reflected and called directly with
    // synthetic path arrays. Phase G7.1.
    //
    // Only the two branches that are provably side-effect-free are exercised directly here:
    // `didDomainReload: true` (short-circuits immediately) and an empty `WatchedPaths` (also
    // short-circuits). The "genuine match schedules a rerun" branch is deliberately NOT
    // driven from this test: TsvrcGenerator is a static, process-wide singleton, and
    // triggering a real ScheduleRerun() here would enqueue a real EditorApplication.delayCall
    // that runs the real Run() pipeline (with real Assets/TsvrcGenerated file writes and
    // process-lifetime EditorApplication.hierarchyChanged subscriptions) later in the same
    // batch, potentially affecting every test that runs after this one. AnyMatch's own
    // matching logic is already covered directly and safely by
    // TsvrcAssetWatcherAnyMatchTests; TsvrcBuildCompileTests separately proves that a real
    // Run() does populate WatchedPaths with real entries - composing those two facts is
    // sufficient without needing to actually fire ScheduleRerun() from this test.
    public class TsvrcAssetWatcherOnPostprocessAllAssetsTests
    {
        private static readonly MethodInfo Method = typeof(TsvrcAssetWatcher).GetMethod(
            "OnPostprocessAllAssets", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void OnPostprocessAllAssets_DidDomainReloadTrue_DoesNotThrowRegardlessOfPathOverlap()
        {
            Assert.IsNotNull(Method, "TsvrcAssetWatcher.OnPostprocessAllAssets signature changed or was removed.");

            var anyPaths = new[] { "Assets/TsvrcGenerated/TsvrcGenerated.cs" };
            Assert.DoesNotThrow(() => Method.Invoke(null, new object[] { anyPaths, anyPaths, anyPaths, anyPaths, true }));
        }

        [Test]
        public void OnPostprocessAllAssets_EmptyArraysAndNotADomainReload_DoesNotThrow()
        {
            var none = new string[0];
            Assert.DoesNotThrow(() => Method.Invoke(null, new object[] { none, none, none, none, false }));
        }
    }
}
