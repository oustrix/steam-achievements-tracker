using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamCommunityClientTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static SteamCommunityClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://steamcommunity.com/") });

    [Fact]
    public async Task ReadsTheNameAndAvatarOutOfTheProfileDocument()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));

        var profile = await client.GetProfileAsync(SteamId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(SteamId, profile.SteamId64);
        Assert.Equal("oustrix", profile.PersonaName);
        Assert.EndsWith("_full.jpg", profile.AvatarUrl);
    }

    [Fact]
    public async Task AsksTheCommunitySiteForTheXmlVariantOfTheProfile()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml");

        await Client(handler).GetProfileAsync(SteamId, CancellationToken.None);

        var requested = handler.Requests.Single().ToString();
        Assert.Contains($"/profiles/{SteamId}/", requested);
        Assert.Contains("xml=1", requested);
    }

    [Fact]
    public async Task ReturnsNullForAProfileThatDoesNotExistEvenThoughSteamAnswersTwoHundred()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_not_found.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullWhenTheCommunitySiteServesHtml()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.OK, "<html><body>Sign In</body></html>", "text/html"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullOnAnErrorStatus()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.ServiceUnavailable, string.Empty, "text/html"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullWhenTheNetworkIsDown()
    {
        var client = Client(new FakeHttpMessageHandler(_ => throw new HttpRequestException("no route")));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task StillHonoursCancellation()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetProfileAsync(SteamId, cancelled.Token));
    }
}
