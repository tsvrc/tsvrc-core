using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    // TsInstance.IsTsMaster is already virtual (public virtual bool IsTsMaster => Networking.IsMaster),
    // so no Runtime seam is needed here - this double overrides it directly with a
    // test-controllable field, letting master-gate consumers (e.g. RankedGameSession) be
    // tested against both the master and non-master branch without a real networking session.
    //
    // [TsCodegenIgnore]: this IS a non-abstract TsInstance subclass, which is exactly what
    // TsGenerator's bootstrap scan and InstanceModule's scaffold-instance scan are looking
    // for - without this attribute, both mistake it for the project's real scaffold and try
    // to Wire() it, which fails (it has no matching generated program asset) and reschedules
    // forever. See TsCodegenIgnoreAttribute's own doc comment.
    [TsCodegenIgnore]
    public class TsInstanceTestSubclass : TsInstance
    {
        public bool SimulatedIsTsMaster;

        public override bool IsTsMaster => SimulatedIsTsMaster;
    }
}
