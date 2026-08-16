using System.Collections.Generic;
using UdonSharp;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Generic TsSubscribe/TsEmit listener double: records how many times each named callback
    /// fired, and optionally the cross-listener firing order via a shared <see cref="Log"/>.
    /// Usable by any world testing its own TsBehaviour subclasses' pub/sub wiring - has no
    /// dependency on Tsvrc.Runtime itself, since TsSubscribe/TsEmit invoke any
    /// UdonSharpBehaviour by method name, so this only needs to be one.
    ///
    /// Lives in its own Tsvrc.Testing.Behaviours assembly (not Tsvrc.Testing.Framework, despite
    /// sharing its C# namespace) because it's a real UdonSharpBehaviour meant to be
    /// AddComponent'd in tests: UdonSharp only allows that for scripts belonging to an assembly
    /// registered as a U# assembly, and once an assembly is so registered, UdonSharp Udon-
    /// compiles every source file in it - including plain reflection-only C# helpers that were
    /// never meant to run as Udon bytecode and may reference internal APIs Udon's compiler
    /// can't resolve. Splitting the one real Udon behaviour out keeps Tsvrc.Testing.Framework's
    /// own files out of that compilation entirely.
    /// </summary>
    public class TsCallbackRecorder : UdonSharpBehaviour
    {
        public List<string> Log;
        public int CallbackACount;
        public int CallbackBCount;

        public void CallbackA()
        {
            CallbackACount++;
            Log?.Add($"{name}.CallbackA");
        }

        public void CallbackB()
        {
            CallbackBCount++;
            Log?.Add($"{name}.CallbackB");
        }
    }
}
