using System.Runtime.CompilerServices;

// Lets Tests/Editor reach the internal CodeGen classes (UdonWriter, TsModule
// helpers, module slot-count/parsing logic, etc.) directly instead of via
// reflection.
[assembly: InternalsVisibleTo("Tsvrc.Tests.EditMode")]

// Lets AutomaticTriggersSetUpFixtureBase reach TsGenerator.SuppressAutomaticTriggers(), the
// test-isolation seam every per-assembly [SetUpFixture] (Tsvrc's own two, and every consuming
// world's own - see Tsvrc.Testing.Framework's README) uses to stop reactive codegen
// regeneration during a test run. A single grant here, rather than one per consumer: nothing
// outside Tsvrc.Testing.Framework calls TsGenerator directly for this anymore.
[assembly: InternalsVisibleTo("Tsvrc.Testing.Framework")]
