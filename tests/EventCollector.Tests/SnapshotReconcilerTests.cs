using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary><see cref="SnapshotReconciler"/> の単体テスト。</summary>
public sealed class SnapshotReconcilerTests
{
    private static readonly DateOnly Today = new(2026, 6, 21);

    [Fact]
    public void 未来イベントは今回未ヒットでも保持される()
    {
        // 前回はあったが今回の収集に含まれない、まだ開催前のイベント。
        EventItem future = MakeEvent("未来イベント", "2026-09-01");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([future], [], Today);

        Assert.Contains(result, e => e.Key == future.Key);
    }

    [Fact]
    public void 過去日のイベントは除外される()
    {
        EventItem past = MakeEvent("過去イベント", "2026-06-20");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([past], [], Today);

        Assert.DoesNotContain(result, e => e.Key == past.Key);
    }

    [Fact]
    public void 当日のイベントは保持される()
    {
        EventItem todayEvent = MakeEvent("当日イベント", "2026-06-21");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([todayEvent], [], Today);

        Assert.Contains(result, e => e.Key == todayEvent.Key);
    }

    [Fact]
    public void 今回の新規イベントは追加される()
    {
        EventItem added = MakeEvent("新規", "2026-08-01");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([], [added], Today);

        Assert.Contains(result, e => e.Key == added.Key);
    }

    [Fact]
    public void 同一キーは今回の内容で上書きされる()
    {
        EventItem before = MakeEvent("勉強会", "2026-07-10") with { Location = "Online" };
        EventItem after = MakeEvent("勉強会", "2026-07-10") with { Location = "東京" };

        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([before], [after], Today);

        EventItem merged = Assert.Single(result);
        Assert.Equal("東京", merged.Location);
    }

    [Fact]
    public void 解析不能な日付は保持される()
    {
        EventItem tbd = MakeEvent("日程未定イベント", "TBD");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([tbd], [], Today);

        Assert.Contains(result, e => e.Key == tbd.Key);
    }

    [Fact]
    public void 範囲表記は先頭日付で過去判定する()
    {
        // "2026-06-25～26" は先頭 2026-06-25 が today(06-21) より未来 → 保持。
        EventItem range = MakeEvent("範囲イベント", "2026-06-25～26");
        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([range], [], Today);

        Assert.Contains(result, e => e.Key == range.Key);
    }

    [Fact]
    public void Date昇順で決定的に並ぶ()
    {
        EventItem later = MakeEvent("後", "2026-09-01");
        EventItem earlier = MakeEvent("先", "2026-07-01");

        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([later, earlier], [], Today);

        Assert.Equal("先", result[0].Title);
        Assert.Equal("後", result[1].Title);
    }

    private static EventItem MakeEvent(string title, string date)
    {
        return new EventItem
        {
            Title = title,
            Date = date,
            Location = "Online",
            Url = "https://example.com",
            Theme = "テスト",
            Summary = "テスト用イベント。",
        };
    }
}
