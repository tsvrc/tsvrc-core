using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // SanitizeIdentifier/EscapeString/_languages/BuildEnumNames are private (static/instance
    // members), unreachable via InternalsVisibleTo, so those go through reflection.
    // ParseLanguageJson and LanguageEntry are internal (shared with TsTranslationWindow's own
    // preview - see TranslationWindowRegexTests), so those are called directly.
    public class TranslationParsingTests
    {
        private static readonly Type ModuleType = typeof(TranslationModule);

        private static string SanitizeIdentifier(string s)
        {
            MethodInfo m = ModuleType.GetMethod("SanitizeIdentifier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "TranslationModule.SanitizeIdentifier method changed or was removed.");
            return (string)m.Invoke(null, new object[] { s });
        }

        private static string EscapeString(string s)
        {
            MethodInfo m = ModuleType.GetMethod("EscapeString", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "TranslationModule.EscapeString method changed or was removed.");
            return (string)m.Invoke(null, new object[] { s });
        }

        // Builds the private List<LanguageEntry> _languages state and invokes the private
        // instance method BuildEnumNames(), returning the resulting names in order.
        private static List<string> BuildEnumNames(params (string key, string label)[] languages)
        {
            var typedList = new List<TranslationModule.LanguageEntry>();
            foreach (var (key, label) in languages)
                typedList.Add(new TranslationModule.LanguageEntry { Key = key, Label = label, Entries = new Dictionary<string, string>() });

            object moduleInstance = new TranslationModule();
            FieldInfo languagesField = ModuleType.GetField("_languages", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(languagesField, "TranslationModule._languages field changed or was removed.");
            languagesField.SetValue(moduleInstance, typedList);

            MethodInfo buildEnumNames = ModuleType.GetMethod("BuildEnumNames", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(buildEnumNames, "TranslationModule.BuildEnumNames method changed or was removed.");
            return (List<string>)buildEnumNames.Invoke(moduleInstance, null);
        }

        private static Regex TmpTargetPattern()
        {
            FieldInfo f = ModuleType.GetField("TmpTargetPattern", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "TranslationModule.TmpTargetPattern field changed or was removed.");
            return (Regex)f.GetValue(null);
        }

        // ---- SanitizeIdentifier ----

        [Test]
        public void SanitizeIdentifier_NullOrEmpty_ReturnsUnderscore()
        {
            Assert.AreEqual("_", SanitizeIdentifier(null));
            Assert.AreEqual("_", SanitizeIdentifier(""));
        }

        [Test]
        public void SanitizeIdentifier_AllSymbols_ReturnsUnderscore()
        {
            Assert.AreEqual("_", SanitizeIdentifier("!!!"));
        }

        [Test]
        public void SanitizeIdentifier_LeadingDigit_PrependsUnderscore()
        {
            Assert.AreEqual("_123abc", SanitizeIdentifier("123abc"));
        }

        [Test]
        public void SanitizeIdentifier_InternalSeparator_BecomesSingleUnderscore()
        {
            Assert.AreEqual("Hello_World", SanitizeIdentifier("Hello World"));
        }

        [Test]
        public void SanitizeIdentifier_LeadingSeparator_IsDroppedNotConvertedToUnderscore()
        {
            // The separator-to-underscore rule only fires once sb already has content
            // (`else if (sb.Length > 0)`), so a leading non-alphanumeric run is skipped
            // entirely rather than producing a leading underscore.
            Assert.AreEqual("Hello", SanitizeIdentifier(" Hello"));
        }

        [Test]
        public void SanitizeIdentifier_ConsecutiveSeparators_EachProducesItsOwnUnderscore()
        {
            // No run-collapsing: three symbols in a row yield three underscores, not one.
            Assert.AreEqual("Hello___World", SanitizeIdentifier("Hello!!!World"));
        }

        // ---- EscapeString ----

        [Test]
        public void EscapeString_AllSpecialCharacters_AreEscapedInOrder()
        {
            string input = "a\\b\"c\nd\re\tf";
            string expected = "a" + "\\\\" + "b" + "\\\"" + "c" + "\\n" + "d" + "\\r" + "e" + "\\t" + "f";

            Assert.AreEqual(expected, EscapeString(input));
        }

        [Test]
        public void EscapeString_PlainText_IsUnchanged()
        {
            Assert.AreEqual("hello world", EscapeString("hello world"));
        }

        // ---- ParseLanguageJson ----

        [Test]
        public void ParseLanguageJson_MalformedJson_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[TranslationModule\] Failed to parse 'broken': .*"));

            Assert.IsNull(TranslationModule.ParseLanguageJson("broken", "not json{"));
        }

        [Test]
        public void ParseLanguageJson_MissingKeyField_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TranslationModule] 'nokey' is missing the 'key' field.");

            string json = "{\"label\":\"English\",\"entries\":{}}";
            Assert.IsNull(TranslationModule.ParseLanguageJson("nokey", json));
        }

        [Test]
        public void ParseLanguageJson_MissingLabelField_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TranslationModule] 'nolabel' is missing the 'label' field.");

            string json = "{\"key\":\"en\",\"entries\":{}}";
            Assert.IsNull(TranslationModule.ParseLanguageJson("nolabel", json));
        }

        [Test]
        public void ParseLanguageJson_MissingEntriesField_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TranslationModule] 'noentries' is missing the 'entries' object.");

            string json = "{\"key\":\"en\",\"label\":\"English\"}";
            Assert.IsNull(TranslationModule.ParseLanguageJson("noentries", json));
        }

        [Test]
        public void ParseLanguageJson_EntriesAsArrayInsteadOfObject_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TranslationModule] 'wrongtype' is missing the 'entries' object.");

            string json = "{\"key\":\"en\",\"label\":\"English\",\"entries\":[]}";
            Assert.IsNull(TranslationModule.ParseLanguageJson("wrongtype", json));
        }

        [Test]
        public void ParseLanguageJson_ValidPlainStringEntries_ParsesKeyLabelAndEntries()
        {
            string json = "{\"key\":\"en\",\"label\":\"English\",\"entries\":{\"greeting\":\"Hello\"}}";

            var entry = TranslationModule.ParseLanguageJson("valid", json);

            Assert.IsTrue(entry.HasValue);
            Assert.AreEqual("en", entry.Value.Key);
            Assert.AreEqual("English", entry.Value.Label);
            Assert.AreEqual("Hello", entry.Value.Entries["greeting"]);
        }

        [Test]
        public void ParseLanguageJson_NestedEntryObjectWithLabel_UsesNestedLabel()
        {
            string json = "{\"key\":\"en\",\"label\":\"English\",\"entries\":{\"greeting\":{\"label\":\"Hi there\"}}}";

            var entry = TranslationModule.ParseLanguageJson("nested", json);

            Assert.AreEqual("Hi there", entry.Value.Entries["greeting"]);
        }

        [Test]
        public void ParseLanguageJson_NestedEntryObjectMissingLabel_FallsBackToEmptyString()
        {
            string json = "{\"key\":\"en\",\"label\":\"English\",\"entries\":{\"greeting\":{}}}";

            var entry = TranslationModule.ParseLanguageJson("nestedmissing", json);

            Assert.AreEqual("", entry.Value.Entries["greeting"]);
        }

        // ---- BuildEnumNames ----

        [Test]
        public void BuildEnumNames_SingleLanguage_UsesLabelDirectly()
        {
            List<string> names = BuildEnumNames(("en", "English"));

            Assert.AreEqual(new[] { "English" }, names.ToArray());
        }

        [Test]
        public void BuildEnumNames_DuplicateLabelDifferentKeys_FallsBackToLabelUnderscoreKey()
        {
            List<string> names = BuildEnumNames(("en", "English"), ("en-GB", "English"));

            Assert.AreEqual(new[] { "English", "English_en_GB" }, names.ToArray());
        }

        [Test]
        public void BuildEnumNames_DuplicateLabelAndKey_AppendsNumberedSuffixOnFurtherCollision()
        {
            // Same Label AND same Key across three entries: the second collides with the
            // first and falls back to "Label_Key"; the third collides with THAT fallback
            // too, forcing the numbered-suffix loop to kick in.
            List<string> names = BuildEnumNames(("en", "English"), ("en", "English"), ("en", "English"));

            Assert.AreEqual(new[] { "English", "English_en", "English_en2" }, names.ToArray());
        }

        // ---- TmpTargetPattern ----

        [Test]
        public void TmpTargetPattern_SingleCharKey_Matches()
        {
            Assert.IsTrue(TmpTargetPattern().IsMatch("_x_"));
        }

        [Test]
        public void TmpTargetPattern_MultiCharKey_Matches()
        {
            Assert.IsTrue(TmpTargetPattern().IsMatch("_ab_"));
        }

        [Test]
        public void TmpTargetPattern_JustTwoUnderscores_DoesNotMatch()
        {
            Assert.IsFalse(TmpTargetPattern().IsMatch("__"));
        }

        [Test]
        public void TmpTargetPattern_NoLeadingOrTrailingUnderscore_DoesNotMatch()
        {
            Assert.IsFalse(TmpTargetPattern().IsMatch("a_b_c"));
        }
    }
}
