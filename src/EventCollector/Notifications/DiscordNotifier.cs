using System.Text;
using System.Text.Json;
using EventCollector.Models;

namespace EventCollector.Notifications;

/// <summary>Discord Webhook へ差分を POST する通知実装。</summary>
public sealed class DiscordNotifier : IDiffNotifier
{
    private readonly string _webhookUrl;
    private readonly HttpClient _httpClient;
    private readonly DiscordMessageBuilder _messageBuilder;

    /// <summary>Webhook URL を指定して生成する。</summary>
    /// <param name="webhookUrl">Discord の Webhook URL。</param>
    /// <param name="httpClient">差し替え用の HttpClient。省略時は既定。</param>
    /// <param name="messageBuilder">差し替え用のビルダー。省略時は既定。</param>
    public DiscordNotifier(
        string webhookUrl,
        HttpClient? httpClient = null,
        DiscordMessageBuilder? messageBuilder = null)
    {
        _webhookUrl = webhookUrl;
        _httpClient = httpClient ?? new HttpClient();
        _messageBuilder = messageBuilder ?? new DiscordMessageBuilder();
    }

    /// <inheritdoc />
    public async Task NotifyAsync(
        DiffResult diff,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        string content = _messageBuilder.Build(diff, generatedAt);
        string json = JsonSerializer.Serialize(new { content });

        using StringContent body = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response =
            await _httpClient.PostAsync(_webhookUrl, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
