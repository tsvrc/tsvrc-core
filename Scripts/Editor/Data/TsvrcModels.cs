#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal enum TsvrcGroupKind { Singleton, Behaviour }

    internal class TsvrcEntry
    {
        public Type Type;
        public string FieldName;
        public bool SingletonUsed;    // any _ts.FieldName reference found in code
        public bool FactoryUsed;      // any Create{TypeName}() call found in code
        public bool IsTsvrcBehaviour; // type extends TsvrcBehaviour
        public UnityEngine.Object SceneObject;
    }

    internal class TsvrcGroup
    {
        public string Label;
        public TsvrcGroupKind Kind;
        public List<TsvrcEntry> Entries = new List<TsvrcEntry>();
    }

    // Scan result passed between scanning, building, and wiring layers.
    internal class TsvrcScanResult
    {
        public Tsvrc.Core.TsvrcConfig SourceConfig;
        public Type InstanceType; // null when no TsvrcInstance subclass exists
        public List<TsvrcGroup> Groups = new List<TsvrcGroup>();

        public int TotalEntries()
        {
            int count = 0;
            foreach (var g in Groups) count += g.Entries.Count;
            return count;
        }
    }
}
#endif
