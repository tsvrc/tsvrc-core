#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal enum TsvrcGroupKind { Singleton, Pool, Construct }

    // Represents one physical call site of Get{TypeName}() found in user code.
    internal class TsvrcCallSite
    {
        public string ClassName; // class that contains the call (file stem)
        public string FileName;  // full path (for diagnostics)
    }
}
#endif
