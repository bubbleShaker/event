using System.Net;
using System.Text;

namespace EventCollector.Tests;

/// <summary>
/// 実サイトを叩かずに収集源を検証するための <see cref="HttpClient"/> スタブ。
/// HTML を fetch する収集源が複数あるため、テスト側でも共有する。
/// </summary>
internal static class StubHttp
{
    /// <summary>URL に関係なく固定の本文・ステータスで応答する <see cref="HttpClient"/> を作る。</summary>
    public static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(body, status));

    /// <summary>
    /// URL に関係なく固定の本文・ステータスで応答するテスト用ハンドラ。
    /// <c>cancellationToken</c> は見ない（キャンセルは <see cref="HttpClient"/> 側が送信前に判定するため、
    /// キャンセルのテストはハンドラまで到達しない）。
    /// </summary>
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
