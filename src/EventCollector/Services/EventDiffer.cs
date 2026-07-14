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
        // ToDictionary は同一キーが並ぶと例外を投げる。タイトル正規化で稀に別表記が同一キーへ
        // 畳まれるため、後勝ちで重複を吸収する（過去スナップショットにも同一キーが混在しうる）。
        Dictionary<string, EventItem> previousByKey = ToLastWins(previous);
        Dictionary<string, EventItem> currentByKey = ToLastWins(current);

        List<EventItem> added = [];
        List<EventItem> changed = [];
        List<EventItem> removed = [];

        // キー単位で 1 度だけ処理するため、生リストではなく畳んだ値を走査する。
        foreach (EventItem item in currentByKey.Values)
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

        foreach (EventItem item in previousByKey.Values)
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

    // キーで畳んだ辞書を作る。同一キーは後勝ちで上書きし、重複キー例外を避ける。
    private static Dictionary<string, EventItem> ToLastWins(IReadOnlyList<EventItem> items)
    {
        Dictionary<string, EventItem> map = [];
        foreach (EventItem item in items)
        {
            map[item.Key] = item;
        }

        return map;
    }
}
