#if UNITY_EDITOR
using System;
using Tsvrc.Utils;

namespace Tsvrc.Editor
{
    // Generates the _memory field and _TsMemoryStart() on TsGenerated, and wires
    // the scene TsvrcMemory component into it. Every other system accesses shared memory
    // through TsGenerated.Memory; this module ensures that reference is always set. Mirrors LogModule.
    internal class MemoryModule : TsSingleComponentModule
    {
        // Package-relative, not a literal, see PackagePaths.
        private static string MemoryScriptPath => $"{PackagePaths.Root}/Runtime/Utils/TsvrcMemory.cs";
        private static string MemoryAssetPath => $"{PackagePaths.Root}/Runtime/Utils/TsvrcMemory.asset";

        internal override string FileName => "TsGeneratedMemory.cs";

        protected override Type ComponentType => typeof(TsvrcMemory);
        protected override string FieldName => "_memory";
        protected override string PublicPropertyName => "Memory";
        protected override string StartMethodName => "_TsMemoryStart";
        protected override string ChildGameObjectName => "TsMemory";
        protected override string ScriptPath => MemoryScriptPath;
        protected override string AssetPath => MemoryAssetPath;
        protected override string ModuleTag => "MemoryModule";
    }
}
#endif
