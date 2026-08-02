#if UNITY_EDITOR
using System.Text.RegularExpressions;

namespace Tsvrc.Editor
{
    // Answers "is this generated member actually referenced by any project script" for
    // tree-shaking. Built on ScriptIndex's whole-project source-text scan rather than reflection:
    // a Singleton read (_ts.Name) or a Factory call (CreateName(...)) is a bare member/method
    // access with no attribute-markable field to reflect over, unlike PoolModule's [WirePool]
    // mechanism, so this has to look at source text rather than live scene instances or compiled
    // types.
    //
    // Deliberately conservative: a plain substring-shaped regex, not one that tries to exclude
    // comments or string literals. A false positive only costs a few wasted bytes; a false
    // negative would silently drop real generated content, which is worse than not tree-shaking
    // at all. When in doubt, this says "used."
    internal static class TsUsageScanner
    {
        // True if any script in the project contains "_ts.{memberName}" (whitespace around the
        // dot tolerated), the shape every Singleton/Log/Memory access takes from a
        // TsvrcBehaviour.
        internal static bool IsMemberReferenced(string memberName) =>
            ScriptIndex.AnySourceMatches(MemberAccessPattern(memberName));

        // True if any script in the project contains a call shaped like "{methodName}(", the
        // shape a Factory's generated Create{Name}(Transform parent) call site takes.
        internal static bool IsMethodCallReferenced(string methodName) =>
            ScriptIndex.AnySourceMatches(MethodCallPattern(methodName));

        // Pure halves, split out so tests can exercise the exact matching rules against
        // synthetic source strings without depending on ScriptIndex's real, AssetDatabase-backed
        // project scan - mirrors ScriptIndex.ParseInto's own internal/pure split for the same
        // reason.
        internal static bool SourceReferencesMember(string source, string memberName) =>
            MemberAccessPattern(memberName).IsMatch(source ?? string.Empty);

        internal static bool SourceReferencesMethodCall(string source, string methodName) =>
            MethodCallPattern(methodName).IsMatch(source ?? string.Empty);

        private static Regex MemberAccessPattern(string memberName) =>
            new Regex($@"_ts\s*\.\s*{Regex.Escape(memberName)}\b");

        private static Regex MethodCallPattern(string methodName) =>
            new Regex($@"\b{Regex.Escape(methodName)}\s*\(");
    }
}
#endif
