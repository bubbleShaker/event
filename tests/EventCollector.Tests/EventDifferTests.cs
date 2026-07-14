using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary>差分抽出（追加・変更・削除）のテスト。特に色分けメタデータ Group の扱いを固定する。</summary>
public sealed class EventDifferTests
{
    private readonly EventDiffer _differ = new();

    [Fact]
    public void Group差だけでは変更扱いにしない()
    {
        // 旧スナップショットは Group 未設定（null）、今回は色分けのため Group を刻んだだけ。
        // 内容は同じなので、Group が付いただけで「変更」通知が誤爆してはいけない。
        EventItem before = MakeEvent() with { Group = null };
        EventItem after = MakeEvent() with { Group = "C# / .NET" };

        DiffResult diff = _differ.Diff([before], [after]);

        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Group以外の内容差は変更扱いになる()
    {
        EventItem before = MakeEvent() with { Group = "C# / .NET", Summary = "旧概要。" };
        EventItem after = MakeEvent() with { Group = "C# / .NET", Summary = "新概要。" };

        DiffResult diff = _differ.Diff([before], [after]);

        Assert.Single(diff.Changed);
    }

    private static EventItem MakeEvent() => new()
    {
        Title = "勉強会",
        Date = "2026-06-25",
        Location = "Online",
        Url = "https://example.com",
        Theme = "テスト",
        Summary = "テスト用イベント。",
    };
}
