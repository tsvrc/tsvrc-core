using NUnit.Framework;
using Tsvrc.Player;

namespace Tsvrc.Tests.EditMode
{
    // GetNumericPlayerId has zero VRCPlayerApi dependency, unlike most of TsPlayer - see
    // TsPlayerToArrayTests for the other exception and why the rest lives in Play Mode instead.
    public class TsPlayerGetNumericPlayerIdTests
    {
        [Test]
        public void GetNumericPlayerId_StandardFormat_ReturnsDigitsAfterHash()
        {
            Assert.AreEqual(123, TsPlayer.GetNumericPlayerId("SomePlayer#123"));
        }

        [Test]
        public void GetNumericPlayerId_DisplayNameContainsHash_UsesTheLastOne()
        {
            Assert.AreEqual(5, TsPlayer.GetNumericPlayerId("Na#me#5"));
        }

        [Test]
        public void GetNumericPlayerId_NoHash_ReturnsZero()
        {
            Assert.AreEqual(0, TsPlayer.GetNumericPlayerId("NoHashHere"));
        }

        [Test]
        public void GetNumericPlayerId_HashWithNoDigitsAfterIt_ReturnsZero()
        {
            Assert.AreEqual(0, TsPlayer.GetNumericPlayerId("Trailing#"));
        }

        [Test]
        public void GetNumericPlayerId_NonDigitAfterHash_ReturnsZero()
        {
            Assert.AreEqual(0, TsPlayer.GetNumericPlayerId("Weird#12x"));
        }

        [Test]
        public void GetNumericPlayerId_ZeroId_ReturnsZero()
        {
            Assert.AreEqual(0, TsPlayer.GetNumericPlayerId("First#0"));
        }
    }
}
