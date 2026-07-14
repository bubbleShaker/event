using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EventCollector.Models;

/// <summary>
/// モデルが自由記述するイベント名を、同一性判定（キー）用に正規化する共有ヘルパー。
/// web_search は同じイベントでも表記を揺らす（全角/半角括弧 <c>（OMC）</c>↔<c>(OMC)</c>、
/// 空白の有無、年号 <c>2026</c> の有無、記号の差）ため、これらを畳んで重複登録を防ぐ。
/// 表示用の <see cref="EventItem.Title"/> は生のまま保持し、正規化はキー算出時だけ使う。
/// </summary>
public static partial class EventTitle
{
    /// <summary>
    /// キー算出用にタイトルを正規化する。以下を吸収する:
    /// 全角→半角（NFKC）・小文字化・西暦（<c>20xx</c>）除去・空白除去・記号除去。
    /// 意味的な改名（語の追加など）は吸収しないため、それ由来の重複は残る（原理的限界）。
    /// </summary>
    public static string Normalize(string raw)
    {
        // NFKC で全角英数・全角括弧などを半角へ寄せ、表記差の大半を潰す。
        string normalized = raw.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        normalized = Year().Replace(normalized, "");         // "2026" 等の西暦を除去
        normalized = Whitespace().Replace(normalized, "");   // 全角/半角スペースを除去
        normalized = Symbols().Replace(normalized, "");      // 括弧・約物などの記号を除去

        // 記号除去で空になった場合（記号だけのタイトル等）は、区別が付かなくなるのを避けるため
        // NFKC 済みの元文字列にフォールバックする（小文字化は他分岐と揃える）。
        return normalized.Length > 0 ? normalized : raw.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
    }

    // 西暦 4 桁（2000-2099）。年号の有無による揺れ（"JMO夏季セミナー 2026" ↔ "JMO夏季セミナー"）を吸収する。
    // 前後を数字以外に限定し、長い数字列の途中（"12026" 等）を年号と誤認しない。
    // 実データ上、20xx を含む名前は全て本物の西暦で、大会番号（"ABC 466" 等 3 桁）とは衝突しない。
    [GeneratedRegex(@"(?<!\d)20\d{2}(?!\d)")]
    private static partial Regex Year();

    // 半角/全角スペース・タブ等の空白。
    [GeneratedRegex(@"[\s　]+")]
    private static partial Regex Whitespace();

    // 括弧類・約物・区切りなど、同一性に無関係な記号。文字（日本語含む・長音符 ー）と数字は残す。
    [GeneratedRegex(@"[「」『』（）()【】\[\]｛｝{}、。,\.・:：;；!！\?？~〜\-—―_/\\|]")]
    private static partial Regex Symbols();
}
