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
    /// 先に現れたものを採用し、収集源をまたいだ同一イベントの重複を防ぐ。
    /// </summary>
    private static IReadOnlyList<EventItem> Deduplicate(IReadOnlyList<EventItem> items)
    {
        HashSet<string> seen = [];
        List<EventItem> unique = [];
        foreach (EventItem item in items)
        {
            if (seen.Add(item.Key))
            {
                unique.Add(item);
            }
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
