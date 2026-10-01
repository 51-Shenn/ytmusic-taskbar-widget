using Xunit;
using System.Net;
using System.Text;
using YTMTaskbarWidget.Services;
namespace YTMTaskbarWidget.Tests;
sealed class StubHandler : HttpMessageHandler
{
    private readonly string _json; private readonly HttpStatusCode _code;
    public StubHandler(string json, HttpStatusCode code = HttpStatusCode.OK) { _json = json; _code = code; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
}
public sealed class LrclibClientTests
{
    [Fact] public async Task Returns_Synced_Lyrics_On_Hit()
    {
        const string json = """{"id":1,"trackName":"T","artistName":"A","syncedLyrics":"[00:10.00]hi\n","plainLyrics":"hi"}""";
        var client = new LrclibClient(new HttpClient(new StubHandler(json)));
        var r = await client.GetAsync("T", "A", TimeSpan.FromMinutes(3));
        Assert.NotNull(r); Assert.Single(r!.Lines); Assert.Equal("hi", r.Lines[0].Text);
    }
    [Fact] public async Task Returns_Null_On_404()
    {
        var client = new LrclibClient(new HttpClient(new StubHandler("{}", HttpStatusCode.NotFound)));
        Assert.Null(await client.GetAsync("T", "A", null));
    }
}
