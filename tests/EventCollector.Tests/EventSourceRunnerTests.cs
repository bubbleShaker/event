using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary><see cref="EventSourceRunner"/> の失敗分離・重複除去に関するテスト。</summary>
public sealed class EventSourceRunnerTests
{
    [Fact]
    public async Task 全収集源の結果をマージする()
    {
        var sources = new IEventSource[]
        {
            new FakeSource("A", [Event("勉強会X", "2026-08-01")]),
            new FakeSource("B", [Event("勉強会Y", "2026-08-02")]),
        };

        IReadOnlyList<EventItem> result = await new EventSourceRunner().CollectAllAsync(sources);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task 一つの収集源が失敗しても他は収集される()
    {
        var sources = new IEventSource[]
        {
            new FakeSource("A", [Event("勉強会X", "2026-08-01")]),
            new ThrowingSource("B"),
            new FakeSource("C", [Event("勉強会Z", "2026-08-03")]),
        };

        List<string> errors = [];
        var runner = new EventSourceRunner(logError: errors.Add);

        IReadOnlyList<EventItem> result = await runner.CollectAllAsync(sources);

        Assert.Equal(2, result.Count); // A と C は生き残る
        Assert.Single(errors); // B の失敗が1件記録される
        Assert.Contains("[B]", errors[0]);
    }

    [Fact]
    public async Task 収集源をまたいだ同一イベントは重複除去される()
    {
        var sources = new IEventSource[]
        {
            new FakeSource("A", [Event("合同勉強会", "2026-08-01")]),
            // タイトル同じ・日付の範囲表記違い → Key 正規化で同一とみなす。
            new FakeSource("B", [Event("合同勉強会", "2026-08-01～02")]),
        };

        IReadOnlyList<EventItem> result = await new EventSourceRunner().CollectAllAsync(sources);

        Assert.Single(result);
    }

    private static EventItem Event(string title, string date) => new()
    {
        Title = title,
        Date = date,
        Location = "Online",
        Url = "N/A",
        Theme = "test",
        Summary = "テスト用イベント",
    };

    /// <summary>固定のイベントを返す収集源。</summary>
    private sealed class FakeSource(string name, IReadOnlyList<EventItem> items) : IEventSource
    {
        public string Name => name;

        public Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(items);
    }

    /// <summary>必ず例外を投げる収集源。失敗分離の検証用。</summary>
    private sealed class ThrowingSource(string name) : IEventSource
    {
        public string Name => name;

        public Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("収集に失敗");
    }
}
