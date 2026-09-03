using NUnit.Framework;
using Tsvrc.Player;

namespace Tsvrc.Tests.EditMode
{
    // TsPlayer.ToArray has zero VRCPlayerApi dependency, unlike most of TsPlayer, which needs a
    // live player list (see Tests/PlayMode/Player/TsPlayerTests.cs) and can't run in Edit Mode.
    // GetNumericPlayerId is the other pure-string exception - see TsPlayerGetNumericPlayerIdTests.
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
