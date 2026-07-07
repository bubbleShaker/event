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

        CollectionRunResult result = await new EventSourceRunner().CollectAllAsync(sources);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
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

        CollectionRunResult result = await runner.CollectAllAsync(sources);

        Assert.Equal(2, result.Events.Count); // A と C は生き残る
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Single(errors); // B の失敗が1件記録される
        Assert.Contains("[B]", errors[0]);
    }

    [Fact]
    public async Task キャンセルは失敗分離せず伝播する()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sources = new IEventSource[]
        {
            new CancelObservingSource("A"),
        };

        List<string> errors = [];
        var runner = new EventSourceRunner(logError: errors.Add);

        // キャンセル済みトークンでは「収集失敗（スキップ）」にせず例外を伝播する。
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.CollectAllAsync(sources, cts.Token));
        Assert.Empty(errors);
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

        CollectionRunResult result = await new EventSourceRunner().CollectAllAsync(sources);

        Assert.Single(result.Events);
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

    /// <summary>渡されたトークンのキャンセルを尊重する収集源。CT 伝播の検証用。</summary>
    private sealed class CancelObservingSource(string name) : IEventSource
    {
        public string Name => name;

        public Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<EventItem>>([]);
        }
    }
}
