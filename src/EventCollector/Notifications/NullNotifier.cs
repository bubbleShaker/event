using EventCollector.Models;

namespace EventCollector.Notifications;

/// <summary>通知先が未設定の場合に使う no-op 実装（Null Object）。</summary>
public sealed class NullNotifier : IDiffNotifier
{
    /// <inheritdoc />
    public Task<bool> NotifyAsync(
        DiffResult diff,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        // 通知先が未設定なので何も送らない。送信していないことを false で伝える。
        return Task.FromResult(false);
    }
}
