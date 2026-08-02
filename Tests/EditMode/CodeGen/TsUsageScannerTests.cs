using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // Pure matching-rule coverage for TsUsageScanner, exercised against synthetic source strings
    // rather than real project scripts - mirrors ScriptIndexTests' own ParseInto-only split for
    // the same reason (deterministic, isolated from whatever this repo's real scripts say).
    public class TsUsageScannerTests
    {
        [Test]
        public void SourceReferencesMember_DirectAccess_ReturnsTrue()
        {
            Assert.IsTrue(TsUsageScanner.SourceReferencesMember("_ts.GameManager.DoThing();", "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_WhitespaceAroundDot_StillMatches()
        {
            Assert.IsTrue(TsUsageScanner.SourceReferencesMember("_ts .  GameManager.DoThing();", "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_DifferentMemberName_ReturnsFalse()
        {
            Assert.IsFalse(TsUsageScanner.SourceReferencesMember("_ts.OtherManager.DoThing();", "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_PrefixOfAnotherName_DoesNotFalsePositiveViaWordBoundary()
        {
            // "GameManager" must not match a reference to "GameManagerV2" - the trailing \b in
            // the pattern is what prevents this.
            Assert.IsFalse(TsUsageScanner.SourceReferencesMember("_ts.GameManagerV2.DoThing();", "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_MentionedInsideAComment_StillCountsAsUsed()
        {
            // Deliberately conservative: a false positive here only costs a few bytes, while a
            // false negative would silently drop real generated content.
            Assert.IsTrue(TsUsageScanner.SourceReferencesMember("// TODO: wire up _ts.GameManager later", "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_EmptySource_ReturnsFalse()
        {
            Assert.IsFalse(TsUsageScanner.SourceReferencesMember(string.Empty, "GameManager"));
        }

        [Test]
        public void SourceReferencesMember_NullSource_ReturnsFalseWithoutThrowing()
        {
            Assert.IsFalse(TsUsageScanner.SourceReferencesMember(null, "GameManager"));
        }

        [Test]
        public void SourceReferencesMethodCall_DirectCall_ReturnsTrue()
        {
            Assert.IsTrue(TsUsageScanner.SourceReferencesMethodCall("_ts.CreateWidget(transform);", "CreateWidget"));
        }

        [Test]
        public void SourceReferencesMethodCall_WhitespaceBeforeParen_StillMatches()
        {
            Assert.IsTrue(TsUsageScanner.SourceReferencesMethodCall("_ts.CreateWidget   (transform);", "CreateWidget"));
        }

        [Test]
        public void SourceReferencesMethodCall_NameMentionedWithoutParens_ReturnsFalse()
        {
            // A bare mention (e.g. a comment or a string literal naming the method without ever
            // calling it) is not a real call site - the trailing "(" requirement distinguishes
            // "referenced" from merely "mentioned" for a method, unlike a member access.
            Assert.IsFalse(TsUsageScanner.SourceReferencesMethodCall("// see CreateWidget for details", "CreateWidget"));
        }

        [Test]
        public void SourceReferencesMethodCall_PrefixOfAnotherName_DoesNotFalsePositiveViaWordBoundary()
        {
            Assert.IsFalse(TsUsageScanner.SourceReferencesMethodCall("_ts.CreateWidgetV2(transform);", "CreateWidget"));
        }

        [Test]
        public void SourceReferencesMethodCall_EmptySource_ReturnsFalse()
        {
            Assert.IsFalse(TsUsageScanner.SourceReferencesMethodCall(string.Empty, "CreateWidget"));
        }
    }
}
