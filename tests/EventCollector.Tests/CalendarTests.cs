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
    public void Factory_月のみ表記は月初で登録し概算注記を付ける()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("月だけ判明イベント", "2026-07"));

        Assert.NotNull(ev);
        Assert.Equal("2026-07-01", ev!.Start.Date);   // 月初で概算配置
        Assert.Equal("2026-07-02", ev.End.Date);      // end は排他的（翌日）
        Assert.Contains("概算", ev.Description);        // 説明欄に概算の旨を明示
    }

    [Fact]
    public void Factory_年月とTBDの混在表記でも月初で登録する()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("技育CAMPハッカソン", "2026-04-TBD"));

        Assert.NotNull(ev);
        Assert.Equal("2026-04-01", ev!.Start.Date);
        Assert.Contains("概算", ev.Description);   // 月精度なので概算注記が付く
    }

    [Fact]
    public void Factory_日精度なら概算注記を付けない()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("確定イベント", "2026-06-25"));

        Assert.NotNull(ev);
        Assert.DoesNotContain("概算", ev!.Description);
    }

    [Fact]
    public void Factory_StartsAtがあれば時刻付きイベントにする()
    {
        var starts = new DateTimeOffset(2026, 7, 11, 21, 0, 0, TimeSpan.FromHours(9));
        var ends = starts.AddMinutes(100);
        EventItem item = MakeEvent("ABC466", "2026-07-11") with { StartsAt = starts, EndsAt = ends };

        Event? ev = CalendarEventFactory.TryCreate(item);

        Assert.NotNull(ev);
        // 終日イベントの Date は使わず、時刻付きの DateTime を持つ。
        Assert.Null(ev!.Start.Date);
        Assert.Equal(starts, ev.Start.DateTimeDateTimeOffset);
        Assert.Equal(ends, ev.End.DateTimeDateTimeOffset);
    }

    [Fact]
    public void Factory_EndsAt無しの時刻付きは1時間後を終了にする()
    {
        var starts = new DateTimeOffset(2026, 7, 11, 21, 0, 0, TimeSpan.FromHours(9));
        EventItem item = MakeEvent("開始のみ", "2026-07-11") with { StartsAt = starts };

        Event? ev = CalendarEventFactory.TryCreate(item);

        Assert.Equal(starts.AddHours(1), ev!.End.DateTimeDateTimeOffset);
    }

    [Fact]
    public async Task NullCalendarSink_何も登録せず0を返す()
    {
        int registered = await new NullCalendarSink().SyncAsync([MakeEvent("勉強会", "2026-06-25")]);

        Assert.Equal(0, registered);
    }

    [Fact]
    public async Task SyncCore_1件失敗しても残りを登録し成功件数を返す()
    {
        // 2件目の upsert だけ失敗させる（レート制限などの一時エラーを模擬）。
        var attempted = new List<string>();
        Task Upsert(Event ev, CancellationToken _)
        {
            attempted.Add(ev.Summary);
            if (ev.Summary == "B")
            {
                throw new InvalidOperationException("一時エラー");
            }

            return Task.CompletedTask;
        }

        int synced = await GoogleCalendarSink.SyncCoreAsync(
            [MakeEvent("A", "2026-06-25"), MakeEvent("B", "2026-06-26"), MakeEvent("C", "2026-06-27")],
            Upsert, _ => { }, CancellationToken.None);

        // 失敗した B で止まらず C まで試行し、成功は A・C の 2 件。
        Assert.Equal(["A", "B", "C"], attempted);
        Assert.Equal(2, synced);
    }

    [Fact]
    public async Task SyncCore_日付不明はupsertせず警告を出してスキップする()
    {
        var attempted = new List<string>();
        var warnings = new List<string>();
        Task Upsert(Event ev, CancellationToken _)
        {
            attempted.Add(ev.Summary);
            return Task.CompletedTask;
        }

        int synced = await GoogleCalendarSink.SyncCoreAsync(
            [MakeEvent("A", "2026-06-25"), MakeEvent("未定", "TBD")],
            Upsert, warnings.Add, CancellationToken.None);

        Assert.Equal(["A"], attempted);
        Assert.Equal(1, synced);
        // スキップは黙殺せず警告に残す（対象イベント名を含む）。
        Assert.Contains(warnings, w => w.Contains("未定") && w.Contains("スキップ"));
    }

    [Fact]
    public async Task SyncCore_トークンがキャンセル済みならバッチごと中断する()
    {
        // 本物のキャンセル（全体時間ガード）は1件スキップではなくバッチ全体を止める。
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Task Upsert(Event _, CancellationToken __) => throw new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            GoogleCalendarSink.SyncCoreAsync(
                [MakeEvent("A", "2026-06-25")], Upsert, _ => { }, cts.Token));
    }

    [Fact]
    public async Task SyncCore_未キャンセルのOperationCanceledは1件スキップして継続する()
    {
        // SDK 内部の I/O タイムアウトはトークン未キャンセルでも OperationCanceledException を投げうる。
        // これはバッチ中断ではなく、そのイベントだけスキップして残りを続行する。
        var attempted = new List<string>();
        Task Upsert(Event ev, CancellationToken _)
        {
            attempted.Add(ev.Summary);
            return ev.Summary == "A"
                ? throw new TaskCanceledException("I/O timeout")
                : Task.CompletedTask;
        }

        int synced = await GoogleCalendarSink.SyncCoreAsync(
            [MakeEvent("A", "2026-06-25"), MakeEvent("B", "2026-06-26")],
            Upsert, _ => { }, CancellationToken.None);

        Assert.Equal(["A", "B"], attempted);
        Assert.Equal(1, synced);
    }

    [Fact]
    public void Palette_異なるグループには異なる色を割り当てる()
    {
        ThemeColorPalette palette = ThemeColorPalette.FromEvents(
        [
            MakeEvent("a", "2026-06-25") with { Group = "C# / .NET" },
            MakeEvent("b", "2026-06-25") with { Group = "AWS / クラウド" },
            MakeEvent("c", "2026-06-25") with { Group = "数学 / 数理科学" },
        ]);

        string?[] colors =
        [
            palette.ColorIdFor("C# / .NET"),
            palette.ColorIdFor("AWS / クラウド"),
            palette.ColorIdFor("数学 / 数理科学"),
        ];

        Assert.All(colors, c => Assert.NotNull(c));
        // 11 グループ以内なので互いに重複しない（＝色でテーマを分けられる）。
        Assert.Equal(colors.Length, colors.Distinct().Count());
    }

    [Fact]
    public void Palette_同じグループ集合なら毎回同じ色になる()
    {
        EventItem[] events =
        [
            MakeEvent("a", "2026-06-25") with { Group = "AWS / クラウド" },
            MakeEvent("b", "2026-06-25") with { Group = "C# / .NET" },
        ];

        // イベントの並び順が変わっても、グループ名でソートするため割り当ては安定する。
        ThemeColorPalette p1 = ThemeColorPalette.FromEvents(events);
        ThemeColorPalette p2 = ThemeColorPalette.FromEvents(events.Reverse().ToArray());

        Assert.Equal(p1.ColorIdFor("C# / .NET"), p2.ColorIdFor("C# / .NET"));
        Assert.Equal(p1.ColorIdFor("AWS / クラウド"), p2.ColorIdFor("AWS / クラウド"));
    }

    [Fact]
    public void Palette_未知グループとnullは色なし()
    {
        ThemeColorPalette palette = ThemeColorPalette.FromEvents(
            [MakeEvent("a", "2026-06-25") with { Group = "C# / .NET" }]);

        Assert.Null(palette.ColorIdFor("知らないグループ"));
        Assert.Null(palette.ColorIdFor(null));
    }

    [Fact]
    public void Factory_paletteを渡すとグループに応じたColorIdが付く()
    {
        EventItem item = MakeEvent("勉強会", "2026-06-25") with { Group = "C# / .NET" };
        ThemeColorPalette palette = ThemeColorPalette.FromEvents([item]);

        Event? ev = CalendarEventFactory.TryCreate(item, palette);

        Assert.NotNull(ev);
        Assert.Equal(palette.ColorIdFor("C# / .NET"), ev!.ColorId);
    }

    [Fact]
    public void Factory_palette無しならColorIdは付かない()
    {
        Event? ev = CalendarEventFactory.TryCreate(MakeEvent("勉強会", "2026-06-25") with { Group = "C# / .NET" });

        Assert.NotNull(ev);
        Assert.Null(ev!.ColorId);
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
