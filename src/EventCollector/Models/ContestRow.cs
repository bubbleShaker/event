namespace EventCollector.Models;

/// <summary>
/// 公式サイトの予定コンテスト表の1行から取り出した確定情報。
/// <paramref name="Start"/> は告知されたタイムゾーン（実質 JST）を保持する。
/// <paramref name="DurationMinutes"/> は 0 なら不明（未公開）を表し、
/// 変換側は終了時刻を空にしてカレンダー既定の1時間に委ねる。
/// </summary>
public sealed record ContestRow(string Slug, string Title, DateTimeOffset Start, long DurationMinutes);
