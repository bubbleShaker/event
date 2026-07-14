using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// AtCoder 公式サイトの「予定されたコンテスト」表を直接 fetch して収集する <see cref="IEventSource"/>。
/// 公式 REST API は無いが、告知されたコンテストは Kenkoooo(<see cref="AtCoderContestSource"/>)へ
/// 反映されるより先に公式サイトの upcoming テーブルへ載る。告知直後のコンテストを取りこぼさないため、
/// JSON 源と併用する（片方が壊れても失敗分離でもう一方は生き残る）。
/// HTML は正規表現で行単位に切り出してパースする（表が単純なため外部依存を足さない）。
/// </summary>
public sealed partial class AtCoderOfficialContestSource : IEventSource
{
    // 予定コンテスト一覧ページ。lang=ja で日本語表記を固定する（表構造は言語に依らない）。
    private const string ContestsPageUrl = "https://atcoder.jp/contests/?lang=ja";

    // 正体を明示する User-Agent。現状 UA 無しでも 200 だが、CDN が既定/空 UA を弾き始めても
    // 収集が黙って 0 件化しないための保険。連絡先代わりにリポジトリ URL を添える。
    private const string UserAgent =
        "EventCollector/1.0 (+https://github.com/bubbleShaker/event)";

    // 収集の対象期間の上限。JSON 源・web_search 収集（おおむね3か月以内）と揃える。
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(92);

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>取得用クライアントと現在時刻の供給を差し込む。省略時は既定の実装を使う。</summary>
    /// <param name="httpClient">差し替え用の <see cref="HttpClient"/>。省略時は新規生成。</param>
    /// <param name="clock">現在時刻の供給。未来判定に使う。省略時は <see cref="DateTimeOffset.UtcNow"/>。</param>
    public AtCoderOfficialContestSource(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null)
    {
        // 既定生成時のみ UA を付ける。注入クライアント（テスト等）の設定は尊重して触らない。
        if (httpClient is null)
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        _httpClient = httpClient;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public string Name => "AtCoder(公式)";

    /// <summary>公式サイトの upcoming 表から、今日以降・約3か月以内のコンテストを収集する。</summary>
    public async Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // 非 200 応答や本文取得失敗は例外になり、EventSourceRunner の失敗分離でこの源だけスキップされる。
        string html = await _httpClient.GetStringAsync(ContestsPageUrl, cancellationToken);

        DateTimeOffset now = _clock();
        DateTimeOffset until = now + Horizon;

        // 予定コンテスト表の tbody を切り出し、行ごとにパースする。表が見つからなければ 0 件。
        string? tbody = ExtractUpcomingTbody(html);
        if (tbody is null)
        {
            return [];
        }

        var upcoming =
            from Match row in RowRegex().Matches(tbody)
            let contest = ParseRow(row.Groups[1].Value)
            where contest is not null && contest.Start > now && contest.Start <= until
            orderby contest.Start
            select ToEventItem(contest);

        return [.. upcoming];
    }

    /// <summary>予定コンテスト表(<c>id="contest-table-upcoming"</c>)の tbody 部分を切り出す。</summary>
    private static string? ExtractUpcomingTbody(string html)
    {
        int table = html.IndexOf("contest-table-upcoming", StringComparison.Ordinal);
        if (table < 0)
        {
            return null;
        }

        int open = html.IndexOf("<tbody>", table, StringComparison.Ordinal);
        int close = open < 0 ? -1 : html.IndexOf("</tbody>", open, StringComparison.Ordinal);
        return open < 0 || close < 0 ? null : html[(open + "<tbody>".Length)..close];
    }

    /// <summary>1行分の HTML から開始時刻・スラッグ・名称・所要時間を抽出する。欠けていれば null。</summary>
    private static ParsedContest? ParseRow(string row)
    {
        Match time = StartRegex().Match(row);
        Match contest = ContestRegex().Match(row);
        if (!time.Success || !contest.Success)
        {
            return null;
        }

        // 開始時刻は "2026-07-12 21:00:00+0900" 形式。zzz はコロン無しオフセットを受け付けないため、
        // 日時とオフセットを分離して DateTimeOffset を組み立てる（+0900 固定に依存しない）。
        var local = DateTime.ParseExact(
            time.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None);
        int sign = time.Groups[2].Value == "-" ? -1 : 1;
        var offset = new TimeSpan(
            sign * int.Parse(time.Groups[3].Value, CultureInfo.InvariantCulture),
            sign * int.Parse(time.Groups[4].Value, CultureInfo.InvariantCulture), 0);
        var start = new DateTimeOffset(local, offset);

        // タイトルは HTML エンティティを戻し、連続空白を1つに畳む（表示の揺れを抑える）。
        string title = CollapseSpaces().Replace(
            WebUtility.HtmlDecode(contest.Groups[2].Value).Trim(), " ");

        // 所要時間 "HH:MM" は分に直す。欠けていても致命的でないため 0（不明扱い）にする。
        Match duration = DurationRegex().Match(row);
        long minutes = duration.Success
            ? long.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) * 60
              + long.Parse(duration.Groups[2].Value, CultureInfo.InvariantCulture)
            : 0;

        return new ParsedContest(contest.Groups[1].Value, title, start, minutes);
    }

    /// <summary>コンテスト1件を、JST 表記の <see cref="EventItem"/> へ変換する。</summary>
    private static EventItem ToEventItem(ParsedContest c)
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
            Group = "AtCoder / 競技プログラミング",
            Summary = $"{c.Start:yyyy-MM-dd HH:mm} JST 開催{duration}。",
            // 開始時刻・所要時間が確定しているため、カレンダーには時刻付きイベントとして載せる。
            // 所要時間不明（0 分）のときは終了を空にし、Factory 側の既定（1 時間）に委ねる。
            StartsAt = c.Start,
            EndsAt = c.DurationMinutes > 0 ? c.Start.AddMinutes(c.DurationMinutes) : null,
        };
    }

    /// <summary>行から取り出した確定情報。開始時刻は告知された JST オフセットを保持する。</summary>
    private sealed record ParsedContest(string Slug, string Title, DateTimeOffset Start, long DurationMinutes);

    // 表の各行。tbody 内の <tr>...</tr> を1件ずつ取り出す。
    [GeneratedRegex(@"<tr>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    // 開始時刻セルの <time> テキスト。日時・符号・オフセット(hh)(mm) を分けて捕捉する。
    [GeneratedRegex(@"fixtime-full'>(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})([+-])(\d{2})(\d{2})</time>")]
    private static partial Regex StartRegex();

    // コンテストリンク。/contests/<slug> の a 要素からスラッグと表示名を捕捉する。
    [GeneratedRegex(@"href=""/contests/([^""?#]+)""[^>]*>([^<]+)</a>")]
    private static partial Regex ContestRegex();

    // 所要時間セル "HH:MM"。日時セルの秒(:SS)を誤検出しないよう </td> で閉じを固定する。
    [GeneratedRegex(@">(\d{1,4}):(\d{2})</td>")]
    private static partial Regex DurationRegex();

    // 連続する空白文字。
    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseSpaces();
}
