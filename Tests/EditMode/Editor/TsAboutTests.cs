using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsAbout.DetermineAboutMessage - internal static and pure, called directly (no reflection
    // needed, InternalsVisibleTo already exposes Tsvrc.Editor here).
    public class TsAboutTests
    {
        [Test]
        public void DetermineAboutMessage_ValidPackageJson_IncludesNameVersionAndDescription()
        {
            string json = "{\"name\":\"com.tsvrc.core\",\"displayName\":\"Tsvrc Core\",\"version\":\"0.1.0\"," +
                "\"description\":\"A test description.\"}";

            string message = TsAbout.DetermineAboutMessage(json);

            StringAssert.Contains("Tsvrc Core", message);
            StringAssert.Contains("0.1.0", message);
            StringAssert.Contains("A test description.", message);
        }

        [Test]
        public void DetermineAboutMessage_MissingDisplayName_FallsBackToTsvrc()
        {
            string json = "{\"version\":\"0.1.0\"}";

            string message = TsAbout.DetermineAboutMessage(json);

            StringAssert.Contains("Tsvrc", message);
            StringAssert.Contains("0.1.0", message);
        }

        [Test]
        public void DetermineAboutMessage_NullJson_ReturnsNotFoundMessage()
        {
            string message = TsAbout.DetermineAboutMessage(null);

            StringAssert.Contains("not found", message);
        }

        [Test]
        public void DetermineAboutMessage_EmptyJson_ReturnsNotFoundMessage()
        {
            string message = TsAbout.DetermineAboutMessage("");

            StringAssert.Contains("not found", message);
        }

        [Test]
        public void DetermineAboutMessage_MissingVersion_ReturnsCouldNotBeParsedMessage()
        {
            string json = "{\"displayName\":\"Tsvrc Core\"}";

            string message = TsAbout.DetermineAboutMessage(json);

            StringAssert.Contains("could not be parsed", message);
        }

        [Test]
        public void DetermineAboutMessage_MalformedJson_DoesNotThrowAndReturnsCouldNotBeParsedMessage()
        {
            string message = null;
            Assert.DoesNotThrow(() => message = TsAbout.DetermineAboutMessage("not json at all"));

            StringAssert.Contains("could not be parsed", message);
        }

        [Test]
        public void DetermineAboutMessage_NoDescription_OmitsDescriptionSectionButStillHasNameAndVersion()
        {
            string json = "{\"displayName\":\"Tsvrc Core\",\"version\":\"0.1.0\"}";

            string message = TsAbout.DetermineAboutMessage(json);

            StringAssert.Contains("Tsvrc Core", message);
            StringAssert.Contains("0.1.0", message);
        }
    }
}
