using System.Net;
using System.Text;
using System.Text.Json;
using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary>
/// <see cref="AtCoderContestSource"/> の期間フィルタ・JST 変換・整形に関するテスト。
/// ネットワークはスタブハンドラで固定 JSON を返し、Claude API もネットワークも呼ばずに検証する。
/// </summary>
public sealed class AtCoderContestSourceTests
{
    // 現在時刻を固定し、未来判定を決定的にする。
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    [Fact]
    public async Task 未来かつ期間内のコンテストだけを収集する()
    {
        long past = new DateTimeOffset(2026, 7, 7, 21, 0, 0, Jst).ToUnixTimeSeconds();   // 過去 → 除外
        long near = new DateTimeOffset(2026, 7, 12, 9, 0, 0, Jst).ToUnixTimeSeconds();   // 4日後 → 採用
        long mid = new DateTimeOffset(2026, 8, 7, 21, 0, 0, Jst).ToUnixTimeSeconds();    // 約30日後 → 採用
        long far = new DateTimeOffset(2026, 11, 1, 21, 0, 0, Jst).ToUnixTimeSeconds();   // 92日超 → 除外

        string json =
            $$"""
            [
              { "id": "past01", "title": "過去コン",   "start_epoch_second": {{past}}, "duration_second": 6000 },
              { "id": "abc400", "title": "AtCoder Beginner Contest 400", "start_epoch_second": {{near}}, "duration_second": 6000 },
              { "id": "arc200", "title": "AtCoder Regular Contest 200",   "start_epoch_second": {{mid}},  "duration_second": 7200 },
              { "id": "far01",  "title": "遠い未来コン", "start_epoch_second": {{far}},  "duration_second": 6000 }
            ]
            """;

        var source = new AtCoderContestSource(StubClient(json), () => Now);

        IReadOnlyList<EventItem> events = await source.CollectAsync();

        // 開催時刻順に、near → mid の2件だけが残る。
        Assert.Collection(
            events,
            first => Assert.Equal("AtCoder Beginner Contest 400", first.Title),
            second => Assert.Equal("AtCoder Regular Contest 200", second.Title));
    }

    [Fact]
    public async Task JST日付とURLとテーマと概要を整形する()
    {
        long near = new DateTimeOffset(2026, 7, 12, 9, 0, 0, Jst).ToUnixTimeSeconds();
        string json =
            $$"""
            [
              { "id": "abc400", "title": "AtCoder Beginner Contest 400", "start_epoch_second": {{near}}, "duration_second": 6000 }
            ]
            """;

        var source = new AtCoderContestSource(StubClient(json), () => Now);

        EventItem item = Assert.Single(await source.CollectAsync());

        Assert.Equal("2026-07-12", item.Date);
        Assert.Equal("Online", item.Location);
        Assert.Equal("https://atcoder.jp/contests/abc400", item.Url);
        Assert.Equal("競技プログラミング（AtCoder）", item.Theme);
        Assert.Contains("2026-07-12 09:00 JST", item.Summary);
        Assert.Contains("100 分", item.Summary); // 6000 秒 = 100 分
        // 時刻付きカレンダー登録用に JST の開始・終了を持つ（終了 = 開始 + 100 分）。
        Assert.Equal(new DateTimeOffset(2026, 7, 12, 9, 0, 0, Jst), item.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 12, 10, 40, 0, Jst), item.EndsAt);
    }

    [Fact]
    public async Task 空配列なら0件を返す()
    {
        var source = new AtCoderContestSource(StubClient("[]"), () => Now);

        Assert.Empty(await source.CollectAsync());
    }

    [Fact]
    public async Task 不正JSONは例外を投げる_失敗分離に委ねる()
    {
        // 収集源は握り潰さず例外を伝播し、EventSourceRunner 側でこの源だけスキップさせる設計。
        var source = new AtCoderContestSource(StubClient("not-json"), () => Now);

        await Assert.ThrowsAsync<JsonException>(() => source.CollectAsync());
    }

    [Fact]
    public async Task 非200応答は例外を投げる()
    {
        var source = new AtCoderContestSource(
            StubClient("Service Unavailable", HttpStatusCode.ServiceUnavailable), () => Now);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.CollectAsync());
    }

    // 固定の本文・ステータスで応答する HttpClient を組み立てる。
    private static HttpClient StubClient(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(body, status));

    /// <summary>URL に関係なく固定の本文・ステータスで応答するテスト用ハンドラ。</summary>
    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
