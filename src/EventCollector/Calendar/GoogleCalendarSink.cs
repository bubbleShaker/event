using System.Net;
using EventCollector.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace EventCollector.Calendar;

/// <summary>
/// サービスアカウント認証で Google カレンダーへイベントを登録する実装。
/// 決定的 ID により、無ければ作成・あれば更新する冪等 upsert を行う。
/// </summary>
public sealed class GoogleCalendarSink : ICalendarSink
{
    private const string ApplicationName = "event-collector";

    private readonly CalendarService _service;
    private readonly string _calendarId;

    /// <summary>既存の <see cref="CalendarService"/> を指定して生成する（テスト差し替え用）。</summary>
    public GoogleCalendarSink(CalendarService service, string calendarId)
    {
        _service = service;
        _calendarId = calendarId;
    }

    /// <summary>サービスアカウント JSON と登録先カレンダー ID から生成する（鍵あり・ローカル等）。</summary>
    /// <param name="credentialsJson">サービスアカウントの鍵 JSON（中身）。</param>
    /// <param name="calendarId">登録先カレンダーの ID。</param>
    public static GoogleCalendarSink Create(string credentialsJson, string calendarId)
    {
        // CredentialFactory がサービスアカウント JSON を解釈する（GoogleCredential.FromJson は非推奨）。
        // ToGoogleCredential() で共通型に変換し、カレンダー編集スコープを付与する。
        GoogleCredential credential = CredentialFactory
            .FromJson<ServiceAccountCredential>(credentialsJson)
            .ToGoogleCredential();

        return new GoogleCalendarSink(BuildService(credential), calendarId);
    }

    /// <summary>
    /// Application Default Credentials（ADC）と登録先カレンダー ID から生成する（鍵レス）。
    /// Workload Identity Federation の auth ステップが実行環境に用意した認証情報を自動で拾う。
    /// </summary>
    /// <param name="calendarId">登録先カレンダーの ID。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    public static async Task<GoogleCalendarSink> CreateWithApplicationDefaultAsync(
        string calendarId, CancellationToken cancellationToken = default)
    {
        GoogleCredential credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken);
        return new GoogleCalendarSink(BuildService(credential), calendarId);
    }

    // 共通: スコープ付与済みの CalendarService を組み立てる。
    private static CalendarService BuildService(GoogleCredential credential)
    {
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.CreateScoped(CalendarService.Scope.Calendar),
            ApplicationName = ApplicationName,
        });
    }

    /// <inheritdoc />
    public async Task<int> SyncAsync(
        IReadOnlyList<EventItem> events, CancellationToken cancellationToken = default)
    {
        int synced = 0;
        foreach (EventItem item in events)
        {
            Event? calendarEvent = CalendarEventFactory.TryCreate(item);
            if (calendarEvent is null)
            {
                // 日付不明（TBD 等）はカレンダーに置けないのでスキップ。
                continue;
            }

            await UpsertAsync(calendarEvent, cancellationToken);
            synced++;
        }

        return synced;
    }

    // ID 指定で Insert し、既に存在（409 Conflict）なら Update する冪等処理。
    private async Task UpsertAsync(Event calendarEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _service.Events.Insert(calendarEvent, _calendarId).ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
        {
            await _service.Events
                .Update(calendarEvent, _calendarId, calendarEvent.Id)
                .ExecuteAsync(cancellationToken);
        }
    }
}
