using EventCollector.Models;

namespace EventCollector.Notifications;

/// <summary>差分を外部へ通知する抽象。</summary>
public interface IDiffNotifier
{
    /// <summary>差分を通知する。</summary>
    /// <param name="diff">通知対象の差分。</param>
    /// <param name="generatedAt">生成時刻。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>実際に通知を送信したなら <c>true</c>、通知先未設定などで送らなかったなら <c>false</c>。</returns>
    Task<bool> NotifyAsync(
        DiffResult diff,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default);
}
