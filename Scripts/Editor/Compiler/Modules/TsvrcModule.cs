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
        // Reads config and scans source files. Called once before any Write or Wire method.
        internal abstract void Scan(TsvrcConfig config);

        // Called in RunWire after domain reload. Uses reflection on the compiled type instead of
        // re-scanning source files, since we only need to know what fields already exist.
        internal virtual void ScanForWire(TsvrcConfig config, Type compiledType) => Scan(config);

        internal virtual IEnumerable<string> GetUsings() => Enumerable.Empty<string>();
        // Emits plain C# types at namespace scope, before the generated class declaration.
        internal virtual void WriteBeforeClass(CsWriter w) { }
        internal virtual void WriteFields(CsWriter w) { }
        internal virtual void WriteMethods(CsWriter w) { }
        internal virtual void WriteStartBody(CsWriter w) { }

        // Assigns scene references via SerializedObject. Called after domain reload.
        internal virtual void Wire(SerializedObject target) { }

        // Returns asset paths whose modification requires a full recompile rather than just re-wiring.
        // Use this when a change affects the generated code structure, for example adding a language
        // adds new fields and needs CompiledTsvrc.cs to be regenerated.
        internal virtual IEnumerable<string> GetFullCompileAssetPaths() => Enumerable.Empty<string>();
    }
}
#endif
