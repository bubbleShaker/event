using System.Text.Json.Serialization;

namespace EventCollector.Models;

/// <summary>
/// Kenkoooo AtCoder Problems の <c>contests.json</c> 1件分。
/// AtCoder に公式 REST API が無いため、事実上の標準であるこのエンドポイントを使う。
/// 過去・未来すべてのコンテストが1配列で返るため、開催時刻での絞り込みは収集源側で行う。
/// </summary>
public sealed record AtCoderContest
{
    /// <summary>コンテスト ID（URL スラッグ。例 <c>abc400</c>）。</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>コンテスト名。</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>開始時刻（Unix epoch 秒、UTC 基準）。</summary>
    [JsonPropertyName("start_epoch_second")]
    public required long StartEpochSecond { get; init; }

    /// <summary>開催時間（秒）。</summary>
    [JsonPropertyName("duration_second")]
    public required long DurationSecond { get; init; }
}
