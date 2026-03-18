#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UnityEditor;

namespace Tsvrc.Editor
{
    // Subclass and register in TsvrcCompiler.CreateModules() to extend the compiler.
    internal abstract class TsvrcModule
    {
        /// <summary>Read config + scan source files. Called once before any Write or Wire.</summary>
        internal abstract void Scan(TsvrcConfig config);

        internal virtual IEnumerable<string> GetUsings() => Enumerable.Empty<string>();
        internal virtual void WriteFields(CsWriter w) { }
        internal virtual void WriteMethods(CsWriter w) { }
        internal virtual void WriteStartBody(CsWriter w) { }

        /// <summary>Assign scene references via SerializedObject after domain reload.</summary>
        internal virtual void Wire(SerializedObject target) { }
    }
}
#endif
