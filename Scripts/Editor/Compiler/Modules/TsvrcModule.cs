#if UNITY_EDITOR
using System;
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

        // Called in RunWire after domain reload. Uses reflection on the compiled type instead of
        // re-scanning source files, since we only need to know what fields already exist.
        internal virtual void ScanForWire(TsvrcConfig config, Type compiledType) => Scan(config);

        internal virtual IEnumerable<string> GetUsings() => Enumerable.Empty<string>();
        /// <summary>Emit plain C# types at namespace scope, before the generated class.</summary>
        internal virtual void WriteBeforeClass(CsWriter w) { }
        internal virtual void WriteFields(CsWriter w) { }
        internal virtual void WriteMethods(CsWriter w) { }
        internal virtual void WriteStartBody(CsWriter w) { }

        /// <summary>Assign scene references via SerializedObject after domain reload.</summary>
        internal virtual void Wire(SerializedObject target) { }

        /// <summary>
        /// Asset paths whose change requires regenerating CompiledTsvrc.cs (full compile + domain reload).
        /// Use when the change affects the generated code structure — e.g. adding a language adds new fields.
        /// </summary>
        internal virtual IEnumerable<string> GetFullCompileAssetPaths() => Enumerable.Empty<string>();
    }
}
#endif
