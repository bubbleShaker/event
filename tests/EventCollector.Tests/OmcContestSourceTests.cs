using System.Net;
using EventCollector.Models;
using EventCollector.Services;
using Xunit;

namespace EventCollector.Tests;

/// <summary>
/// <see cref="OmcContestSource"/> の HTML パース・期間フィルタ・JST 整形のテスト。
/// ネットワークはスタブハンドラで固定 HTML を返し、実サイトを叩かずに検証する。
/// フィクスチャは実際のコンテスト一覧ページ（予定/開催中/過去の3節）の構造をそのまま縮小したもの。
/// </summary>
public sealed class OmcContestSourceTests
{
    // 現在時刻を固定し、未来判定を決定的にする。
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

    // 予定表に4行（所要時間未定 / 所要時間確定 / 略号を含まない名称 / 92日超）、
    // 過去表に1行を持つページ。過去表の行を拾わないことも併せて確かめる。
    private const string ContestsHtml =
        """
        <section id="upcoming_contests">
          <table class="table table-sm table-striped">
            <thead class="thead-dark"><tr><th>名前</th><th>開始時刻</th><th>時間</th></tr></thead>
            <tbody>             <tr>
              <th> <a href="https://onlinemathcontest.com/contests/omcb085">OMCB085</a> </th>
              <td>2026-09-04 21:00:00</td>
              <td>
                                  開始1時間前に公開
                              </td>
            </tr>             <tr>
              <th> <a href="https://onlinemathcontest.com/contests/omc293">OMC293</a> </th>
              <td>2026-09-11 21:00:00</td>
              <td>
                                  100 分
                              </td>
            </tr>             <tr>
              <th> <a href="https://onlinemathcontest.com/contests/hamamatsu2027">第6回高校生数学コンテスト&nbsp;in  Hamamatsu 予選</a> </th>
              <td>2026-09-20 13:00:00</td>
              <td>
                                  120 分
                              </td>
            </tr>             <tr>
              <th> <a href="https://onlinemathcontest.com/contests/omc300">OMC300</a> </th>
              <td>2027-01-15 21:00:00</td>
              <td>
                                  100 分
                              </td>
            </tr>  </tbody>
          </table>
        </section>
        <section id="running_contests">
          <table><tbody>  </tbody></table>
        </section>
        <section id="past_contests">
          <table><tbody>             <tr>
              <th> <a href="https://onlinemathcontest.com/contests/omc292">OMC292</a> </th>
              <td>2026-08-26 21:00:00</td>
              <td>
                                  80 分
                              </td>
            </tr>  </tbody></table>
        </section>
        """;

    [Fact]
    public async Task 予定表の未来かつ期間内のコンテストだけを開催順に収集する()
    {
        var source = new OmcContestSource(StubHttp.Client(ContestsHtml), () => Now);

        IReadOnlyList<EventItem> events = await source.CollectAsync();

        // omc300（92日超）と過去表の omc292 は除外。予定表の3件が開催順に並ぶ。
        Assert.Collection(
            events,
            first => Assert.Equal("OMCB085", first.Title),
            second => Assert.Equal("OMC293", second.Title),
            third => Assert.Equal("第6回高校生数学コンテスト in Hamamatsu 予選", third.Title));
    }

