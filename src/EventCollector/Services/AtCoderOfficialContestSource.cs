using System.Globalization;
using System.Text.RegularExpressions;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// AtCoder 公式サイトの「予定されたコンテスト」表を直接 fetch して収集する <see cref="IEventSource"/>。
/// 公式 REST API は無いが、告知されたコンテストは Kenkoooo(<see cref="AtCoderContestSource"/>)へ
/// 反映されるより先に公式サイトの upcoming テーブルへ載る。告知直後のコンテストを取りこぼさないため、
/// JSON 源と併用する（片方が壊れても失敗分離でもう一方は生き残る）。
/// 取得・行の反復・期間フィルタは <see cref="HtmlContestTableSource"/> に任せ、
/// ここでは AtCoder 固有の「表の場所」「セルの読み方」だけを持つ。
/// </summary>
public sealed partial class AtCoderOfficialContestSource : HtmlContestTableSource
{
    /// <summary>取得用クライアントと現在時刻の供給を差し込む。省略時は既定の実装を使う。</summary>
    /// <param name="httpClient">差し替え用の <see cref="HttpClient"/>。省略時は新規生成。</param>
    /// <param name="clock">現在時刻の供給。未来判定に使う。省略時は <see cref="DateTimeOffset.UtcNow"/>。</param>
    public AtCoderOfficialContestSource(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null)
        : base(httpClient, clock)
    {
    }

    /// <inheritdoc />
    public override string Name => "AtCoder(公式)";

    // 予定コンテスト一覧ページ。lang=ja で日本語表記を固定する（表構造は言語に依らない）。
    /// <inheritdoc />
    protected override string PageUrl => "https://atcoder.jp/contests/?lang=ja";

    /// <inheritdoc />
    protected override string UpcomingTableMarker => "contest-table-upcoming";

    /// <summary>1行分の HTML から開始時刻・スラッグ・名称・所要時間を抽出する。欠けていれば null。</summary>
    protected override ContestRow? ParseRow(string row)
    {
        Match time = StartRegex().Match(row);
        Match contest = ContestRegex().Match(row);
        if (!time.Success || !contest.Success)
        {
            return null;
        }

        // 開始時刻は "2026-07-12 21:00:00+0900" 形式。zzz はコロン無しオフセットを受け付けないため、
        // 日時とオフセットを分離して DateTimeOffset を組み立てる（+0900 固定に依存しない）。
        // 正規表現を通っても暦として不正な日付（2026-02-30 等）はありうるので、
        // 1行の異常で収集源ごと落ちないよう Try 版で受け止めて行を捨てる。
        if (!DateTime.TryParseExact(
                time.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }

        int sign = time.Groups[2].Value == "-" ? -1 : 1;
        var offset = new TimeSpan(
            sign * int.Parse(time.Groups[3].Value, CultureInfo.InvariantCulture),
            sign * int.Parse(time.Groups[4].Value, CultureInfo.InvariantCulture), 0);
        var start = new DateTimeOffset(local, offset);

        // 所要時間 "HH:MM" は分に直す。欠けていても致命的でないため 0（不明扱い）にする。
        Match duration = DurationRegex().Match(row);
        long minutes = duration.Success
            ? long.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) * 60
              + long.Parse(duration.Groups[2].Value, CultureInfo.InvariantCulture)
            : 0;

        return new ContestRow(contest.Groups[1].Value, NormalizeTitle(contest.Groups[2].Value), start, minutes);
    }

    /// <summary>コンテスト1件を、JST 表記の <see cref="EventItem"/> へ変換する。</summary>
    protected override EventItem ToEventItem(ContestRow c)
    {
        string duration = c.DurationMinutes > 0 ? $"（約 {c.DurationMinutes} 分）" : string.Empty;

        return new EventItem
        {
            Title = c.Title,
            Date = c.Start.ToString("yyyy-MM-dd"),
            Location = "Online",
            Url = $"https://atcoder.jp/contests/{c.Slug}",
            Theme = "競技プログラミング（AtCoder）",
            // themes.md の見出しと一致させ、AtCoder テーマ群のイベントと同じ色でカレンダーに載せる。
            Group = ThemeGroups.CompetitiveProgramming,
            Summary = $"{c.Start:yyyy-MM-dd HH:mm} JST 開催{duration}。",
            // 開始時刻・所要時間が確定しているため、カレンダーには時刻付きイベントとして載せる。
            // 所要時間不明（0 分）のときは終了を空にし、Factory 側の既定（1 時間）に委ねる。
            StartsAt = c.Start,
            EndsAt = c.DurationMinutes > 0 ? c.Start.AddMinutes(c.DurationMinutes) : null,
        };
    }

    // 開始時刻セルの <time> テキスト。日時・符号・オフセット(hh)(mm) を分けて捕捉する。
    [GeneratedRegex(
        @"fixtime-full'>(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})([+-])(\d{2})(\d{2})</time>",
        RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex StartRegex();

    // コンテストリンク。/contests/<slug> の a 要素からスラッグと表示名を捕捉する。
    [GeneratedRegex(
        @"href=""/contests/([A-Za-z0-9_-]+)""[^>]*>([^<]+)</a>",
        RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ContestRegex();

    // 所要時間セル "HH:MM"。日時セルの秒(:SS)を誤検出しないよう </td> で閉じを固定する。
    [GeneratedRegex(@">(\d{1,4}):(\d{2})</td>", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DurationRegex();
}
