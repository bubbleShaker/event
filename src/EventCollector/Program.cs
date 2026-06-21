using System.Text.Json;
using EventCollector.Models;
using EventCollector.Notifications;
using EventCollector.Services;

// イベント収集プロトタイプのエントリポイント。
// 収集 → events.json / events.md / runs ログの生成 → 差分があれば通知（commit は次ステップ）。
//
// 実行例（リポジトリ直下から）:
//   ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector
// 出力先は EVENTS_DIR 環境変数で上書き可。既定はリポジトリ直下（"."）。

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
ClaudeEventSource source = new();
EventDiffer differ = new();
MarkdownRenderer renderer = new();
IDiffNotifier notifier = BuildNotifier();

IReadOnlyList<string> themes = themeStore.LoadThemes(themesPath);
Console.WriteLine($"テーマ {themes.Count} 件を読み込み。収集を開始します。");

IReadOnlyList<EventItem> previous = LoadPrevious(dataPath, jsonOptions);

IReadOnlyList<EventItem> current;
try
{
    current = await source.CollectAsync(themes);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"収集に失敗: {ex.Message}");
    return 1;
}

DiffResult diff = differ.Diff(previous, current);
DateTimeOffset now = DateTimeOffset.Now;

Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
Directory.CreateDirectory(runsDir);

await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(current, jsonOptions));
await File.WriteAllTextAsync(eventsMdPath, renderer.RenderEventList(current, now));

string runPath = Path.Combine(runsDir, $"{now:yyyy-MM-dd}.md");
await File.WriteAllTextAsync(runPath, renderer.RenderRunLog(diff, now));

Console.WriteLine(
    $"収集 {current.Count} 件 / 追加 {diff.Added.Count} 変更 {diff.Changed.Count} 削除 {diff.Removed.Count}");
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
