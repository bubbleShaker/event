using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>Claude API（web_search + 構造化出力）でイベントを収集する。</summary>
public sealed class ClaudeEventSource
{
    // 構造化出力のスキーマ。全フィールドを required かつ additionalProperties: false にする
    // 必要がある（構造化出力の制約）。不明な値はモデルが "TBD" / "N/A" / "Online" を埋める。
    private const string SchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "events": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title":    { "type": "string" },
                  "date":     { "type": "string" },
                  "location": { "type": "string" },
                  "url":      { "type": "string" },
                  "theme":    { "type": "string" },
                  "summary":  { "type": "string" }
                },
                "required": ["title", "date", "location", "url", "theme", "summary"],
                "additionalProperties": false
              }
            }
          },
          "required": ["events"],
          "additionalProperties": false
        }
        """;

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AnthropicClient _client;

    /// <summary>既定では <c>ANTHROPIC_API_KEY</c> 環境変数から認証する。</summary>
    /// <param name="client">差し替え用のクライアント。省略時は既定クライアント。</param>
    public ClaudeEventSource(AnthropicClient? client = null)
    {
        _client = client ?? new AnthropicClient();
    }

    /// <summary>テーマに合致する直近のイベントを Web 検索して収集する。</summary>
    /// <param name="themes">収集テーマの一覧。</param>
    /// <returns>収集したイベント一覧。</returns>
    public async Task<IReadOnlyList<EventItem>> CollectAsync(IReadOnlyList<string> themes)
    {
        string prompt = BuildPrompt(themes);

        Dictionary<string, JsonElement> schema =
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(SchemaJson)!;

        // TODO: web_search はサーバー側で複数反復し stop_reason "pause_turn" を返すことがある。
        // 実運用では pause_turn の継続ループを追加する（DESIGN.md の TODO 1）。
        MessageCreateParams parameters = new()
        {
            Model = Model.ClaudeSonnet4_6,
            MaxTokens = 8000,
            Tools = [new ToolUnion(new WebSearchTool20260209())],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema },
            },
            Messages = [new() { Role = Role.User, Content = prompt }],
        };

        Message response = await _client.Messages.Create(parameters);

        // web_search を挟むと content に複数ブロックが並ぶため、最終の text ブロックを採用する。
        TextBlock? jsonBlock = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .LastOrDefault();

        if (jsonBlock is null)
        {
            throw new InvalidOperationException("構造化出力のテキストブロックが得られなかった。");
        }

        CollectionResult? result =
            JsonSerializer.Deserialize<CollectionResult>(jsonBlock.Text, DeserializeOptions);

        return result?.Events ?? [];
    }

    private static string BuildPrompt(IReadOnlyList<string> themes)
    {
        string themeList = string.Join("\n", themes.Select(t => $"- {t}"));
        return
            $"""
            次のテーマに合致する、これから開催される（今日以降、おおむね3か月以内の）
            プログラミング関連イベントを Web 検索で収集してください。

            テーマ:
            {themeList}

            条件:
            - 日本国内またはオンライン開催を対象とする。
            - date は可能な限り ISO 8601（YYYY-MM-DD）。未定なら "TBD"。
            - location 不明は "N/A"、オンラインは "Online"。url 不明は "N/A"。
            - theme は上記テーマのいずれかを明記する。
            - 重複は除外し、出典となる公式ページの url を優先する。
            - 指定された JSON スキーマに厳密に従って出力する。
            """;
    }
}
