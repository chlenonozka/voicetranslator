using FluentAssertions;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;

namespace VoiceTranslator.UnitTests.Sessions;

public sealed class TranslationSessionTests
{
    [Fact]
    public void StartRequiresSpeakerConsent()
    {
        TranslationSession session =
            TranslationSession.Create(TargetLanguage.English);

        Action act = session.Start;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*consent*");
    }

    [Fact]
    public void StartRequiresReadyState()
    {
        TranslationSession session =
            TranslationSession.Create(TargetLanguage.English);
        session.GrantSpeakerConsent(DateTimeOffset.UtcNow);

        Action act = session.Start;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ready*");
    }

    [Fact]
    public void StopClearsActiveState()
    {
        TranslationSession session =
            TranslationSession.Create(TargetLanguage.English);
        session.GrantSpeakerConsent(DateTimeOffset.UtcNow);
        session.MarkReady();
        session.Start();

        session.Stop();

        session.State.Should().Be(SessionState.Stopped);
    }
}
