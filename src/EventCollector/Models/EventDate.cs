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

    // 文字列の先頭にある ISO 日付（yyyy-MM-dd）。範囲表記 "2026-06-25～26" でも先頭日付を拾う。
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex LeadingIsoDate();
}
