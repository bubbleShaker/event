using System.Net;
using EventCollector.Models;
using EventCollector.Notifications;
using Xunit;

namespace EventCollector.Tests;

/// <summary>通知実装が「実際に送信したか」を戻り値で正しく伝えるかの単体テスト。</summary>
public sealed class NotifierResultTests
{
    [Fact]
    public async Task NullNotifier_送信しないのでfalseを返す()
    {
        bool sent = await new NullNotifier().NotifyAsync(SampleDiff(), DateTimeOffset.Now);

        Assert.False(sent);
    }

    [Fact]
    public async Task DiscordNotifier_送信成功でtrueを返す()
    {
        // 実 HTTP を打たずに 204 を返すスタブハンドラへ差し替える。
        using HttpClient stubClient = new(new StubHandler(HttpStatusCode.NoContent));
        DiscordNotifier notifier = new("https://discord.test/webhook", stubClient);

        bool sent = await notifier.NotifyAsync(SampleDiff(), DateTimeOffset.Now);

        Assert.True(sent);
    }

    private static DiffResult SampleDiff()
    {
        return new DiffResult
        {
            Added = [new EventItem
            {
                Title = "イベントA",
                Date = "2026-07-01",
                Location = "Online",
                Url = "https://example.com",
                Theme = "C# / .NET",
                Summary = "テスト用イベント。",
            }],
            Changed = [],
            Removed = [],
        };
    }

    /// <summary>指定したステータスを返すだけの HttpMessageHandler スタブ。</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
