using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using EventCollector.Models;

namespace EventCollector.Services;

/// <summary>
/// 1つのテーマ群を Claude API で収集する <see cref="IEventSource"/>。
/// 検索（ストリーミング）と構造化抽出の2フェーズで行う。グループごとに独立した
/// 検索枠（<see cref="WebSearchMaxUses"/>）を持つため、系統の異なるテーマが検索枠を
/// 食い合って取りこぼす問題が起きない。
/// </summary>
public sealed class ClaudeGroupSource : IEventSource
{
    // グループ1つあたりの web_search 回数上限。全テーマを1回の呼び出しに詰めていた頃は
    // 全体で3回だったが、グループごとに独立して3回まで検索できるようにする。
    private const int WebSearchMaxUses = 3;

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
    private readonly ThemeGroup _group;

    /// <summary>担当グループと、既定では <c>ANTHROPIC_API_KEY</c> 環境変数で認証するクライアントを渡す。</summary>
    /// <param name="group">この収集源が担当するテーマ群。</param>
    /// <param name="client">差し替え用のクライアント。省略時は既定クライアント。</param>
    public ClaudeGroupSource(ThemeGroup group, AnthropicClient? client = null)
    {
        _group = group;
        _client = client ?? new AnthropicClient();
    }

    /// <inheritdoc />
    public string Name => _group.Name;

    /// <summary>担当グループのテーマに合致する直近のイベントを Web 検索して収集する。</summary>
    public async Task<IReadOnlyList<EventItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // Phase 1: web_search で検索し、所見をプレーンテキストで受け取る。
        // 長時間化しうるためストリーミングで実行し、タイムアウトによるキャンセルを避ける。
        string findings = await SearchAsync(cancellationToken);

        // Phase 2: ツール無し・構造化出力で所見を JSON へ整形する（高速かつ確実）。
        return await ExtractAsync(findings, cancellationToken);
    }

    private async Task<string> SearchAsync(CancellationToken cancellationToken)
    {
        MessageCreateParams searchParams = new()
        {
            // コスト優先: Haiku 4.5 + basic web search。動的フィルタリング（内部 code execution）
            // を伴う _20260209 は重く高コストなため、軽量な _20250305 を使う。
            Model = "claude-haiku-4-5",
            MaxTokens = 2000,
            // MaxUses で検索回数を制限し、暴走・長時間化・課金増を防ぐ。
            Tools = [new ToolUnion(new WebSearchTool20250305 { MaxUses = WebSearchMaxUses })],
            Messages = [new() { Role = Role.User, Content = BuildSearchPrompt() }],
        };

        StringBuilder findings = new();
        await foreach (RawMessageStreamEvent streamEvent in
            _client.Messages.CreateStreaming(searchParams, cancellationToken: cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                delta.Delta.TryPickText(out var text))
            {
                findings.Append(text.Text);
            }
        }

        return findings.ToString();
    }

    private async Task<IReadOnlyList<EventItem>> ExtractAsync(string findings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(findings))
        {
            return [];
        }

        Dictionary<string, JsonElement> schema =
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(SchemaJson)!;

        MessageCreateParams extractParams = new()
        {
            // 抽出はツール無し・構造化出力のみ。Haiku 4.5 は構造化出力に対応し最も安価。
            Model = "claude-haiku-4-5",
            MaxTokens = 2000,
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema },
            },
            Messages = [new() { Role = Role.User, Content = BuildExtractPrompt(findings) }],
        };

        Message response = await _client.Messages.Create(extractParams, cancellationToken: cancellationToken);

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

        // 自分の担当グループ名を刻む。カレンダー登録時、テーマグループ単位で色分けする軸になる
        // （Theme は Claude が埋める自由記述で表記が揺れるため、色のキーには安定したグループ名を使う）。
        return (result?.Events ?? [])
            .Select(e => e with { Group = _group.Name })
            .ToList();
    }

    private string BuildSearchPrompt()
    {
        string themeList = string.Join("\n", _group.Themes.Select(t => $"- {t}"));
        return
            $"""
            次のテーマ（分野「{_group.Name}」）に合致する、これから開催される（今日以降、おおむね3か月以内の）
            イベントを Web 検索で調べてください。日本国内またはオンライン開催を対象とします。

            テーマ:
            {themeList}

            見つかったイベントを、1件1行のプレーンテキストで列挙してください（JSONにはしない）。
            各行は次の項目を含めます。出典は公式ページの URL を優先してください。
            - イベント名 / 開催日（分かれば YYYY-MM-DD、未定なら TBD） / 開催地（オンラインは Online、不明は N/A）
              / 公式URL（不明は N/A） / 該当テーマ / 1文の概要
            重複は除外してください。
            """;
    }

    private static string BuildExtractPrompt(string findings)
    {
        return
            $"""
            次の「イベント所見」を、指定された JSON スキーマに厳密に従って整形してください。
            新しい情報は加えず、所見に書かれている内容のみを使ってください。
            不明な値は date="TBD"、location は不明なら "N/A"・オンラインなら "Online"、url 不明は "N/A" とします。

            イベント所見:
            {findings}
            """;
    }
}
