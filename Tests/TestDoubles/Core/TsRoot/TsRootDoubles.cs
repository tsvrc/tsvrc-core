using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // TestTsRoot (Tests/TestDoubles/Core/TsBehaviour/TsBehaviourDoubles.cs)
    // already covers the no-override case (both Instance/Memory stay null) and is
    // reused here for that scenario. These three doubles cover every override
    // combination TsRoot's own two independent virtual properties allow: only
    // Instance, only Memory, and both - proving neither property's default is
    // accidentally coupled to the other's.
    public class InstanceOnlyTsRootDouble : TsRoot
    {
        public TsInstance FakeInstance;

        public override TsInstance Instance => FakeInstance;
    }

    public class MemoryOnlyTsRootDouble : TsRoot
    {
        public TsMemory FakeMemory;

        public override TsMemory Memory => FakeMemory;
    }

    public class FullTsRootDouble : TsRoot
    {
        public TsInstance FakeInstance;
        public TsMemory FakeMemory;

        public override TsInstance Instance => FakeInstance;
        public override TsMemory Memory => FakeMemory;
    }
}
