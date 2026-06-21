using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// 前回スナップショットと今回の収集結果を和集合マージし、新しいスナップショットを作る。
/// 収集は非決定的なため「今回未ヒット」だけでは消さず、<b>開催日が過ぎたものだけ</b>を除外する。
/// </summary>
public sealed class SnapshotReconciler
{
    /// <summary>
    /// 前回分と今回分をマージする。同一キーは今回分（<paramref name="current"/>）で上書きし、
    /// 過去日のイベントは双方から除外する。結果は (Date, Title) で決定的に並べ替えて返す。
    /// </summary>
    /// <param name="previous">前回のスナップショット。</param>
    /// <param name="current">今回の収集結果。</param>
    /// <param name="today">基準日（これより前に終わったイベントを過去とみなす）。</param>
    /// <returns>新しいスナップショット。</returns>
    public IReadOnlyList<EventItem> Reconcile(
        IReadOnlyList<EventItem> previous,
        IReadOnlyList<EventItem> current,
        DateOnly today)
    {
        Dictionary<string, EventItem> merged = [];

        // 前回分のうち未来（または日付不明）のものを土台にする。
        foreach (EventItem item in previous)
        {
            if (!IsPast(item, today))
            {
                merged[item.Key] = item;
            }
        }

        // 今回分で upsert。同キーは今回の内容で上書きされ、変更が反映される。
        foreach (EventItem item in current)
        {
            if (!IsPast(item, today))
            {
                merged[item.Key] = item;
            }
        }

        return
        [
            .. merged.Values
                .OrderBy(e => e.Date, StringComparer.Ordinal)
                .ThenBy(e => e.Title, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// イベントの開催日が <paramref name="today"/> より前なら過去とみなす。
    /// 先頭の <c>yyyy-MM-dd</c> のみを判定に使い、<c>TBD</c> 等の解析不能な日付は過去としない（保持する）。
    /// </summary>
    internal static bool IsPast(EventItem item, DateOnly today)
    {
        // 解析できない（TBD / N/A など）場合は安全側に倒して保持する（過去としない）。
        return EventDate.TryGetStartDate(item.Date, out DateOnly date) && date < today;
    }
}
