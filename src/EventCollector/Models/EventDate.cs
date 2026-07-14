using System.Globalization;
using System.Text.RegularExpressions;

namespace EventCollector.Models;

/// <summary>
/// モデルが自由記述する開催日文字列（例 <c>2026-06-25～26</c> / <c>2026-06-25:2026-06-26</c> / <c>TBD</c>）を
/// 解釈するための共有ヘルパー。キー正規化と過去判定で同じ規則を使うために切り出している。
/// </summary>
public static partial class EventDate
{
    /// <summary>
    /// キー算出用に日付を正規化する。先頭の <c>yyyy-MM-dd</c> があればそれを、
    /// 無ければ trim した生文字列を返す。これにより範囲表記の違いが同一キーへ寄る。
    /// </summary>
    public static string Normalize(string raw)
    {
        Match match = LeadingIsoDate().Match(raw);
        return match.Success ? match.Value : raw.Trim();
    }

    /// <summary>先頭の <c>yyyy-MM-dd</c> を開始日として取り出す。解析できなければ <c>false</c>。</summary>
    public static bool TryGetStartDate(string raw, out DateOnly date)
    {
        Match match = LeadingIsoDate().Match(raw);
        if (!match.Success)
        {
            date = default;
            return false;
        }

        return DateOnly.TryParseExact(
            match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// カレンダー登録用に開始日を取り出す。日精度（<c>yyyy-MM-dd</c>）があればそれを返す。
    /// 無ければ月精度（先頭 <c>yyyy-MM</c>。例 <c>2026-07</c> / <c>2026-04-TBD</c>）から
    /// <b>その月の1日</b>を返し <paramref name="approximate"/> を <c>true</c> にする。
    /// 完全に解析できない（<c>TBD</c> / <c>N/A</c> など）場合のみ <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 過去判定（<see cref="TryGetStartDate"/>）はあえて日精度のままにしている。月精度を過去判定に使うと
    /// 月初を基準に「月途中で過去扱い」となり、月内開催イベントが会期中に消えるため。
    /// </remarks>
    public static bool TryGetCalendarStartDate(string raw, out DateOnly date, out bool approximate)
    {
        // まず日精度を優先。取れれば概算ではない。
        if (TryGetStartDate(raw, out date))
        {
            approximate = false;
            return true;
        }

        // 日精度が無ければ月精度（yyyy-MM）を試し、その月の1日で概算登録する。
        Match month = LeadingYearMonth().Match(raw);
        if (month.Success && DateOnly.TryParseExact(
                month.Value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            approximate = true;
            return true;
        }

        date = default;
        approximate = false;
        return false;
    }

    // 文字列の先頭にある ISO 日付（yyyy-MM-dd）。範囲表記 "2026-06-25～26" でも先頭日付を拾う。
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex LeadingIsoDate();

    // 文字列の先頭にある年月（yyyy-MM）。"2026-07" や "2026-04-TBD" の先頭月を拾う。
    // 後ろに続く日付部（-dd）は問わない（TryGetStartDate で先に日精度を拾えなかった時のみ使う）。
    [GeneratedRegex(@"^\d{4}-\d{2}")]
    private static partial Regex LeadingYearMonth();
}
