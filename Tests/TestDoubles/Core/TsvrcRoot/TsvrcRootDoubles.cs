using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;

namespace Tsvrc.Tests.Editor
{
    // TestTsvrcRoot (Tests/TestDoubles/Core/TsvrcBehaviour/TsvrcBehaviourDoubles.cs)
    // already covers the no-override case (both Instance/Memory stay null) and is
    // reused here for that scenario. These three doubles cover every override
    // combination TsvrcRoot's own two independent virtual properties allow: only
    // Instance, only Memory, and both - proving neither property's default is
    // accidentally coupled to the other's.
    public class InstanceOnlyTsvrcRootDouble : TsvrcRoot
    {
        public TsvrcInstance FakeInstance;

        public override TsvrcInstance Instance => FakeInstance;
    }

    public class MemoryOnlyTsvrcRootDouble : TsvrcRoot
    {
        public TsMemory FakeMemory;

        public override TsMemory Memory => FakeMemory;
    }

    public class FullTsvrcRootDouble : TsvrcRoot
    {
        public TsvrcInstance FakeInstance;
        public TsMemory FakeMemory;

        public override TsvrcInstance Instance => FakeInstance;
        public override TsMemory Memory => FakeMemory;
    }
}
