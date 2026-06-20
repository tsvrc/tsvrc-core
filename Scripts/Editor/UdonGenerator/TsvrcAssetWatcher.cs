#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tsvrc.Editor
{
    // Triggers a generator rerun when any asset that a module has declared as watched
    // (via WatchedAssets()) is imported, deleted, or moved. Domain reload events are
    // excluded because TsvrcDomainReloadHandler already handles those.
    internal class TsvrcAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets,
            bool didDomainReload)
        {
            // TsvrcDomainReloadHandler.cs owns post-reload runs.
            if (didDomainReload) return;
            var watched = TsvrcGenerator.WatchedPaths;
            if (watched.Count == 0) return;

            if (AnyMatch(importedAssets, watched) ||
                AnyMatch(deletedAssets, watched) ||
                AnyMatch(movedAssets, watched) ||
                AnyMatch(movedFromAssets, watched))
                TsvrcGenerator.ScheduleRerun();
        }

        private static bool AnyMatch(string[] paths, HashSet<string> watched)
        {
            foreach (var path in paths)
                if (watched.Contains(path)) return true;
            return false;
        }
    }
}
#endif