    [Fact]
    public async Task JST日付とURLとテーマと所要時間を整形する()
    {
        var source = new OmcContestSource(StubHttp.Client(ContestsHtml), () => Now);

        EventItem omc293 = (await source.CollectAsync())[1];

        Assert.Equal("2026-09-11", omc293.Date);
        Assert.Equal("Online", omc293.Location);
        Assert.Equal("https://onlinemathcontest.com/contests/omc293", omc293.Url);
        Assert.Equal("数学コンテスト（OnlineMathContest）", omc293.Theme);
        // カレンダーの色は AtCoder テーマ群と同じグループに揃える。
        Assert.Equal("AtCoder / 競技プログラミング", omc293.Group);
        Assert.Contains("2026-09-11 21:00 JST", omc293.Summary);
        Assert.Contains("100 分", omc293.Summary);
        // 時刻付きカレンダー登録用に JST の開始・終了を持つ（終了 = 開始 + 100 分）。
        var jst = TimeSpan.FromHours(9);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 21, 0, 0, jst), omc293.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 22, 40, 0, jst), omc293.EndsAt);
    }

    [Fact]
    public async Task 所要時間が未公開なら終了時刻を空にする()
    {
        var source = new OmcContestSource(StubHttp.Client(ContestsHtml), () => Now);

        EventItem omcb085 = (await source.CollectAsync())[0];

        // 「開始1時間前に公開」の回。開始は確定、終了は Factory 側の既定（1 時間）へ委ねる。
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 21, 0, 0, TimeSpan.FromHours(9)), omcb085.StartsAt);
        Assert.Null(omcb085.EndsAt);
        Assert.Contains("開始直前に公開", omcb085.Summary);
    }

    [Fact]
    public async Task 名称のHTMLエンティティと連続空白を正規化する()
    {
        var source = new OmcContestSource(StubHttp.Client(ContestsHtml), () => Now);

        EventItem hamamatsu = (await source.CollectAsync())[2];

        // "第6回…&nbsp;in  Hamamatsu 予選" → 実体参照を戻し連続空白を1つに畳む。
        // 名称は加工しない（web_search 側と EventItem.Key を一致させ重複登録を防ぐため）。
        Assert.Equal("第6回高校生数学コンテスト in Hamamatsu 予選", hamamatsu.Title);
    }

    [Fact]
    public async Task 予定表が無ければ0件を返す()
    {
        var source = new OmcContestSource(
            StubHttp.Client("<section id=\"past_contests\"><table><tbody></tbody></table></section>"), () => Now);

        Assert.Empty(await source.CollectAsync());
    }

    [Fact]
    public async Task 予定表が空なら0件を返す()
    {
        // 予定が無い期間は tbody が空になる（開催中の表と同じ形）。これは異常ではない。
        var source = new OmcContestSource(
            StubHttp.Client("<section id=\"upcoming_contests\"><table><tbody>  </tbody></table></section>"), () => Now);

        Assert.Empty(await source.CollectAsync());
    }

    [Fact]
    public async Task 行はあるのに解析できなければ例外を投げる_構造変化を検知する()
    {
        // 「本当に予定が無い」と「サイト構造が変わって読めない」を区別するため、
        // 行があるのに1件も読めない場合は失敗させ、失敗分離のエラーログに載せる。
        const string Broken =
            """
            <section id="upcoming_contests"><table><tbody>
              <tr><th><span>OMCB085</span></th><td>2026/09/04 21:00</td><td>60 分</td></tr>
            </tbody></table></section>
            """;

        var source = new OmcContestSource(StubHttp.Client(Broken), () => Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.CollectAsync());
    }

    [Fact]
    public async Task 開始時刻を持たない行は読み飛ばす()
    {
        // 実ページの表には「すべてのコンテストを見る」等のリンク行が混ざる。
        // 解析できる行が1件でもあれば、残りを読み飛ばして正常に返す。
        const string WithLinkRow =
            """
            <section id="upcoming_contests"><table><tbody>
              <tr><th> <a href="https://onlinemathcontest.com/contests/omc293">OMC293</a> </th>
                  <td>2026-09-11 21:00:00</td><td>100 分</td></tr>
              <tr><td colspan="3"><a href="https://onlinemathcontest.com/contests/all">すべてのコンテストを見る</a></td></tr>
            </tbody></table></section>
            """;

        var source = new OmcContestSource(StubHttp.Client(WithLinkRow), () => Now);

        EventItem only = Assert.Single(await source.CollectAsync());
        Assert.Equal("OMC293", only.Title);
    }

    [Fact]
    public async Task キャンセル済みトークンでは収集しない()
    {
        var source = new OmcContestSource(StubHttp.Client(ContestsHtml), () => Now);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.CollectAsync(cts.Token));
    }

    [Fact]
    public async Task 非200応答は例外を投げる_失敗分離に委ねる()
    {
        var source = new OmcContestSource(
            StubHttp.Client("Service Unavailable", HttpStatusCode.ServiceUnavailable), () => Now);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.CollectAsync());
    }
}
