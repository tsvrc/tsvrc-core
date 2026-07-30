#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Persists each module's last successfully resolved entry list, name, type, and namespace
    // only, since that is all GenerateCode() ever needs and it deliberately excludes live object
    // references. This lets a module fall back to it instead of collapsing to an empty stub when
    // the current resolution pass comes back empty or reduced while
    // EditorUtility.scriptCompilationFailed is true.
    //
    // This is what actually happened in the field: a world script referencing a not yet
    // generated TsGenerated member breaks Assembly-CSharp's compile, which makes every scene
    // reference to a component declared in that same assembly, meaning every real Singleton or
    // Factory entry, resolve to null. LoadConfig() would otherwise see zero entries and silently
    // regenerate an empty _TsSingletonStart(){}, permanently wiping 9 real singletons down to
    // nothing on the very next domain reload. See SingletonModule and FactoryModule for the call
    // sites, and TsModule.IsTsvrcBehaviourType and ScriptIndex for the complementary fix that
    // avoids needing this fallback in the first place wherever possible.
    //
    // Stored under {TsPaths.GeneratedFolder}/.cache/{moduleKey}.json, inside the test
    // redirectable generated folder itself, so tests get their own isolated cache for free via
    // the same TsPaths seam everything else uses, with no separate path to manage.
    internal static class ModuleEntrySnapshot
    {
        [Serializable]
        internal class Entry
        {
            public string Name;
            public string TypeName;
            public string Namespace;

            // Module-specific auxiliary count. Currently only PoolModule populates this, with its
            // last known good TotalSlots per pool type, since that number itself is derived from
            // a live, scene-wide reflection scan just as fragile to a broken compile as the entry
            // list itself. Unused (0) for every other module.
            public int SlotCount;
        }

        [Serializable]
        private class EntryList
        {
            public List<Entry> Items = new List<Entry>();
        }

        // A failed write, for example a read-only .cache folder, a full disk, or a path that is
        // too long, degrades to "no snapshot saved this pass" rather than taking down the whole
        // LoadConfig() call. The fallback this class exists for is itself best effort, so its own
        // persistence layer must never be the thing that turns a transient I/O hiccup into a
        // generation failure.
        internal static void Save(string moduleKey, List<Entry> entries)
        {
            string path = PathFor(moduleKey);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(new EntryList { Items = entries }));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModuleEntrySnapshot] Could not save snapshot for '{moduleKey}': {e.Message}");
            }
        }

        // Null when there is no snapshot yet, meaning it was never saved, or it can't be read or
        // parsed. Callers treat null as nothing to fall back to, never as confirmed empty.
        internal static List<Entry> Load(string moduleKey)
        {
            string path = PathFor(moduleKey);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<EntryList>(File.ReadAllText(path))?.Items;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModuleEntrySnapshot] Could not read snapshot for '{moduleKey}': {e.Message}");
                return null;
            }
        }

        internal static void Clear(string moduleKey)
        {
            string path = PathFor(moduleKey);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string PathFor(string moduleKey) =>
            ToFullPath($"{TsPaths.GeneratedFolder}/.cache/{moduleKey}.json");

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
#endif
