using EventCollector.Models;
using Xunit;

namespace EventCollector.Tests;

/// <summary>開催日文字列の解釈（日精度・月精度フォールバック）のテスト。</summary>
public sealed class EventDateTests
{
    [Fact]
    public void CalendarStartDate_日精度はそのまま概算でない()
    {
        bool ok = EventDate.TryGetCalendarStartDate("2026-06-25", out var date, out bool approximate);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2026, 6, 25), date);
        Assert.False(approximate);
    }

    [Fact]
    public void CalendarStartDate_範囲表記は先頭の日を採る()
    {
        bool ok = EventDate.TryGetCalendarStartDate("2026-06-25～26", out var date, out bool approximate);

        Assert.True(ok);
        Assert.Equal(new DateOnly(2026, 6, 25), date);
        Assert.False(approximate);
    }

    [Theory]
    [InlineData("2026-07")]        // 月のみ
    [InlineData("2026-04-TBD")]    // 年月 + 未定の日
    public void CalendarStartDate_月精度は月初で概算になる(string raw)
    {
        bool ok = EventDate.TryGetCalendarStartDate(raw, out var date, out bool approximate);

        Assert.True(ok);
        Assert.Equal(1, date.Day);          // その月の1日
        Assert.True(approximate);
    }

    [Theory]
    [InlineData("TBD")]
    [InlineData("N/A")]
    [InlineData("2026年7月")]        // ISO でない表記は解析しない
    [InlineData("12026-07")]         // 5桁年は先頭アンカーでマッチしない（誤マッチ防止）
    public void CalendarStartDate_完全に不明ならfalse(string raw)
    {
        bool ok = EventDate.TryGetCalendarStartDate(raw, out _, out bool approximate);

        Assert.False(ok);
        Assert.False(approximate);
    }

    [Fact]
    public void StartDate_過去判定用は月精度を採らない()
    {
        // IsPast が使う日精度メソッドは月のみ表記を解析しない（会期中に消えるのを防ぐため）。
        Assert.False(EventDate.TryGetStartDate("2026-07", out _));
    }
}
