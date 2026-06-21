using System.Text;
using EventCollector.Models;

namespace EventCollector.Notifications;

/// <summary>差分から Discord 通知用のメッセージ本文を組み立てる。</summary>
public sealed class DiscordMessageBuilder
{
    /// <summary>Discord の content フィールドの最大文字数。</summary>
    public const int MaxContentLength = 2000;

    /// <summary>差分を通知本文に整形する。上限を超える場合は末尾を切り詰める。</summary>
    /// <param name="diff">差分結果。</param>
    /// <param name="generatedAt">生成時刻。</param>
    /// <returns>Discord に送る本文。</returns>
    public string Build(DiffResult diff, DateTimeOffset generatedAt)
    {
        StringBuilder sb = new();
        sb.AppendLine($"イベント収集更新 ({generatedAt:yyyy-MM-dd HH:mm})");
        sb.AppendLine($"追加 {diff.Added.Count} / 変更 {diff.Changed.Count} / 削除 {diff.Removed.Count}");

        AppendSection(sb, "新規", diff.Added);
        AppendSection(sb, "変更", diff.Changed);
        AppendSection(sb, "削除", diff.Removed);

        return Truncate(sb.ToString().TrimEnd());
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<EventItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"[{heading}]");
        foreach (EventItem item in items)
        {
            sb.AppendLine($"- {item.Title}（{item.Date} / {item.Theme}）");
        }
    }

    private static string Truncate(string content)
    {
        if (content.Length <= MaxContentLength)
        {
            return content;
        }

        const string ellipsis = "\n…(以下省略)";
        return string.Concat(content.AsSpan(0, MaxContentLength - ellipsis.Length), ellipsis);
    }
}
