using EventCollector.Models;
using EventCollector.Notifications;
using Xunit;

namespace EventCollector.Tests;

/// <summary><see cref="DiscordMessageBuilder"/> の単体テスト。</summary>
public sealed class DiscordMessageBuilderTests
{
    [Fact]
    public void Build_件数と各イベント名を含む()
    {
        DiffResult diff = new()
        {
            Added = [MakeEvent("新規イベントA")],
            Changed = [MakeEvent("変更イベントB")],
            Removed = [],
        };

        string content = new DiscordMessageBuilder()
            .Build(diff, DateTimeOffset.Parse("2026-06-21T09:52:00"));

        Assert.Contains("追加 1 / 変更 1 / 削除 0", content);
        Assert.Contains("新規イベントA", content);
        Assert.Contains("変更イベントB", content);
    }

    [Fact]
    public void Build_Discordの上限に収める()
    {
        List<EventItem> many = [.. Enumerable.Range(0, 500).Select(i => MakeEvent($"イベント{i}"))];
        DiffResult diff = new()
        {
            Added = many,
            Changed = [],
            Removed = [],
        };

        string content = new DiscordMessageBuilder().Build(diff, DateTimeOffset.Now);

        Assert.True(content.Length <= DiscordMessageBuilder.MaxContentLength);
    }

    private static EventItem MakeEvent(string title)
    {
        return new EventItem
        {
            Title = title,
            Date = "2026-07-01",
            Location = "Online",
            Url = "https://example.com",
            Theme = "C# / .NET",
            Summary = "テスト用イベント。",
        };
    }
}
