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

    /// <summary>1〜2文の概要。</summary>
    public required string Summary { get; init; }

    /// <summary>差分検知に使う一意キー（イベント名 + 開催日）。</summary>
    public string Key => $"{Title}|{Date}";
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
