using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>前回スナップショットと今回の収集結果を比較する。</summary>
public sealed class EventDiffer
{
    /// <summary>キー（イベント名 + 開催日）単位で追加・変更・削除を抽出する。</summary>
    /// <param name="previous">前回のイベント一覧。</param>
    /// <param name="current">今回のイベント一覧。</param>
    /// <returns>差分結果。</returns>
    public DiffResult Diff(IReadOnlyList<EventItem> previous, IReadOnlyList<EventItem> current)
    {
        Dictionary<string, EventItem> previousByKey = previous.ToDictionary(e => e.Key);
        Dictionary<string, EventItem> currentByKey = current.ToDictionary(e => e.Key);

        List<EventItem> added = [];
        List<EventItem> changed = [];
        List<EventItem> removed = [];

        foreach (EventItem item in current)
        {
            if (!previousByKey.TryGetValue(item.Key, out EventItem? before))
            {
                added.Add(item);
            }
            else if (before != item)
            {
                // レコードの値等価で、同一キーでも内容差があれば変更扱い。
                changed.Add(item);
            }
        }

        foreach (EventItem item in previous)
        {
            if (!currentByKey.ContainsKey(item.Key))
            {
                removed.Add(item);
            }
        }

        return new DiffResult
        {
            Added = added,
            Changed = changed,
            Removed = removed,
        };
    }
}
