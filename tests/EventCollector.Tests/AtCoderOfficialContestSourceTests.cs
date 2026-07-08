using System.Net;
using System.Text;
using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary>
/// <see cref="AtCoderOfficialContestSource"/> の HTML パース・期間フィルタ・JST 整形のテスト。
/// ネットワークはスタブハンドラで固定 HTML を返し、実サイトを叩かずに検証する。
/// フィクスチャは実際の予定コンテスト表(id="contest-table-upcoming")の構造をそのまま縮小したもの。
/// </summary>
public sealed class AtCoderOfficialContestSourceTests
{
    // 現在時刻を固定し、未来判定を決定的にする。
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);

    // past01（過去）/ near(abc466, 3日後) / mid(arc224, 4日後) / far(92日超) の4行を持つ表。
    private const string UpcomingHtml =
        """
        <div id="contest-table-upcoming">
          <h3>予定されたコンテスト</h3>
          <table><thead><tr><th>開始時刻</th><th>コンテスト名</th><th>時間</th><th>Rated対象</th></tr></thead>
          <tbody>
            <tr>
              <td class="text-center"><a href='http://x?iso=1'><time class='fixtime fixtime-full'>2026-07-07 21:00:00+0900</time></a></td>
              <td ><span title="Algorithm">Ⓐ</span><span>◉</span> <a href="/contests/past01">過去コン</a></td>
              <td class="text-center">01:40</td>
              <td class="text-center"> ~ 1999</td>
            </tr>
            <tr>
              <td class="text-center"><a href='http://x?iso=2'><time class='fixtime fixtime-full'>2026-07-11 21:00:00+0900</time></a></td>
              <td ><span title="Algorithm">Ⓐ</span><span class="user-blue">◉</span> <a href="/contests/abc466">AtCoder Beginner Contest 466</a></td>
              <td class="text-center">01:40</td>
              <td class="text-center"> ~ 1999</td>
            </tr>
            <tr>
              <td class="text-center"><a href='http://x?iso=3'><time class='fixtime fixtime-full'>2026-07-12 21:00:00+0900</time></a></td>
              <td ><span title="Algorithm">Ⓐ</span><span class="user-yellow">◉</span> <a href="/contests/arc224">AtCoder&nbsp;Regular  Contest 224</a></td>
              <td class="text-center">02:00</td>
              <td class="text-center">800 ~ 2399</td>
            </tr>
            <tr>
              <td class="text-center"><a href='http://x?iso=4'><time class='fixtime fixtime-full'>2026-11-01 21:00:00+0900</time></a></td>
              <td ><span title="Algorithm">Ⓐ</span><span>◉</span> <a href="/contests/far01">遠い未来コン</a></td>
              <td class="text-center">01:40</td>
              <td class="text-center"> ~ 1999</td>
            </tr>
          </tbody></table>
        </div>
        <div id="contest-table-recent"><tbody>
          <tr><td><time class='fixtime fixtime-full'>2026-07-05 21:00:00+0900</time></td>
          <td><a href="/contests/abc465">終わったコン</a></td><td class="text-center">01:40</td><td>-</td></tr>
        </tbody></div>
        """;

    [Fact]
    public async Task 未来かつ期間内のコンテストだけを開催順に収集する()
    {
        var source = new AtCoderOfficialContestSource(StubClient(UpcomingHtml), () => Now);

        IReadOnlyList<EventItem> events = await source.CollectAsync();

        // past01(過去) と far01(92日超) は除外。recent 表は対象外。abc466 → arc224 の2件。
        Assert.Collection(
            events,
            first => Assert.Equal("AtCoder Beginner Contest 466", first.Title),
            second => Assert.Equal("AtCoder Regular Contest 224", second.Title));
    }

    [Fact]
    public async Task JST日付とURLとテーマと所要時間を整形する()
    {
        var source = new AtCoderOfficialContestSource(StubClient(UpcomingHtml), () => Now);

        EventItem abc = (await source.CollectAsync())[0];

        Assert.Equal("2026-07-11", abc.Date);
        Assert.Equal("Online", abc.Location);
        Assert.Equal("https://atcoder.jp/contests/abc466", abc.Url);
        Assert.Equal("競技プログラミング（AtCoder）", abc.Theme);
        Assert.Contains("2026-07-11 21:00 JST", abc.Summary);
        Assert.Contains("100 分", abc.Summary); // 01:40 = 100 分
    }

    [Fact]
    public async Task 名称のHTMLエンティティと連続空白を正規化する()
    {
        var source = new AtCoderOfficialContestSource(StubClient(UpcomingHtml), () => Now);

        EventItem arc = (await source.CollectAsync())[1];

        // "AtCoder&nbsp;Regular  Contest 224" → 実体参照を戻し連続空白を1つに畳む。
        Assert.Equal("AtCoder Regular Contest 224", arc.Title);
    }

    [Fact]
    public async Task 予定表が無ければ0件を返す()
    {
        var source = new AtCoderOfficialContestSource(
            StubClient("<div id=\"contest-table-recent\"><tbody></tbody></div>"), () => Now);

        Assert.Empty(await source.CollectAsync());
    }

    [Fact]
    public async Task 非200応答は例外を投げる_失敗分離に委ねる()
    {
        var source = new AtCoderOfficialContestSource(
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
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            });
    }
}
