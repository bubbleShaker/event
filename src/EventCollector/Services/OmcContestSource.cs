using System.Globalization;
using System.Text.RegularExpressions;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// OnlineMathContest（OMC）の「予定されたコンテスト」表を直接 fetch して収集する <see cref="IEventSource"/>。
/// web_search 経由では個別コンテストが拾えず日付 <c>TBD</c> のプレースホルダにしかならないため、
/// AtCoder と同じく公式サイトから確定情報（開催日時・URL）で取り込む。
/// 取得・行の反復・期間フィルタは <see cref="HtmlContestTableSource"/> に任せ、
/// ここでは OMC 固有の「表の場所」「セルの読み方」だけを持つ。
/// </summary>
public sealed partial class OmcContestSource : HtmlContestTableSource
{
    // OMC の開始時刻はオフセット表記を持たず、すべて日本時間で告知される。
    // 移植性のため TimeZoneInfo ではなく固定オフセットで JST として解釈する。
    private static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    /// <summary>取得用クライアントと現在時刻の供給を差し込む。省略時は既定の実装を使う。</summary>
    /// <param name="httpClient">差し替え用の <see cref="HttpClient"/>。省略時は新規生成。</param>
    /// <param name="clock">現在時刻の供給。未来判定に使う。省略時は <see cref="DateTimeOffset.UtcNow"/>。</param>
    public OmcContestSource(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null)
        : base(httpClient, clock)
    {
    }

    /// <inheritdoc />
    public override string Name => "OMC";

    // コンテスト一覧ページ。予定・開催中・過去の3表を1ページに持つ。
    /// <inheritdoc />
    protected override string PageUrl => "https://onlinemathcontest.com/contests";

    /// <inheritdoc />
    protected override string UpcomingTableMarker => "upcoming_contests";

    /// <summary>1行分の HTML から開始時刻・スラッグ・名称・所要時間を抽出する。欠けていれば null。</summary>
    protected override ContestRow? ParseRow(string row)
    {
        Match contest = ContestRegex().Match(row);
        Match time = StartRegex().Match(row);
        if (!contest.Success || !time.Success)
        {
            return null;
        }

        // 開始時刻は "2026-09-04 21:00:00"（オフセット無し）。JST として解釈する。
        // 正規表現を通っても暦として不正な日付（2026-02-30 等）はありうるので、
        // 1行の異常で収集源ごと落ちないよう Try 版で受け止めて行を捨てる。
        if (!DateTime.TryParseExact(
                time.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }

        // 所要時間は "80 分"。開催直前まで伏せられる回（「開始1時間前に公開」）もあるため、
        // 取れなければ 0（不明扱い）にする。致命的ではない。
        Match duration = DurationRegex().Match(row);
        long minutes = duration.Success
            ? long.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture)
            : 0;

        return new ContestRow(
            contest.Groups[1].Value,
            NormalizeTitle(contest.Groups[2].Value),
            new DateTimeOffset(local, JstOffset),
            minutes);
    }

    /// <summary>コンテスト1件を、JST 表記の <see cref="EventItem"/> へ変換する。</summary>
    protected override EventItem ToEventItem(ContestRow c)
    {
        string duration = c.DurationMinutes > 0
            ? $"（約 {c.DurationMinutes} 分）"
            : "（所要時間は開始直前に公開）";

        return new EventItem
        {
            // タイトルは加工しない。EventItem.Key は正規化した名称＋開催日なので、
            // ここで "OMC " などを冠すると web_search 側が拾った同じイベントとキーが一致せず、
            // カレンダーに二重登録される（AtCoder 公式源と同じ理由で生の名称を保つ）。
            // 何のイベントかは Theme / Summary 側で示す。
            Title = c.Title,
            Date = c.Start.ToString("yyyy-MM-dd"),
            Location = "Online",
            Url = $"https://onlinemathcontest.com/contests/{c.Slug}",
            Theme = "数学コンテスト（OnlineMathContest）",
            // themes.md の見出しと一致させ、AtCoder テーマ群のイベントと同じ色でカレンダーに載せる。
            Group = ThemeGroups.CompetitiveProgramming,
            Summary = $"OnlineMathContest（OMC）のコンテスト。{c.Start:yyyy-MM-dd HH:mm} JST 開催{duration}。",
            // 開始時刻が確定しているため、カレンダーには時刻付きイベントとして載せる。
            // 所要時間不明（0 分）のときは終了を空にし、Factory 側の既定（1 時間）に委ねる（AtCoder 源と対称）。
            StartsAt = c.Start,
            EndsAt = c.DurationMinutes > 0 ? c.Start.AddMinutes(c.DurationMinutes) : null,
        };
    }

    // コンテストリンク。/contests/<slug> の a 要素からスラッグと表示名を捕捉する。
    // 現状は絶対 URL だが、相対 href へ変わっても拾えるようホスト部分は任意にする。
    [GeneratedRegex(
        @"href=""(?:https://onlinemathcontest\.com)?/contests/([A-Za-z0-9_-]+)""[^>]*>([^<]+)</a>",
        RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ContestRegex();

    // 開始時刻セル。オフセットを持たない "yyyy-MM-dd HH:mm:ss" のみ。属性が付いても拾えるようにする。
    [GeneratedRegex(
        @"<td[^>]*>\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s*</td>",
        RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex StartRegex();

    // 所要時間セル "80 分"。未定の回は「開始1時間前に公開」となり一致しない。
    // 他セルの数字を拾わないよう、セルの開始から閉じまでを固定する。
    [GeneratedRegex(
        @"<td[^>]*>[^<]*?(\d{1,4})\s*分\s*</td>",
        RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DurationRegex();
}
