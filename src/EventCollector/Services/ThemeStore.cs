using EventCollector.Models;

namespace EventCollector.Services;

/// <summary><c>themes.md</c> から収集テーマを読み込む。</summary>
public sealed class ThemeStore
{
    private const string BulletPrefix = "- ";
    private const string HeadingPrefix = "## ";

    /// <summary>見出しの無い箇条書きだけの場合に使う既定グループ名。</summary>
    private const string DefaultGroupName = "その他";

    /// <summary>
    /// 指定パスの Markdown を、<c>## 見出し</c> ＝ 1グループとして読み込む。
    /// 見出し配下の <c>- </c> 箇条書きがそのグループのテーマになる。
    /// テーマを持たない見出しは無視する（空グループを作らない）。
    /// </summary>
    /// <param name="themesFilePath"><c>themes.md</c> の絶対または相対パス。</param>
    /// <returns>グループ一覧。1つも見出しが無ければ全テーマを既定グループにまとめる。</returns>
    public IReadOnlyList<ThemeGroup> LoadGroups(string themesFilePath)
    {
        if (!File.Exists(themesFilePath))
        {
            throw new FileNotFoundException($"テーマ設定が見つからない: {themesFilePath}");
        }

        List<ThemeGroup> groups = [];
        string currentName = DefaultGroupName;
        List<string> currentThemes = [];

        void Flush()
        {
            if (currentThemes.Count > 0)
            {
                groups.Add(new ThemeGroup(currentName, currentThemes));
                currentThemes = [];
            }
        }

        foreach (string rawLine in File.ReadLines(themesFilePath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(HeadingPrefix, StringComparison.Ordinal))
            {
                // 見出しが来たら直前グループを確定し、新しいグループを開始する。
                Flush();
                currentName = line[HeadingPrefix.Length..].Trim();
            }
            else if (line.StartsWith(BulletPrefix, StringComparison.Ordinal))
            {
                string theme = line[BulletPrefix.Length..].Trim();
                if (theme.Length > 0)
                {
                    currentThemes.Add(theme);
                }
            }
        }

        Flush();
        return groups;
    }

    /// <summary>指定パスの Markdown から、箇条書き行をテーマとして抽出する。</summary>
    /// <param name="themesFilePath"><c>themes.md</c> の絶対または相対パス。</param>
    /// <returns>テーマ文字列の一覧。</returns>
    public IReadOnlyList<string> LoadThemes(string themesFilePath)
    {
        if (!File.Exists(themesFilePath))
        {
            throw new FileNotFoundException($"テーマ設定が見つからない: {themesFilePath}");
        }

        List<string> themes = [];
        foreach (string rawLine in File.ReadLines(themesFilePath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(BulletPrefix, StringComparison.Ordinal))
            {
                string theme = line[BulletPrefix.Length..].Trim();
                if (theme.Length > 0)
                {
                    themes.Add(theme);
                }
            }
        }

        return themes;
    }
}
