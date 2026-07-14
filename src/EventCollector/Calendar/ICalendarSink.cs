using EventCollector.Models;

namespace EventCollector.Calendar;

/// <summary>イベント一覧をカレンダーへ登録する抽象。</summary>
public interface ICalendarSink
{
    /// <summary>イベントを登録（無ければ作成・あれば更新）する。</summary>
    /// <param name="events">登録対象のイベント一覧。</param>
    /// <param name="palette">テーマグループごとの色（colorId）割り当て。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>実際に登録（作成または更新）した件数。</returns>
    Task<int> SyncAsync(
        IReadOnlyList<EventItem> events,
        ThemeColorPalette palette,
        CancellationToken cancellationToken = default);
}
