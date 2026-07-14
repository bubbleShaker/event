using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary>キー正規化（日付表記揺れの吸収）に関するテスト。</summary>
public sealed class EventKeyTests
{
    [Fact]
    public void 日付の範囲表記が違っても同じキーになる()
    {
        EventItem a = MakeEvent("勉強会", "2026-06-25～26");
        EventItem b = MakeEvent("勉強会", "2026-06-25:2026-06-26");

        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void 先頭ISO日付が同じなら単一日付表記とも一致する()
    {
        EventItem ranged = MakeEvent("勉強会", "2026-06-25～26");
        EventItem single = MakeEvent("勉強会", "2026-06-25");

        Assert.Equal(ranged.Key, single.Key);
    }

    [Fact]
    public void タイトル前後の空白はキーで無視される()
    {
        EventItem padded = MakeEvent("  勉強会  ", "2026-06-25");
        EventItem trimmed = MakeEvent("勉強会", "2026-06-25");

        Assert.Equal(trimmed.Key, padded.Key);
    }

    [Fact]
    public void 解析不能な日付は生文字列がキーに使われる()
    {
        // TBD 同士は一致し、別の未定表記とは区別される。
        EventItem tbd1 = MakeEvent("日程未定", "TBD");
        EventItem tbd2 = MakeEvent("日程未定", "TBD");
        EventItem na = MakeEvent("日程未定", "N/A");

        Assert.Equal(tbd1.Key, tbd2.Key);
        Assert.NotEqual(tbd1.Key, na.Key);
    }

    [Fact]
    public void 全角半角の括弧違いは同じキーになる()
    {
        // 実データで観測: "Online Math Contest（OMC）" ↔ "Online Math Contest (OMC)"。
        EventItem full = MakeEvent("Online Math Contest（OMC）", "2026-06-25");
        EventItem half = MakeEvent("Online Math Contest (OMC)", "2026-06-25");

        Assert.Equal(full.Key, half.Key);
    }

    [Fact]
    public void タイトル中の空白の有無は同じキーになる()
    {
        // 実データで観測: "サイエンスカフェ in 桐生3" ↔ "サイエンスカフェin桐生3"。
        EventItem spaced = MakeEvent("サイエンスカフェ in 桐生3", "2026-06-25");
        EventItem packed = MakeEvent("サイエンスカフェin桐生3", "2026-06-25");

        Assert.Equal(spaced.Key, packed.Key);
    }

    [Fact]
    public void 年号の有無は同じキーになる()
    {
        // 実データで観測: "JMO夏季セミナー 2026" ↔ "JMO夏季セミナー"。
        EventItem withYear = MakeEvent("JMO夏季セミナー 2026", "2026-06-25");
        EventItem without = MakeEvent("JMO夏季セミナー", "2026-06-25");

        Assert.Equal(withYear.Key, without.Key);
    }

    [Fact]
    public void 別イベントは正規化しても別キーのまま()
    {
        // 同じ日でも都市が違えば別イベント。過剰マージしないことを保証する。
        EventItem otemachi = MakeEvent("第23回御徒町ミネラルマルシェ", "2026-06-25");
        EventItem hiroshima = MakeEvent("第11回広島ミネラルマルシェ", "2026-06-25");

        Assert.NotEqual(otemachi.Key, hiroshima.Key);
    }

    [Fact]
    public void 表記違いの重複はマージで1件にまとまる()
    {
        DateOnly today = new(2026, 6, 21);
        EventItem prev = MakeEvent("勉強会", "2026-06-25～26") with { Location = "Online" };
        EventItem curr = MakeEvent("勉強会", "2026-06-25:2026-06-26") with { Location = "幕張" };

        IReadOnlyList<EventItem> result =
            new SnapshotReconciler().Reconcile([prev], [curr], today);

        EventItem merged = Assert.Single(result);
        Assert.Equal("幕張", merged.Location);
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
