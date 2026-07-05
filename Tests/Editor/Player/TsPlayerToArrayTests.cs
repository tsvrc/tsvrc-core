using NUnit.Framework;
using Tsvrc.Player;

namespace Tsvrc.Tests.Editor
{
    // TsPlayer.ToArray is the only member of TsPlayer with zero VRCPlayerApi dependency —
    // everything else needs a live player list (see Tests/PlayMode/Player/TsPlayerTests.cs,
    // Phase 5.6 in TESTING_PLAN.md) and can't run in Edit Mode.
    public class TsPlayerToArrayTests
    {
        [Test]
        public void ToArray_WithValue_ReturnsSingleElementArray()
        {
            Assert.AreEqual(new[] { "player#1" }, TsPlayer.ToArray("player#1"));
        }

        [Test]
        public void ToArray_WithNull_ReturnsSingleElementArrayContainingNull()
        {
            string[] result = TsPlayer.ToArray(null);

            Assert.AreEqual(1, result.Length);
            Assert.IsNull(result[0]);
        }
    }
}
