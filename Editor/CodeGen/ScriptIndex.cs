#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Tsvrc.Editor
{
    // Resolves whether class X derives from class Y using script source text instead of loaded
    // type reflection. MonoScript.text is available regardless of whether the project currently
    // compiles. Unity keeps it around for things like the Add Component search and script icons
    // even while the console is full of errors, so this never depends on Assembly-CSharp or any
    // other assembly having successfully built.
    //
    // Deliberately simple and regex based rather than a real C# parser: it only needs to answer
    // what class X's immediate base class's simple name is, which a lightweight scan handles for
    // the vast majority of real world formatting. It mirrors TsModule.IsTsvrcBehaviourType's own
    // pre-existing simplification of comparing base types by simple name only. See
    // IsTsvrcBehaviourType's own comment on why that's deliberate, not sloppy.
    //
    // This is namespace aware, not just name aware, because a project can legitimately have two
    // unrelated classes that share a short name in different namespaces. Every declaration is
    // kept rather than overwritten, keyed by simple class name, with its own enclosing namespace
    // attached. DerivesFrom then either resolves unambiguously, because there is exactly one
    // declaration or a caller supplied namespace hint matches one, or returns false when genuinely
    // ambiguous rather than silently guessing based on file enumeration order.
    internal static class ScriptIndex
    {
        internal readonly struct ClassInfo
        {
            internal readonly string Namespace;
            internal readonly string BaseSimpleName;

            internal ClassInfo(string ns, string baseSimpleName)
            {
                Namespace = ns;
                BaseSimpleName = baseSimpleName;
            }
        }

        // Maps a class's simple name to every declaration of a class by that name seen anywhere
        // in the project. This is almost always a single entry; more than one only happens when
        // two unrelated classes share a short name in different namespaces.
        private static Dictionary<string, List<ClassInfo>> _baseByClass;

        // Every MonoScript's raw source text, collected in the same Rebuild() pass as
        // _baseByClass so usage-based lookups (see TsUsageScanner) don't need a second
        // AssetDatabase.FindAssets("t:MonoScript") walk of their own.
        private static List<string> _sourceTexts;

        private static readonly Regex ClassDeclaration = new Regex(
            @"\bclass\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^>{]*>)?\s*(?::\s*(?<base>[A-Za-z_][\w\.]*))?",
            RegexOptions.Compiled);

        // Matches both block scoped (namespace Foo { ... }) and file scoped (namespace Foo;)
        // declarations. Only the name is needed, not where the block ends.
        private static readonly Regex NamespaceDeclaration = new Regex(
            @"\bnamespace\s+(?<ns>[A-Za-z_][\w\.]*)",
            RegexOptions.Compiled);

        // Called once at the start of every TsGenerator.Run() pass so every module's lookups
        // within that pass see the same, currently on disk state. Never cached across passes,
        // since a developer may have just added, renamed, or removed a script.
        internal static void Rebuild()
        {
            var map = new Dictionary<string, List<ClassInfo>>(System.StringComparer.Ordinal);
            var texts = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;
                texts.Add(script.text);
                ParseInto(script.text, map);
            }
            _baseByClass = map;
            _sourceTexts = texts;
        }

        // True if any indexed script's raw source text matches pattern. Backs TsUsageScanner's
        // "is this generated member referenced anywhere in the project" checks. Rebuilds first if
        // nothing has been indexed yet this pass, same as every other query below.
        internal static bool AnySourceMatches(Regex pattern)
        {
            if (_sourceTexts == null) Rebuild();
            foreach (var text in _sourceTexts)
                if (pattern.IsMatch(text)) return true;
            return false;
        }

        // Walks the base class chain by simple name, starting at className, until it either
        // finds targetBaseSimpleName and returns true, runs out of known base classes and
        // returns false, or detects a cycle and returns false. A cycle means malformed input,
        // so it should never be trusted either way.
        //
        // namespaceHint, when supplied, disambiguates the first hop only, since the caller
        // usually knows the starting class's own namespace, for example from a live scene
        // reference. Every later hop walks a bare ": Base" token from source with no namespace
        // annotation to disambiguate it further, which is a real limit of a regex based index
        // without full using resolution. When a hop has more than one candidate and no hint
        // resolves it, this returns false: a defined "cannot prove derivation" rather than a
        // silent guess based on whichever file happened to be scanned last.
        internal static bool DerivesFrom(string className, string targetBaseSimpleName, string namespaceHint = null)
        {
            if (_baseByClass == null) Rebuild();

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            string current = className;
            string hint = namespaceHint;
            while (current != null && seen.Add(current))
            {
                if (current == targetBaseSimpleName) return true;
                if (!_baseByClass.TryGetValue(current, out var candidates) || candidates.Count == 0) return false;

                ClassInfo? chosen = null;
                if (hint != null)
                    foreach (var c in candidates)
                        if (c.Namespace == hint) { chosen = c; break; }

                if (chosen == null)
                {
                    if (candidates.Count == 1) chosen = candidates[0];
                    else return false;
                }

                current = chosen.Value.BaseSimpleName;
                hint = null;
            }
            return false;
        }

        // Enumerates every indexed (name, namespace) pair whose chain reaches
        // targetBaseSimpleName. This is for callers that need to discover which classes derive
        // from Y, for example InstanceModule confirming a real subclass exists in source even
        // when nothing matching is currently loaded, as opposed to DerivesFrom answering
        // whether one already known name derives from Y.
        internal static IEnumerable<(string Name, string Namespace)> FindAllDerivedFrom(string targetBaseSimpleName)
        {
            if (_baseByClass == null) Rebuild();

            foreach (var kv in _baseByClass)
                foreach (var info in kv.Value)
                    if (DerivesFrom(kv.Key, targetBaseSimpleName, info.Namespace))
                        yield return (kv.Key, info.Namespace);
        }

        // Resolves the (simple class name, namespace) that a MonoScript's own source file
        // declares, independent of whether that class currently compiles. Used when a live
        // UnityEngine.Object reference's GetType() can't produce a real type - typically a
        // "Missing (Mono Script)" component, because Assembly-CSharp currently has a compile
        // error and this particular class has never yet been part of a successfully compiled
        // assembly in this session (see TsModule.TryResolveObjectType). MonoScript.name always
        // matches the declared class's simple name, a requirement Unity itself enforces for a
        // script to be attachable as a component.
        internal static bool TryResolveDeclaredType(MonoScript script, out string typeName, out string ns)
        {
            typeName = null;
            ns = null;
            if (script == null) return false;
            return TryResolveDeclaredType(script.name, script.text, out typeName, out ns);
        }

        // Pure logic half of TryResolveDeclaredType, directly testable with synthetic inputs -
        // no real MonoScript asset required. scriptName is the file's (and therefore the
        // class's) simple name; scriptText is that file's full source.
        internal static bool TryResolveDeclaredType(string scriptName, string scriptText, out string typeName, out string ns)
        {
            typeName = null;
            ns = null;
            if (string.IsNullOrEmpty(scriptName)) return false;

            if (_baseByClass == null) Rebuild();

            typeName = scriptName;
            if (_baseByClass.TryGetValue(typeName, out var candidates) && candidates.Count == 1)
            {
                ns = candidates[0].Namespace;
                return true;
            }

            // Zero or ambiguous (more than one) candidates in the project-wide index: parse
            // this specific file's own text directly, so the answer is always this file's own
            // declaration, never an unrelated same-named class elsewhere in the project.
            var local = new Dictionary<string, List<ClassInfo>>(System.StringComparer.Ordinal);
            ParseInto(scriptText, local);
            if (local.TryGetValue(typeName, out var localMatches) && localMatches.Count > 0)
            {
                ns = localMatches[0].Namespace;
                return true;
            }
            return false;
        }

        // Internal (not private) so tests can exercise the parsing rules directly against
        // arbitrary source snippets without needing real MonoScript assets on disk.
        internal static void ParseInto(string source, Dictionary<string, List<ClassInfo>> target)
        {
            if (string.IsNullOrEmpty(source)) return;

            // Merges namespace and class matches in source order so each class is attributed to
            // the namespace declared nearest before it in the text. This is a position sorted
            // scan, not a real scope aware parser, but it is good enough for the overwhelmingly
            // common one namespace per file convention this codebase (and most real world C#)
            // follows. A class inside a later sibling namespace block simply picks up that later
            // name, which is the correct answer for that shape too.
            var events = new List<(int Position, bool IsNamespace, Match Match)>();
            foreach (Match m in NamespaceDeclaration.Matches(source)) events.Add((m.Index, true, m));
            foreach (Match m in ClassDeclaration.Matches(source)) events.Add((m.Index, false, m));
            events.Sort((a, b) => a.Position.CompareTo(b.Position));

            string currentNamespace = string.Empty;
            foreach (var (_, isNamespace, m) in events)
            {
                if (isNamespace) { currentNamespace = m.Groups["ns"].Value; continue; }

                string name = m.Groups["name"].Value;
                string baseSimple = null;
                if (m.Groups["base"].Success)
                {
                    string baseToken = m.Groups["base"].Value;
                    int lastDot = baseToken.LastIndexOf('.');
                    baseSimple = lastDot >= 0 ? baseToken.Substring(lastDot + 1) : baseToken;
                }

                if (!target.TryGetValue(name, out var list))
                {
                    list = new List<ClassInfo>();
                    target[name] = list;
                }

                int existingIndex = list.FindIndex(c => c.Namespace == currentNamespace);
                if (existingIndex < 0)
                {
                    // First time seeing this (name, namespace) pair.
                    list.Add(new ClassInfo(currentNamespace, baseSimple));
                }
                else if (baseSimple != null)
                {
                    // A partial class is declared once per file or part, and only one of those
                    // declarations, if any, carries the ": Base" clause. A base-less occurrence
                    // must never overwrite a base already found for the same (name, namespace)
                    // pair, but a later base-ful one, the real part, must win over an earlier
                    // base-less one.
                    list[existingIndex] = new ClassInfo(currentNamespace, baseSimple);
                }
            }
        }
    }
}
#endif
