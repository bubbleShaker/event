namespace EventCollector.Services;

/// <summary><c>themes.md</c> から収集テーマを読み込む。</summary>
public sealed class ThemeStore
{
    private const string BulletPrefix = "- ";

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
