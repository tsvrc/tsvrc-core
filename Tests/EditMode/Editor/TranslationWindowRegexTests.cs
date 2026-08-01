using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsTranslationWindow.PeekKeyLabel is private - reflection is the only way in
    // (InternalsVisibleTo only reaches internal members). It's a thin wrapper over
    // TranslationModule.ParseLanguageJson (internal, shared with the real regenerate pass), so a
    // file that fails real parsing can never show a plausible-looking preview here, and vice versa.
    public class TranslationWindowRegexTests
    {
        private static readonly Type WindowType = typeof(TsTranslationWindow);

        private static (string key, string label) PeekKeyLabel(string assetName, string json)
        {
            var asset = new TextAsset(json) { name = assetName };
            MethodInfo m = WindowType.GetMethod("PeekKeyLabel", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "TsTranslationWindow.PeekKeyLabel method changed or was removed.");
            try
            {
                return ((string, string))m.Invoke(null, new object[] { asset });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
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

            var (key, label) = PeekKeyLabel("lang", json);

            Assert.AreEqual("en", key);
            Assert.AreEqual("English", label);
        }

        // Missing-field/malformed-JSON error paths are TranslationModule.ParseLanguageJson's own
        // behavior, already covered by TranslationParsingTests' ParseLanguageJson_* tests -
        // PeekKeyLabel just forwards to it, so re-testing that matrix here would be redundant.

        [Test]
        public void PeekKeyLabel_WindowsOwnSampleJson_ParsesCorrectly()
        {
            // Regression guard: the exact sample text ShowCreateSampleDialog() writes should
            // itself satisfy PeekKeyLabel, catching sample/parser drift here instead of silently
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

            var (key, label) = PeekKeyLabel("lang", sampleJson);

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
