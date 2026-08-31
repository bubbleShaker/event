namespace EventCollector.Models;

/// <summary>
/// 収集源が直接指定するテーマグループ名（<c>config/themes.md</c> の <c>## 見出し</c>）。
/// web_search 由来のイベントは見出しをそのまま持つが、AtCoder・OMC のような専用収集源は
/// 属するグループを自分で名乗る必要がある。名前が themes.md とずれるとカレンダーの色分けが
/// 無言で既定色に落ちるため、文字列は各収集源に散らさずここへ集約する。
/// 見出しを改名したときは、themes.md とこの定数を必ず一緒に直す（起動時に突き合わせて警告する）。
/// </summary>
public static class ThemeGroups
{
    /// <summary>AtCoder・OMC のコンテストが属するグループ。</summary>
    public const string CompetitiveProgramming = "AtCoder / 競技プログラミング";

    /// <summary>収集源が名乗るグループ名の一覧（themes.md との突き合わせに使う）。</summary>
    public static IReadOnlyList<string> All => [CompetitiveProgramming];
}
