using EventCollector.Models;
using Xunit;

namespace EventCollector.Tests;

/// <summary>タイトル正規化（同一性判定の安定化）に関するテスト。</summary>
public sealed class EventTitleTests
{
    [Theory]
    [InlineData("Online Math Contest（OMC）", "Online Math Contest (OMC)")] // 全角/半角括弧
    [InlineData("ＡＢＣ 466", "ABC466")]                                    // 全角英数 + 空白
    [InlineData("JMO夏季セミナー 2026", "JMO夏季セミナー")]                 // 年号の有無
    [InlineData("勉強会！", "勉強会")]                                       // 末尾記号
    [InlineData("  勉強会  ", "勉強会")]                                     // 前後空白
    public void 表記が違っても正規化後は一致する(string a, string b)
    {
        Assert.Equal(EventTitle.Normalize(a), EventTitle.Normalize(b));
    }

    [Fact]
    public void 長音符は語の一部として残す()
    {
        // "セミナー" と "セミナ" を畳むと別語を誤マージしうるため、長音符は保持する。
        Assert.NotEqual(EventTitle.Normalize("セミナー"), EventTitle.Normalize("セミナ"));
    }

    [Fact]
    public void 意味の違う語は別のままにする()
    {
        Assert.NotEqual(EventTitle.Normalize("御徒町ミネラルマルシェ"), EventTitle.Normalize("広島ミネラルマルシェ"));
    }

    [Fact]
    public void 記号だけのタイトルは空へ潰さない()
    {
        // 記号除去で空になる入力は区別が付かなくなるのを避け、フォールバックで非空を返す。
        Assert.NotEqual(string.Empty, EventTitle.Normalize("（）"));
    }
}
