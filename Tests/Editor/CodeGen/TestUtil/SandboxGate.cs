using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Gates a test on whether CodeGenSandbox.Bootstrap() has been run against this project
    // (see CODEGEN_TESTING_PLAN.md Part 3.5). When it hasn't (the normal state of this
    // repository), the gated test Assert.Ignore()s with a pointer to the sandbox menu items
    // instead of failing - it stays a real, executable assertion that actually verifies the
    // happy path for real whenever the sandbox IS active, rather than a permanently-disabled
    // stub.
    internal static class SandboxGate
    {
        internal static void RequireField(Component root, string fieldName)
        {
            var prop = new SerializedObject(root).FindProperty(fieldName);
            if (prop == null)
                Assert.Ignore(
                    $"Field '{fieldName}' does not exist on the compiled TsvrcGenerated type - " +
                    "this project hasn't been bootstrapped with real sample config. Run " +
                    "Tsvrc > CodeGen Sandbox > 1) Bootstrap, run the EditMode suite, then " +
                    "2) Restore (see CODEGEN_TESTING_PLAN.md Part 3.5).");
        }
    }
}
