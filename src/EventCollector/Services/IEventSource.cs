using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// 独立して1つのテーマ系統からイベントを集める収集源。
/// web_search グループ・外部 API など実装は問わず、「名前を持ち、独立に収集する」だけを約束する。
/// これにより収集源の追加は実装クラスを1つ足すだけで済む（開放閉鎖原則）。
/// </summary>
public interface IEventSource
{
    /// <summary>ログ・失敗分離の識別に使う収集源名。</summary>
    string Name { get; }

    /// <summary>この収集源の担当範囲でイベントを収集する。</summary>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>収集したイベント一覧。0件でも例外にしない。</returns>
    Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default);
}
