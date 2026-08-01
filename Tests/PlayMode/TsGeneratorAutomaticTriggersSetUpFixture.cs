using System;
using NUnit.Framework;
using Tsvrc.Editor;

// Deliberately in the global namespace: NUnit only treats a [SetUpFixture] as covering an
// entire assembly's run when it has no namespace. This is the PlayMode counterpart of
// Tsvrc.Tests.EditMode's TsGeneratorAutomaticTriggersSetUpFixture - see its comment for why this
// small registration shim has to exist in both test assemblies rather than being shared: NUnit
// scopes [SetUpFixture] per assembly. Both call the exact same
// TsGenerator.SuppressAutomaticTriggers(); no suppression logic is duplicated, only this
// registration shim.
[SetUpFixture]
public class TsGeneratorAutomaticTriggersSetUpFixture
{
    private IDisposable _scope;

    // Suppresses TsGenerator's reactive hierarchyChanged/asset-watcher triggers for this
    // assembly's whole run. Play Mode tests never call TsGenerator.Run() directly (only
    // tsvrc's own EditMode CodeGen tests do), so unlike the EditMode fixture there is no
    // deliberate call path to preserve here - this exists purely so that the temp scene Unity
    // Test Framework creates for a Play Mode session can never trigger a real regeneration
    // pass against a consuming project's real Assets/TsGenerated.
    [OneTimeSetUp]
    public void OneTimeSetUp() => _scope = TsGenerator.SuppressAutomaticTriggers();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scope.Dispose();
}
