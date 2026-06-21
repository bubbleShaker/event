using EventCollector.Models;
using Google.Apis.Calendar.v3.Data;

namespace EventCollector.Calendar;

/// <summary>
/// 収集した <see cref="EventItem"/> を Google カレンダーの終日イベント（<see cref="Event"/>）へ変換する純粋関数。
/// 日付が解析できないイベント（TBD 等）はカレンダーに置けないため <c>null</c> を返す。
/// </summary>
public static class CalendarEventFactory
{
    /// <summary><see cref="EventItem"/> を終日イベントへ変換する。日付不明なら <c>null</c>。</summary>
    public static Event? TryCreate(EventItem item)
    {
        if (!EventDate.TryGetStartDate(item.Date, out DateOnly start))
        {
            return null;
        }

        // 終日イベントは start.date / end.date を "yyyy-MM-dd" 文字列で持ち、end は排他的（翌日）にする。
        string startText = start.ToString("yyyy-MM-dd");
        string endText = start.AddDays(1).ToString("yyyy-MM-dd");

        return new Event
        {
            Id = CalendarEventId.FromKey(item.Key),
            Summary = item.Title,
            Location = item.Location,
            Description = BuildDescription(item),
            Start = new EventDateTime { Date = startText },
            End = new EventDateTime { Date = endText },
        };
    }

    private static string BuildDescription(EventItem item)
    {
        // 概要・URL・収集テーマをカレンダーの説明欄にまとめる。
        return $"{item.Summary}\n\n{item.Url}\n\n[テーマ] {item.Theme}";
    }
}
