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
    /// </summary>
    public static Event? TryCreate(EventItem item)
    {
        // 分単位の開始時刻が確定していれば、時刻付き（start–end）イベントにする。
        if (item.StartsAt is { } startsAt)
        {
            return BuildEvent(item, TimedSchedule(startsAt, item.EndsAt));
        }

        // それ以外は日付のみ。開始日が解析できなければカレンダーに置けない。
        if (!EventDate.TryGetStartDate(item.Date, out DateOnly start))
        {
            return null;
        }

        return BuildEvent(item, AllDaySchedule(start));
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
    private static Event BuildEvent(EventItem item, (EventDateTime Start, EventDateTime End) schedule) =>
        new()
        {
            Id = CalendarEventId.FromKey(item.Key),
            Summary = item.Title,
            Location = item.Location,
            Description = BuildDescription(item),
            Start = schedule.Start,
            End = schedule.End,
        };

    private static string BuildDescription(EventItem item)
    {
        // 概要・URL・収集テーマをカレンダーの説明欄にまとめる。
        return $"{item.Summary}\n\n{item.Url}\n\n[テーマ] {item.Theme}";
    }
}
