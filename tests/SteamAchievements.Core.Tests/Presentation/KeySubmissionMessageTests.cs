using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class KeySubmissionMessageTests
{
    [Fact]
    public void MalformedSaysNoRequestWasMade()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Malformed);

        Assert.Equal(NoticeSeverity.Warning, message.Severity);
        Assert.Contains("32", message.Body);
        Assert.Contains("Nothing was sent", message.Body);
    }

    [Fact]
    public void RejectedIsTheOnlyOutcomeThatAsksForAnotherKey()
    {
        var rejected = KeySubmissionMessage.For(KeySubmission.Rejected);
        var unreachable = KeySubmissionMessage.For(KeySubmission.Unreachable);

        Assert.Equal(NoticeSeverity.Danger, rejected.Severity);
        Assert.Contains("another key", rejected.Body);
        Assert.DoesNotContain("another key", unreachable.Body);
    }

    [Fact]
    public void UnreachableAdvisesRetrying()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Unreachable);

        Assert.Equal(NoticeSeverity.Warning, message.Severity);
        Assert.Contains("try again", message.Body);
    }

    [Fact]
    public void AcceptedConfirmsTheKeyWasStored()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Accepted);

        Assert.Equal(NoticeSeverity.Info, message.Severity);
        Assert.Contains("stored", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A new outcome must not reach a screen as an empty notice or an
    /// exception thrown inside a render.
    /// </summary>
    [Fact]
    public void EveryOutcomeHasCopy()
    {
        foreach (var outcome in Enum.GetValues<KeySubmission>())
        {
            var message = KeySubmissionMessage.For(outcome);

            Assert.NotEqual("", message.Title);
            Assert.NotEqual("", message.Body);
        }
    }
}
