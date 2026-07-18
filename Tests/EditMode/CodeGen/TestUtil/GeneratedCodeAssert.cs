using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // UdonWriter builds output via StringBuilder.AppendLine(), which emits
    // Environment.NewLine ("\r\n" on Windows); expected golden strings written as C#
    // verbatim/interpolated literals in test source carry whatever line-ending the test
    // file itself was saved with. Normalize both sides before comparing so golden tests
    // aren't sensitive to that incidental difference (ScaffoldModule.GenerateCode() is the
    // one exception that needs no normalization - it's a raw literal template, never built
    // through UdonWriter, so both sides already share the same literal newlines).
    internal static class GeneratedCodeAssert
    {
        internal static void AreEqual(string expected, string actual)
            => Assert.AreEqual(Normalize(expected), Normalize(actual));

        private static string Normalize(string s) => s?.Replace("\r\n", "\n");
    }
}
