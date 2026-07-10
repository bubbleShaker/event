using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// 複数の <see cref="IEventSource"/> を順に実行し、結果をマージ・重複除去する。
/// 各収集源は独立して実行し、1つが失敗しても他の収集は続行する（失敗分離）。
/// これにより特定分野の検索失敗が全体を巻き込むのを防ぐ。
/// </summary>
public sealed class EventSourceRunner
{
    private readonly Action<string>? _log;
    private readonly Action<string>? _logError;

    /// <summary>ログ出力を差し込む。省略時は無出力（テスト用）。</summary>
    /// <param name="log">通常ログ（収集源ごとの件数など）。</param>
    /// <param name="logError">エラーログ（収集源の失敗）。</param>
    public EventSourceRunner(Action<string>? log = null, Action<string>? logError = null)
    {
        _log = log;
        _logError = logError;
    }

    /// <summary>全収集源を実行し、重複除去したイベントと成功/失敗数を返す。</summary>
    /// <param name="sources">実行する収集源。</param>
    /// <param name="cancellationToken">キャンセル用トークン。キャンセル時は途中で中断し例外を伝播する。</param>
    /// <returns>重複除去したイベントと、成功・失敗した収集源数。</returns>
    /// <remarks>
    /// 収集源は直列に実行する。各源が独立した Claude API 呼び出しを行うため、
    /// 並列化するとレート制限に触れやすい。レイテンシより堅牢さを優先している。
    /// </remarks>
    public async Task<CollectionRunResult> CollectAllAsync(
        IReadOnlyList<IEventSource> sources,
        CancellationToken cancellationToken = default)
    {
        List<EventItem> collected = [];
        int succeeded = 0;
        int failed = 0;

        foreach (IEventSource source in sources)
        {
            try
            {
                IReadOnlyList<EventItem> items = await source.CollectAsync(cancellationToken);
                collected.AddRange(items);
                succeeded++;
                _log?.Invoke($"[{source.Name}] {items.Count} 件収集");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // キャンセル（タイムアウト等）は失敗分離の対象外。契約どおり上位へ伝播する。
                throw;
            }
            catch (Exception ex)
            {
                // 収集源単位で失敗を握りつぶし、他の収集源は続行する。
                failed++;
                _logError?.Invoke($"[{source.Name}] 収集失敗（スキップ）: {ex.Message}");
            }
        }

        return new CollectionRunResult(Deduplicate(collected), succeeded, failed);
    }

    /// <summary>
    /// <see cref="EventItem.Key"/>（イベント名＋正規化日付）で重複を除去する。
    /// 基本は先に現れたものを採用するが、キーが衝突したとき後続が
    /// <see cref="EventItem.StartsAt"/>（確定した開始時刻）を持ち既存が持たない場合は差し替える。
    /// これにより web_search（時刻なし）と AtCoder API（時刻あり）が同一コンテストを返しても、
    /// 収集源の並び順に依存せず時刻付きイベントとしてカレンダーに載る。
    /// 出現順は最初に採用した位置を保ち、差し替え時は中身だけ入れ替える。
    /// </summary>
    private static IReadOnlyList<EventItem> Deduplicate(IReadOnlyList<EventItem> items)
    {
        Dictionary<string, int> indexByKey = [];
        List<EventItem> unique = [];
        foreach (EventItem item in items)
        {
            if (indexByKey.TryGetValue(item.Key, out int existingIndex))
            {
                // 既存が時刻を持たず、今回が時刻を持つなら、確定情報を持つ側へレコードごと差し替える
                // （時刻だけの移植ではなく、URL・概要も確定源の値を採用する）。
                // 逆向き（時刻あり→なし）と、両方が時刻を持つ衝突は先勝ちのまま。確定情報同士は
                // 最初に採った源を信頼し、後続の揺らぎで上書きしない。
                if (unique[existingIndex].StartsAt is null && item.StartsAt is not null)
                {
                    unique[existingIndex] = item;
                }

                continue;
            }

            indexByKey[item.Key] = unique.Count;
            unique.Add(item);
        }

        return unique;
    }
}

/// <summary>複数収集源の実行結果。イベント本体と成功/失敗した収集源数を持つ。</summary>
/// <param name="Events">重複除去済みのイベント一覧。</param>
/// <param name="Succeeded">正常終了した収集源の数（0件でも成功に含む）。</param>
/// <param name="Failed">例外でスキップした収集源の数。</param>
public sealed record CollectionRunResult(
    IReadOnlyList<EventItem> Events,
    int Succeeded,
    int Failed);
