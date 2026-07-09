using System.Text.Json;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// AtCoder のコンテストを収集する <see cref="IEventSource"/>。
/// 公式 REST API が無いため、Kenkoooo AtCoder Problems の <c>contests.json</c> を取得し、
/// これから開催されるコンテストだけを <see cref="EventItem"/> に変換する。
/// 日付・URL が確定情報として得られるため、web_search と違い揺らぎなく取り込める。
/// </summary>
public sealed class AtCoderContestSource : IEventSource
{
    // 事実上の標準となっているコンテスト一覧 API。過去〜未来の全コンテストを1配列で返す。
    private const string ContestsJsonUrl = "https://kenkoooo.com/atcoder/resources/contests.json";

    // 収集の対象期間の上限。既存の web_search 収集（おおむね3か月以内）と揃える。
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(92);

    // AtCoder の時刻はすべて日本時間（JST, UTC+9）で告知される。移植性のため
    // TimeZoneInfo ではなく固定オフセットで JST へ変換する。
    private static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>取得用クライアントと現在時刻の供給を差し込む。省略時は既定の実装を使う。</summary>
    /// <param name="httpClient">差し替え用の <see cref="HttpClient"/>。省略時は新規生成。</param>
    /// <param name="clock">現在時刻の供給。未来判定に使う。省略時は <see cref="DateTimeOffset.UtcNow"/>。</param>
    public AtCoderContestSource(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public string Name => "AtCoder";

    /// <summary>これから開催される（今日以降・約3か月以内の）AtCoder コンテストを収集する。</summary>
    public async Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // CDN の Content-Type 差異で黙って失敗しないよう、本文を明示的に取得して解釈する。
        // 不正 JSON や非 200 応答は例外になり、EventSourceRunner の失敗分離でこの源だけスキップされる。
        string body = await _httpClient.GetStringAsync(ContestsJsonUrl, cancellationToken);
        IReadOnlyList<AtCoderContest>? contests =
            JsonSerializer.Deserialize<IReadOnlyList<AtCoderContest>>(body);

        if (contests is null)
        {
            return [];
        }

        DateTimeOffset now = _clock();
        DateTimeOffset until = now + Horizon;

        // 未来かつ期間内のコンテストだけを対象にする。開催時刻順に並べて安定した出力にする。
        var upcoming =
            from contest in contests
            let start = DateTimeOffset.FromUnixTimeSeconds(contest.StartEpochSecond)
            where start > now && start <= until
            orderby start
            select ToEventItem(contest, start);

        return [.. upcoming];
    }

    /// <summary>コンテスト1件を、JST 表記の <see cref="EventItem"/> へ変換する。</summary>
    private static EventItem ToEventItem(AtCoderContest contest, DateTimeOffset start)
    {
        DateTimeOffset startJst = start.ToOffset(JstOffset);
        long durationMinutes = contest.DurationSecond / 60;

        return new EventItem
        {
            Title = contest.Title,
            Date = startJst.ToString("yyyy-MM-dd"),
            Location = "Online",
            Url = $"https://atcoder.jp/contests/{contest.Id}",
            Theme = "競技プログラミング（AtCoder）",
            Summary = $"{startJst:yyyy-MM-dd HH:mm} JST 開催（約 {durationMinutes} 分）。",
            // 開始時刻・所要時間が確定しているため、カレンダーには時刻付きイベントとして載せる。
            // 所要時間不明（0 分）のときは終了を空にし、Factory 側の既定（1 時間）に委ねる（公式源と対称）。
            StartsAt = startJst,
            EndsAt = durationMinutes > 0 ? startJst.AddMinutes(durationMinutes) : null,
        };
    }
}
