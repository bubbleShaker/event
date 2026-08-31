using System.Net;
using System.Text.RegularExpressions;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// 「公式サイトの予定コンテスト表を fetch して確定情報で収集する」収集源の共通土台。
/// AtCoder・OMC のように公開 API が無いサイトでは、HTML の表を行単位でパースするしかない。
/// 取得・節の切り出し・行の反復・期間フィルタ・並び順という骨格は共通で、
/// サイトごとに違うのは「どの節か」「1行をどう読むか」「どんな <see cref="EventItem"/> にするか」だけ。
/// その3点だけを派生クラスに委ねる（テンプレートメソッド）。
/// 骨格を1か所に集めることで、堅牢化（構造変化の検知など）が全サイトへ同時に効く。
/// </summary>
public abstract partial class HtmlContestTableSource : IEventSource
{
    // 正体を明示する User-Agent。現状 UA 無しでも 200 だが、CDN が既定/空 UA を弾き始めても
    // 収集が黙って 0 件化しないための保険。連絡先代わりにリポジトリ URL を添える。
    private const string UserAgent =
        "EventCollector/1.0 (+https://github.com/bubbleShaker/event)";

    // 収集の対象期間の上限。web_search 収集（おおむね3か月以内）と揃える。
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(92);

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>取得用クライアントと現在時刻の供給を差し込む。省略時は既定の実装を使う。</summary>
    /// <param name="httpClient">差し替え用の <see cref="HttpClient"/>。省略時は新規生成。</param>
    /// <param name="clock">現在時刻の供給。未来判定に使う。省略時は <see cref="DateTimeOffset.UtcNow"/>。</param>
    protected HtmlContestTableSource(HttpClient? httpClient, Func<DateTimeOffset>? clock)
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
    public abstract string Name { get; }

    /// <summary>予定コンテスト表を含むページの URL。</summary>
    protected abstract string PageUrl { get; }

    /// <summary>
    /// 予定コンテスト表を特定する目印（id 属性など）。この文字列より後ろの最初の tbody を対象にする。
    /// 過去・開催中の表を巻き込まないよう、予定表が先に現れる目印を選ぶ。
    /// </summary>
    protected abstract string UpcomingTableMarker { get; }

    /// <summary>1行分の HTML から確定情報を取り出す。読めない行（見出し・リンク行など）は null。</summary>
    protected abstract ContestRow? ParseRow(string row);

    /// <summary>パース結果を、そのサイトの表記に沿った <see cref="EventItem"/> へ変換する。</summary>
    protected abstract EventItem ToEventItem(ContestRow c);

    /// <summary>予定表から、これから開催される（今日以降・約3か月以内の）コンテストを収集する。</summary>
    public async Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // 非 200 応答や本文取得失敗は例外になり、EventSourceRunner の失敗分離でこの源だけスキップされる。
        string html = await _httpClient.GetStringAsync(PageUrl, cancellationToken);

        DateTimeOffset now = _clock();
        DateTimeOffset until = now + Horizon;

        // 予定コンテスト表の tbody を切り出し、行ごとにパースする。表が見つからなければ 0 件。
        string? tbody = ExtractUpcomingTbody(html);
        if (tbody is null)
        {
            return [];
        }

        // OfType<ContestRow>() は「ParseRow が null を返した行（見出し・リンク行など）を捨てる」意味。
        List<ContestRow> rows =
            [.. RowRegex().Matches(tbody)
                .Select(row => ParseRow(row.Groups[1].Value))
                .OfType<ContestRow>()];

        // 行はあるのに1件も読めない＝サイトの構造が変わった兆候。黙って 0 件を返すと
        // 「本当に予定が無い」と区別できず、収集が壊れたまま気づけないため失敗させる。
        // 例外は EventSourceRunner の失敗分離が拾い、この源だけがエラーログ付きでスキップされる。
        if (rows.Count == 0 && tbody.Contains("<tr", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Name}: 予定表に行があるがどれも解析できなかった。ページ構造が変わった可能性がある。");
        }

        var upcoming =
            from contest in rows
            where contest.Start > now && contest.Start <= until
            orderby contest.Start
            select ToEventItem(contest);

        return [.. upcoming];
    }

    /// <summary>名称の HTML エンティティを戻し、連続空白を1つに畳む（表示の揺れを抑える）。</summary>
    protected static string NormalizeTitle(string raw) =>
        CollapseSpaces().Replace(WebUtility.HtmlDecode(raw).Trim(), " ");

    /// <summary>予定コンテスト表の tbody 部分を切り出す。見つからなければ null。</summary>
    private string? ExtractUpcomingTbody(string html)
    {
        int table = html.IndexOf(UpcomingTableMarker, StringComparison.Ordinal);
        if (table < 0)
        {
            return null;
        }

        int open = html.IndexOf("<tbody>", table, StringComparison.Ordinal);
        int close = open < 0 ? -1 : html.IndexOf("</tbody>", open, StringComparison.Ordinal);
        return open < 0 || close < 0 ? null : html[(open + "<tbody>".Length)..close];
    }

    // 表の各行。tbody 内の <tr>...</tr> を1件ずつ取り出す。class 等の属性が付いても拾えるようにし、
    // 「属性が増えただけ」で構造変化と判定して落ちるのを避ける。閉じタグ欠落の HTML で
    // 後戻りが膨らまないよう、照合にも上限時間を設ける。
    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex RowRegex();

    // 連続する空白文字。
    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex CollapseSpaces();
}
