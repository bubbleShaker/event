namespace EventCollector.Models;

/// <summary>収集した1件のイベント情報。</summary>
public sealed record EventItem
{
    /// <summary>イベント名。</summary>
    public required string Title { get; init; }

    /// <summary>開催日（ISO 8601。未定の場合は "TBD"）。</summary>
    public required string Date { get; init; }

    /// <summary>開催地。オンラインの場合は "Online"、不明なら "N/A"。</summary>
    public required string Location { get; init; }

    /// <summary>イベント詳細URL。不明なら "N/A"。</summary>
    public required string Url { get; init; }

    /// <summary>該当する収集テーマ。</summary>
    public required string Theme { get; init; }

    /// <summary>
    /// このイベントが属する収集グループ名（<c>themes.md</c> の <c>## 見出し</c>）。
    /// カレンダー登録時の色分けの軸に使う。<see cref="Theme"/> は web_search が表記を揺らす自由記述だが、
    /// こちらは人間が管理する安定した単位なので色の割り当てキーに向く。未設定なら既定色になる。
    /// <see cref="Key"/> には含めないため、差分検知・カレンダー冪等 upsert には影響しない。
    /// </summary>
    public string? Group { get; init; }

    /// <summary>1〜2文の概要。</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// 開催開始日時（タイムゾーン付き）。分単位まで確定している源（AtCoder 等）だけが設定する。
    /// null のイベントは日付のみ（web_search 系）で、カレンダーには終日イベントとして登録される。
    /// </summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>開催終了日時（タイムゾーン付き）。<see cref="StartsAt"/> とセットで設定する。</summary>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>
    /// 差分検知・カレンダー冪等 upsert に使う一意キー（正規化したイベント名 + 正規化した開催日）。
    /// <see cref="Title"/> は web_search が表記を揺らす（全角/半角・空白・年号・記号の差）ため、
    /// そのままキーにするとカレンダーが重複登録される。<see cref="EventTitle.Normalize"/> で表記差を畳む。
    /// 日付もモデルが表記を揺らす（例 <c>2026-06-25～26</c> / <c>2026-06-25:2026-06-26</c>）ため、
    /// 先頭 ISO 日付に正規化する。表示用の <see cref="Title"/> / <see cref="Date"/> は生のまま保持する。
    /// </summary>
    public string Key => $"{EventTitle.Normalize(Title)}|{EventDate.Normalize(Date)}";
}

/// <summary>Claude から受け取る収集結果のルート。構造化出力のスキーマに対応する。</summary>
public sealed record CollectionResult
{
    /// <summary>収集したイベントの一覧。</summary>
    public required IReadOnlyList<EventItem> Events { get; init; }
}

/// <summary>前回スナップショットとの差分。</summary>
public sealed record DiffResult
{
    /// <summary>新規に追加されたイベント。</summary>
    public required IReadOnlyList<EventItem> Added { get; init; }

    /// <summary>キーは同じだが内容が変わったイベント。</summary>
    public required IReadOnlyList<EventItem> Changed { get; init; }

    /// <summary>前回あったが今回消えたイベント。</summary>
    public required IReadOnlyList<EventItem> Removed { get; init; }

    /// <summary>追加・変更・削除のいずれかがあるか。</summary>
    public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;
}
