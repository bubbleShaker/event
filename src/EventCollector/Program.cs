using System.Text.Json;
using EventCollector.Calendar;
using EventCollector.Models;
using EventCollector.Notifications;
using EventCollector.Services;

// イベント収集プロトタイプのエントリポイント。
// 収集 → events.json / events.md / runs ログの生成 → 差分があれば通知（commit は次ステップ）。
//
// 実行例（リポジトリ直下から）:
//   ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector
// 出力先は EVENTS_DIR 環境変数で上書き可。既定はリポジトリ直下（"."）。
//
// --notify-test を渡すと、Claude API 収集をスキップして Discord 通知の疎通だけ確認する
// （API キー不要・課金なし）:
//   DISCORD_WEBHOOK_URL=... dotnet run --project src/EventCollector -- --notify-test

// 通知疎通テストモード: API を呼ばず、サンプル差分を既存の通知経路へ流すだけ。
if (args.Contains("--notify-test"))
{
    return await RunNotifyTestAsync();
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY が未設定。環境変数に API キーを設定してください。");
    return 1;
}

string baseDir = Environment.GetEnvironmentVariable("EVENTS_DIR") ?? ".";
string themesPath = Path.Combine(baseDir, "config", "themes.md");
string dataPath = Path.Combine(baseDir, "data", "events.json");
string eventsMdPath = Path.Combine(baseDir, "events.md");
string runsDir = Path.Combine(baseDir, "runs");

JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
};

ThemeStore themeStore = new();
SnapshotReconciler reconciler = new();
EventDiffer differ = new();
MarkdownRenderer renderer = new();
IDiffNotifier notifier = BuildNotifier();
ICalendarSink calendarSink = await BuildCalendarSinkAsync();

// テーマ群ごとに独立した収集源を組む。グループを1つ追加/削除するだけで対象が増減する。
IReadOnlyList<ThemeGroup> groups = themeStore.LoadGroups(themesPath);
IReadOnlyList<IEventSource> sources =
    [.. groups.Select(g => (IEventSource)new ClaudeGroupSource(g))];
Console.WriteLine(
    $"テーマ群 {groups.Count} 件（テーマ計 {groups.Sum(g => g.Themes.Count)} 件）を読み込み。収集を開始します。");

IReadOnlyList<EventItem> previous = LoadPrevious(dataPath, jsonOptions);

// 各収集源を独立実行し、1つ失敗しても他は続行して結果をマージ・重複除去する。
EventSourceRunner runner = new(Console.WriteLine, Console.Error.WriteLine);
IReadOnlyList<EventItem> current = await runner.CollectAllAsync(sources);
if (current.Count == 0)
{
    Console.Error.WriteLine("全収集源が0件だった。設定・ネットワーク・API キーを確認してください。");
    return 1;
}

DateTimeOffset now = DateTimeOffset.Now;

// 収集は非決定的なため、前回分と和集合マージし過去日のみ除外した snapshot を真実の源にする。
// 差分は「置き換え後」ではなくこの merged に対して取り、削除＝開催済みのみとする。
IReadOnlyList<EventItem> merged =
    reconciler.Reconcile(previous, current, DateOnly.FromDateTime(now.Date));
DiffResult diff = differ.Diff(previous, merged);

Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
Directory.CreateDirectory(runsDir);

await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(merged, jsonOptions));
await File.WriteAllTextAsync(eventsMdPath, renderer.RenderEventList(merged, now));

string runPath = Path.Combine(runsDir, $"{now:yyyy-MM-dd}.md");
await File.WriteAllTextAsync(runPath, renderer.RenderRunLog(diff, now));

Console.WriteLine(
    $"収集 {current.Count} 件 / 一覧 {merged.Count} 件 / 追加 {diff.Added.Count} 変更 {diff.Changed.Count} 削除 {diff.Removed.Count}");
Console.WriteLine($"出力: {eventsMdPath}, {dataPath}, {runPath}");

if (diff.HasChanges)
{
    try
    {
        bool sent = await notifier.NotifyAsync(diff, now);
        Console.WriteLine(sent
            ? "差分を通知しました。"
            : "通知先（DISCORD_WEBHOOK_URL）が未設定のため通知をスキップしました。");
    }
    catch (Exception ex)
    {
        // 通知失敗は収集自体の失敗にはしない。
        Console.Error.WriteLine($"通知に失敗: {ex.Message}");
    }
}

// Google カレンダー登録（未設定なら NullCalendarSink でスキップ）。失敗しても収集は成功扱い。
try
{
    int registered = await calendarSink.SyncAsync(merged);
    Console.WriteLine(registered > 0
        ? $"Google カレンダーに {registered} 件登録しました。"
        : "Google カレンダー連携は未設定/対象なしのためスキップしました。");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"カレンダー登録に失敗: {ex.Message}");
}

// TODO（次ステップ）: git への自動コミットを追加する。

return 0;

static IReadOnlyList<EventItem> LoadPrevious(string dataPath, JsonSerializerOptions options)
{
    if (!File.Exists(dataPath))
    {
        return [];
    }

    string json = File.ReadAllText(dataPath);
    return JsonSerializer.Deserialize<List<EventItem>>(json, options) ?? [];
}

// DISCORD_WEBHOOK_URL が設定されていれば Discord 通知、未設定なら no-op を返す。
static IDiffNotifier BuildNotifier()
{
    string? webhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
    return string.IsNullOrWhiteSpace(webhookUrl)
        ? new NullNotifier()
        : new DiscordNotifier(webhookUrl);
}

// 登録先カレンダー ID があれば Google 連携を有効化、無ければ no-op を返す。
// 認証は鍵 JSON（GOOGLE_CALENDAR_CREDENTIALS）があればそれを、無ければ ADC（WIF など）を使う。
static async Task<ICalendarSink> BuildCalendarSinkAsync()
{
    string? calendarId = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_ID");
    if (string.IsNullOrWhiteSpace(calendarId))
    {
        return new NullCalendarSink();
    }

    string? credentialsJson = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_CREDENTIALS");
    return string.IsNullOrWhiteSpace(credentialsJson)
        ? await GoogleCalendarSink.CreateWithApplicationDefaultAsync(calendarId)
        : GoogleCalendarSink.Create(credentialsJson, calendarId);
}

// 通知疎通テスト: Claude API を呼ばず、サンプル差分を通知経路へ流す。
// webhook 未設定なら NullNotifier が false を返すので exit 1 で明示する。
static async Task<int> RunNotifyTestAsync()
{
    DiffResult sample = new()
    {
        Added =
        [
            new EventItem
            {
                Title = "通知テスト",
                Date = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
                Location = "Online",
                Url = "https://github.com/bubbleShaker/event",
                Theme = "(notify-test)",
                Summary = "Discord Webhook の疎通確認用テスト通知です。",
            },
        ],
        Changed = [],
        Removed = [],
    };

    try
    {
        bool sent = await BuildNotifier().NotifyAsync(sample, DateTimeOffset.Now);
        if (sent)
        {
            Console.WriteLine("テスト通知を送信しました。");
            return 0;
        }

        Console.Error.WriteLine("DISCORD_WEBHOOK_URL が未設定のため送信できません。");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"テスト通知の送信に失敗: {ex.Message}");
        return 1;
    }
}
