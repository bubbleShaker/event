namespace EventCollector.Models;

/// <summary>
/// 独立して収集する1つのテーマ群。<c>themes.md</c> の <c>## 見出し</c> 1つに対応する。
/// グループ単位で Web 検索を分けることで、検索枠の食い合いによる取りこぼしを防ぐ。
/// </summary>
/// <param name="Name">グループ名（見出しテキスト）。ログ・イベントのテーマ表示に使う。</param>
/// <param name="Themes">このグループに属するテーマ行の一覧。</param>
public sealed record ThemeGroup(string Name, IReadOnlyList<string> Themes);
