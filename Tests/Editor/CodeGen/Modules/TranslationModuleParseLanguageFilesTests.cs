using System.Collections;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // TranslationModule.ParseLanguageFiles() - the multi-file orchestration layer one level
    // above the already-tested single-file ParseLanguageJson(). Confirms one malformed file
    // doesn't take down the whole run. Uses in-memory TextAsset instances (new
    // TextAsset(text) with no backing project file) - no AssetDatabase involvement.
    public class TranslationModuleParseLanguageFilesTests
    {
        private const string GoodJson = @"{ ""key"": ""en"", ""label"": ""English"", ""entries"": { ""_hi_"": ""Hi"" } }";
        private const string MalformedJson = @"{ this is not valid json";

        private static IList ParseLanguageFiles(TsTranslationConfig config)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(TranslationModule), "ParseLanguageFiles", config);

        private static string KeyOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Key");

        [Test]
        public void ParseLanguageFiles_OneGoodOneMalformed_MalformedSkippedGoodSurvives()
        {
            var config = ScriptableObject.CreateInstance<TsTranslationConfig>();
            var good = new TextAsset(GoodJson) { name = "en" };
            var bad = new TextAsset(MalformedJson) { name = "broken" };
            config.LanguageFiles = new[] { bad, good };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Failed to parse 'broken'.*"));

            var result = ParseLanguageFiles(config);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("en", KeyOf(result[0]));

            Object.DestroyImmediate(config);
            Object.DestroyImmediate(good);
            Object.DestroyImmediate(bad);
        }

        [Test]
        public void ParseLanguageFiles_NullEntryInLanguageFilesArray_Skipped()
        {
            var config = ScriptableObject.CreateInstance<TsTranslationConfig>();
            config.LanguageFiles = new TextAsset[] { null };

            var result = ParseLanguageFiles(config);

            Assert.AreEqual(0, result.Count);

            Object.DestroyImmediate(config);
        }

        [Test]
        public void ParseLanguageFiles_NullLanguageFilesArray_ReturnsEmptyListWithoutThrowing()
        {
            var config = ScriptableObject.CreateInstance<TsTranslationConfig>();
            config.LanguageFiles = null;

            IList result = null;
            Assert.DoesNotThrow(() => result = ParseLanguageFiles(config));
            Assert.AreEqual(0, result.Count);

            Object.DestroyImmediate(config);
        }
    }
}
