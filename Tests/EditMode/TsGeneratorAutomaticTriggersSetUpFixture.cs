using System;
using NUnit.Framework;
using Tsvrc.Editor;

// Deliberately in the global namespace: NUnit only treats a [SetUpFixture] as covering an
// entire assembly's run when it has no namespace: one declared inside a namespace only wraps
// tests in that same namespace. An equivalent fixture also exists in Tsvrc.Tests.PlayMode - see
// TsGeneratorAutomaticTriggersSetUpFixture there for why that copy can't be avoided (NUnit scopes
// [SetUpFixture] per assembly, so it can't be shared across assemblies). Both call the exact same
// TsGenerator.SuppressAutomaticTriggers(); no suppression logic is duplicated, only this
// registration shim.
[SetUpFixture]
public class TsGeneratorAutomaticTriggersSetUpFixture
{
    private IDisposable _scope;

    // Suppresses TsGenerator's reactive hierarchyChanged/asset-watcher triggers for this
    // assembly's whole run, so no test's temp/synthetic scene (which never has the consuming
    // project's real TsConfig) can cause a real regeneration pass that overwrites the real
    // Assets/TsGenerated. Explicit, deliberate calls tsvrc's own CodeGen tests make directly to
    // TsGenerator.Run()/AfterDomainReload() are unaffected - only the reactive paths are gated.
    [OneTimeSetUp]
    public void OneTimeSetUp() => _scope = TsGenerator.SuppressAutomaticTriggers();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scope.Dispose();
}
