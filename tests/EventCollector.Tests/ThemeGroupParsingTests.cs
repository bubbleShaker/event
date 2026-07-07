using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary><see cref="ThemeStore.LoadGroups"/> のグループ解析に関するテスト。</summary>
public sealed class ThemeGroupParsingTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempPath);

    [Fact]
    public void 見出しごとにグループへ分割される()
    {
        Write(
            """
            # 収集テーマ

            ## C# / .NET
            - C# の勉強会
            - 個人開発イベント

            ## AWS
            - JAWS-UG ハンズオン
            """);

        IReadOnlyList<ThemeGroup> groups = new ThemeStore().LoadGroups(_tempPath);

        Assert.Equal(2, groups.Count);
        Assert.Equal("C# / .NET", groups[0].Name);
        Assert.Equal(["C# の勉強会", "個人開発イベント"], groups[0].Themes);
        Assert.Equal("AWS", groups[1].Name);
        Assert.Equal(["JAWS-UG ハンズオン"], groups[1].Themes);
    }

    [Fact]
    public void テーマを持たない見出しは空グループを作らない()
    {
        Write(
            """
            ## 空の分野

            ## AtCoder
            - ABC コンテスト
            """);

        IReadOnlyList<ThemeGroup> groups = new ThemeStore().LoadGroups(_tempPath);

        Assert.Single(groups);
        Assert.Equal("AtCoder", groups[0].Name);
    }

    [Fact]
    public void 見出しが無い場合は既定グループにまとめる()
    {
        Write(
            """
            - テーマA
            - テーマB
            """);

        IReadOnlyList<ThemeGroup> groups = new ThemeStore().LoadGroups(_tempPath);

        Assert.Single(groups);
        Assert.Equal(["テーマA", "テーマB"], groups[0].Themes);
    }

    [Fact]
    public void ファイルが無ければ例外を投げる()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<FileNotFoundException>(() => new ThemeStore().LoadGroups(missing));
    }

    private void Write(string content) => File.WriteAllText(_tempPath, content);
}
