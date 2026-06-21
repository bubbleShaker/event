using System.Text;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>イベント一覧と差分を Markdown に整形する。</summary>
public sealed class MarkdownRenderer
{
    /// <summary>人間向けの最新一覧（<c>events.md</c>）を生成する。テーマごとにグループ化する。</summary>
    /// <param name="events">イベント一覧。</param>
    /// <param name="generatedAt">生成時刻。</param>
    /// <returns>Markdown 文字列。</returns>
    public string RenderEventList(IReadOnlyList<EventItem> events, DateTimeOffset generatedAt)
    {
        StringBuilder sb = new();
        sb.AppendLine("# イベント一覧");
        sb.AppendLine();
        sb.AppendLine($"最終更新: {generatedAt:yyyy-MM-dd HH:mm} / {events.Count} 件");
        sb.AppendLine();

        IEnumerable<IGrouping<string, EventItem>> byTheme = events
            .OrderBy(e => e.Theme, StringComparer.Ordinal)
            .GroupBy(e => e.Theme);

        foreach (IGrouping<string, EventItem> group in byTheme)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (EventItem item in group.OrderBy(e => e.Date, StringComparer.Ordinal))
            {
                sb.AppendLine($"- **{item.Title}**（{item.Date} / {item.Location}）");
                sb.AppendLine($"  - {item.Summary}");
                sb.AppendLine($"  - {item.Url}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>実行ごとの差分記録（<c>runs/日付.md</c>）を生成する。</summary>
    /// <param name="diff">差分結果。</param>
    /// <param name="generatedAt">生成時刻。</param>
    /// <returns>Markdown 文字列。</returns>
    public string RenderRunLog(DiffResult diff, DateTimeOffset generatedAt)
    {
        StringBuilder sb = new();
        sb.AppendLine($"# 実行ログ {generatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"追加 {diff.Added.Count} / 変更 {diff.Changed.Count} / 削除 {diff.Removed.Count}");
        sb.AppendLine();

        AppendSection(sb, "追加", diff.Added);
        AppendSection(sb, "変更", diff.Changed);
        AppendSection(sb, "削除", diff.Removed);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<EventItem> items)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        if (items.Count == 0)
        {
            sb.AppendLine("（なし）");
            sb.AppendLine();
            return;
        }

        foreach (EventItem item in items)
        {
            sb.AppendLine($"- {item.Title}（{item.Date} / {item.Theme}）");
        }

        sb.AppendLine();
    }
}
