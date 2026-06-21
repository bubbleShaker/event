using EventCollector.Calendar;
using EventCollector.Models;
using Google.Apis.Calendar.v3.Data;
using Xunit;

namespace EventCollector.Tests;

/// <summary>カレンダー連携の純粋ロジック（ID 生成・イベント変換・Null 実装）のテスト。</summary>
public sealed class CalendarTests
{
    [Fact]
    public void CalendarEventId_同じキーなら同じIDになる()
    {
        Assert.Equal(CalendarEventId.FromKey("勉強会|2026-06-25"), CalendarEventId.FromKey("勉強会|2026-06-25"));
    }

    [Fact]
    public void CalendarEventId_違うキーなら違うIDになる()
    {
        Assert.NotEqual(CalendarEventId.FromKey("A|2026-06-25"), CalendarEventId.FromKey("B|2026-06-25"));
    }

    [Fact]
    public void CalendarEventId_base32hexの文字だけで構成される()
    {
        string id = CalendarEventId.FromKey("勉強会|2026-06-25");

        Assert.True(id.Length is >= 5 and <= 1024);
        Assert.All(id, c => Assert.Contains(c, "0123456789abcdefghijklmnopqrstuv"));
    }

    [Fact]
    public void Factory_終日イベントへ変換しendは翌日になる()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("勉強会", "2026-06-25"));

        Assert.NotNull(ev);
        Assert.Equal("勉強会", ev!.Summary);
        Assert.Equal("2026-06-25", ev.Start.Date);
        Assert.Equal("2026-06-26", ev.End.Date); // end.date は排他的（翌日）
    }

    [Fact]
    public void Factory_範囲表記でも先頭日付を開始日にする()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("勉強会", "2026-06-25～26"));

        Assert.NotNull(ev);
        Assert.Equal("2026-06-25", ev!.Start.Date);
    }

    [Fact]
    public void Factory_日付不明はnullでスキップ()
    {
        Assert.Null(CalendarEventFactory.TryCreate(MakeEvent("日程未定", "TBD")));
    }

    [Fact]
    public async Task NullCalendarSink_何も登録せず0を返す()
    {
        int registered = await new NullCalendarSink().SyncAsync([MakeEvent("勉強会", "2026-06-25")]);

        Assert.Equal(0, registered);
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
