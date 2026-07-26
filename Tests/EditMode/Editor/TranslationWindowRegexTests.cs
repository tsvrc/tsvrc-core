using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsTranslationWindow.PeekKeyLabel and its backing regexes are all private —
    // reflection is the only way in (InternalsVisibleTo only reaches internal members,
    // and this class/its members are all private).
    public class TranslationWindowRegexTests
    {
        private static readonly Type WindowType = typeof(TsTranslationWindow);

        private static (string key, string label) PeekKeyLabel(string json)
        {
            MethodInfo m = WindowType.GetMethod("PeekKeyLabel", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "TsTranslationWindow.PeekKeyLabel method changed or was removed.");
            return ((string, string))m.Invoke(null, new object[] { json });
        }

        private static Regex TargetPattern()
        {
            FieldInfo f = WindowType.GetField("TargetPattern", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "TsTranslationWindow.TargetPattern field changed or was removed.");
            return (Regex)f.GetValue(null);
        }

        [Test]
        public void PeekKeyLabel_ValidJson_ExtractsKeyAndLabel()
        {
            string json = "{\"key\":\"en\",\"label\":\"English\",\"entries\":{}}";

            var (key, label) = PeekKeyLabel(json);

            Assert.AreEqual("en", key);
            Assert.AreEqual("English", label);
        }

        [Test]
        public void PeekKeyLabel_TolerantOfWhitespaceAroundColon()
        {
            string json = "{ \"key\"   :   \"en\" , \"label\"  :  \"English\" }";

            var (key, label) = PeekKeyLabel(json);

            Assert.AreEqual("en", key);
            Assert.AreEqual("English", label);
        }

        [Test]
        public void PeekKeyLabel_MissingLabel_ReturnsNullForBoth()
        {
            string json = "{\"key\":\"en\",\"entries\":{}}";

            var (key, label) = PeekKeyLabel(json);

            Assert.IsNull(key);
            Assert.IsNull(label);
        }

        [Test]
        public void PeekKeyLabel_MissingKey_ReturnsNullForBoth()
        {
            string json = "{\"label\":\"English\",\"entries\":{}}";

            var (key, label) = PeekKeyLabel(json);

            Assert.IsNull(key);
            Assert.IsNull(label);
        }

        [Test]
        public void PeekKeyLabel_MalformedNonJsonText_ReturnsNullForBoth()
        {
            var (key, label) = PeekKeyLabel("this is not json at all");

            Assert.IsNull(key);
            Assert.IsNull(label);
        }

        [Test]
        public void PeekKeyLabel_NestedEntryLabelBeforeTopLevelLabel_IncorrectlyMatchesNestedOne()
        {
            // Known limitation: the regex isn't JSON-aware, so a nested per-entry "label"
            // appearing before the top-level one wins - see assert message below.
            string json =
                "{\"key\":\"en\"," +
                "\"entries\":{\"_greeting_\":{\"label\":\"Hello\"}}," +
                "\"label\":\"English\"}";

            var (key, label) = PeekKeyLabel(json);

            Assert.AreEqual("en", key);
            Assert.AreEqual("Hello", label, "Documents the current (incorrect) behavior: the nested entry's label wins because it appears first in the text.");
        }

        [Test]
        public void PeekKeyLabel_WindowsOwnSampleJson_ParsesCorrectly()
        {
            // Regression guard: the exact sample text ShowCreateSampleDialog() writes should
            // itself satisfy PeekKeyLabel, catching sample/regex drift here instead of silently
            // showing "⚠ Invalid File" for a user's freshly created sample.
            string sampleJson =
@"{
    ""key"": ""en"",
    ""label"": ""English"",
    ""entries"": {
        ""_welcome_"": ""Welcome!"",
        ""_start_"": {
            ""label"": ""Start"",
            ""description"": ""Button label to start the experience.""
        },
        ""_exit_"": ""Exit""
    }
}";

            var (key, label) = PeekKeyLabel(sampleJson);

            Assert.AreEqual("en", key);
            Assert.AreEqual("English", label);
        }

        [Test]
        public void TargetPattern_SingleUnderscoreWrappedName_Matches()
        {
            Assert.IsTrue(TargetPattern().IsMatch("_welcome_"));
        }

        [Test]
        public void TargetPattern_JustTwoUnderscores_DoesNotMatch()
        {
            Assert.IsFalse(TargetPattern().IsMatch("__"));
        }

        [Test]
        public void TargetPattern_NoLeadingOrTrailingUnderscore_DoesNotMatch()
        {
            Assert.IsFalse(TargetPattern().IsMatch("a_b_c"));
        }

        // ExtractEntryKeys/FindMissingKeys are internal static and pure, called directly (no
        // reflection needed, unlike PeekKeyLabel/TargetPattern above).

        [Test]
        public void ExtractEntryKeys_SampleJson_ReturnsAllThreeEntryKeys()
        {
            string json =
@"{
    ""key"": ""en"",
    ""label"": ""English"",
    ""entries"": {
        ""_welcome_"": ""Welcome!"",
        ""_start_"": {
            ""label"": ""Start"",
            ""description"": ""Button label to start the experience.""
        },
        ""_exit_"": ""Exit""
    }
}";
            var keys = TsTranslationWindow.ExtractEntryKeys(json).ToList();

            CollectionAssert.AreEquivalent(new[] { "_welcome_", "_start_", "_exit_" }, keys);
        }

        [Test]
        public void ExtractEntryKeys_EmptyOrNullJson_ReturnsEmpty()
        {
            Assert.IsEmpty(TsTranslationWindow.ExtractEntryKeys(""));
            Assert.IsEmpty(TsTranslationWindow.ExtractEntryKeys(null));
        }

        [Test]
        public void FindMissingKeys_SceneKeyNotInAnyLanguageFile_IsReported()
        {
            var missing = TsTranslationWindow.FindMissingKeys(
                sceneTargetKeys: new[] { "_welcome_", "_typo_key_" },
                availableKeys: new[] { "_welcome_", "_exit_" });

            CollectionAssert.AreEqual(new[] { "_typo_key_" }, missing);
        }

        [Test]
        public void FindMissingKeys_AllSceneKeysCovered_ReturnsEmpty()
        {
            var missing = TsTranslationWindow.FindMissingKeys(
                sceneTargetKeys: new[] { "_welcome_", "_exit_" },
                availableKeys: new[] { "_welcome_", "_exit_", "_start_" });

            Assert.IsEmpty(missing);
        }

        [Test]
        public void FindMissingKeys_DuplicateSceneKeys_ReportedOnce()
        {
            var missing = TsTranslationWindow.FindMissingKeys(
                sceneTargetKeys: new[] { "_typo_", "_typo_" },
                availableKeys: Enumerable.Empty<string>());

            Assert.AreEqual(new[] { "_typo_" }, missing);
        }
    }
}
