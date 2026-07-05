using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcTranslationWindow.PeekKeyLabel and its backing regexes are all private —
    // reflection is the only way in (InternalsVisibleTo only reaches internal members,
    // and this class/its members are all private).
    public class TranslationWindowRegexTests
    {
        private static readonly Type WindowType = typeof(TsvrcTranslationWindow);

        private static (string key, string label) PeekKeyLabel(string json)
        {
            MethodInfo m = WindowType.GetMethod("PeekKeyLabel", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "TsvrcTranslationWindow.PeekKeyLabel method changed or was removed.");
            return ((string, string))m.Invoke(null, new object[] { json });
        }

        private static Regex TargetPattern()
        {
            FieldInfo f = WindowType.GetField("TargetPattern", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "TsvrcTranslationWindow.TargetPattern field changed or was removed.");
            return (Regex)f.GetValue(null);
        }

        // ---- PeekKeyLabel ----

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
            // Known limitation: the regex isn't JSON-aware and just takes the first
            // "label" occurrence in the raw text. If a nested per-entry "label" field
            // happens to appear before the top-level one, PeekKeyLabel silently returns
            // the wrong value instead of the file's actual display label.
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
            // Regression guard: the exact sample text ShowCreateSampleDialog() writes to
            // disk should itself satisfy PeekKeyLabel — if the sample's shape ever drifts
            // from what the regexes expect, this will catch it here instead of silently
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

        // ---- TargetPattern ----

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
    }
}
