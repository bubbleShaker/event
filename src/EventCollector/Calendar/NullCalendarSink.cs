using EventCollector.Models;

namespace EventCollector.Calendar;

/// <summary>登録先が未設定の場合に使う no-op 実装（Null Object）。</summary>
public sealed class NullCalendarSink : ICalendarSink
{
    /// <inheritdoc />
    public Task<int> SyncAsync(
        IReadOnlyList<EventItem> events, CancellationToken cancellationToken = default)
    {
        // 認証情報が未設定なので何も登録しない。登録件数 0 を返す。
        return Task.FromResult(0);
    }
}
