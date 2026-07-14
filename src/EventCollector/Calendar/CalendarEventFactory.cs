using EventCollector.Models;
using Google.Apis.Calendar.v3.Data;

namespace EventCollector.Calendar;

/// <summary>
/// 収集した <see cref="EventItem"/> を Google カレンダーのイベント（<see cref="Event"/>）へ変換する純粋関数。
/// 分単位の開始時刻を持つ源（AtCoder 等）は時刻付きイベントに、日付しか無い源は終日イベントにする。
/// 日付が解析できないイベント（TBD 等）はカレンダーに置けないため <c>null</c> を返す。
/// </summary>
public static class CalendarEventFactory
{
    /// <summary>
    /// <see cref="EventItem"/> をカレンダーイベントへ変換する。
    /// <see cref="EventItem.StartsAt"/> があれば時刻付き、無ければ終日。日付不明なら <c>null</c>。
    /// <paramref name="palette"/> を渡すと <see cref="EventItem.Group"/> に応じた色（colorId）を付ける。
    /// </summary>
    public static Event? TryCreate(EventItem item, ThemeColorPalette? palette = null)
    {
        // 分単位の開始時刻が確定していれば、時刻付き（start–end）イベントにする（常に日精度＝概算でない）。
        if (item.StartsAt is { } startsAt)
        {
            return BuildEvent(item, TimedSchedule(startsAt, item.EndsAt), approximate: false, palette);
        }

        // それ以外は日付のみ。日精度が無ければ月精度（その月の1日）で概算登録する。
        // どちらも取れなければ（TBD/N/A）カレンダーに置けない。
        if (!EventDate.TryGetCalendarStartDate(item.Date, out DateOnly start, out bool approximate))
        {
            return null;
        }

        return BuildEvent(item, AllDaySchedule(start), approximate, palette);
    }

    // 時刻付きの開始・終了。終了が無ければ 1 時間後を仮に置き、長さ 0 の予定を避ける。
    private static (EventDateTime Start, EventDateTime End) TimedSchedule(
        DateTimeOffset startsAt, DateTimeOffset? endsAt) =>
        (new EventDateTime { DateTimeDateTimeOffset = startsAt },
         new EventDateTime { DateTimeDateTimeOffset = endsAt ?? startsAt.AddHours(1) });

    // 終日イベントは start.date / end.date を "yyyy-MM-dd" 文字列で持ち、end は排他的（翌日）にする。
    private static (EventDateTime Start, EventDateTime End) AllDaySchedule(DateOnly start) =>
        (new EventDateTime { Date = start.ToString("yyyy-MM-dd") },
         new EventDateTime { Date = start.AddDays(1).ToString("yyyy-MM-dd") });

    // 開始・終了以外は時刻付き/終日で共通。冪等 upsert のため ID はキーから決定的に作る。
    // テーマグループに対応する色（colorId）を付ける。palette 無し/未知グループなら色は付けない（既定色）。
    private static Event BuildEvent(
        EventItem item, (EventDateTime Start, EventDateTime End) schedule, bool approximate,
        ThemeColorPalette? palette) =>
        new()
        {
            Id = CalendarEventId.FromKey(item.Key),
            Summary = item.Title,
            Location = item.Location,
            Description = BuildDescription(item, approximate),
            Start = schedule.Start,
            End = schedule.End,
            ColorId = palette?.ColorIdFor(item.Group),
        };

    private static string BuildDescription(EventItem item, bool approximate)
    {
        // 概要・URL・収集テーマをカレンダーの説明欄にまとめる。
        // 月精度で概算登録した場合は、開催日が確定でない旨を先頭に明示する。
        string note = approximate ? "※開催日は月のみ判明（概算で月初に配置）。公式情報で要確認。\n\n" : "";
        return $"{note}{item.Summary}\n\n{item.Url}\n\n[テーマ] {item.Theme}";
    }
}
