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

    /// <summary>全収集源を実行し、重複除去したイベント一覧を返す。</summary>
    /// <param name="sources">実行する収集源。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>全収集源のイベントを <see cref="EventItem.Key"/> で重複除去した一覧。</returns>
    public async Task<IReadOnlyList<EventItem>> CollectAllAsync(
        IReadOnlyList<IEventSource> sources,
        CancellationToken cancellationToken = default)
    {
        List<EventItem> collected = [];

        foreach (IEventSource source in sources)
        {
            try
            {
                IReadOnlyList<EventItem> items = await source.CollectAsync(cancellationToken);
                collected.AddRange(items);
                _log?.Invoke($"[{source.Name}] {items.Count} 件収集");
            }
            catch (Exception ex)
            {
                // 収集源単位で失敗を握りつぶし、他の収集源は続行する。
                _logError?.Invoke($"[{source.Name}] 収集失敗（スキップ）: {ex.Message}");
            }
        }

        return Deduplicate(collected);
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
