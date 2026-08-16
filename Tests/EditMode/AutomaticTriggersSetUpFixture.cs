using NUnit.Framework;
using Tsvrc.Testing.Framework;

// Deliberately in the global namespace: NUnit only treats a [SetUpFixture] as covering an
// entire assembly's run when it has no namespace. This is the required per-assembly subclass
// AutomaticTriggersSetUpFixtureBase's own doc comment describes - the suppression logic
// itself lives there, exactly once, shared with Tsvrc's own PlayMode suite and every
// consuming world's own test assemblies too.
[SetUpFixture]
public class AutomaticTriggersSetUpFixture : AutomaticTriggersSetUpFixtureBase
{
}
