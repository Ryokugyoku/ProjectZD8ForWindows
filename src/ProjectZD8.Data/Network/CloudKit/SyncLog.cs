using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit の要求と同期が書く診断ログです（分類 <c>sync</c>、ID は 9000 番台）。
/// </summary>
/// <remarks>
/// <para>URL・トークン・レコードの中身は書きません。操作（<c>private/records/query</c> など）、レコード種別、件数、HTTP の状態、所要時間だけです。</para>
/// <para>URL を書くときは <see cref="Redact.Url(Uri?)"/> を通します。</para>
/// </remarks>
internal static partial class SyncLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.Sync);

    /// <summary><see cref="Stopwatch.GetTimestamp"/> からの経過をミリ秒にします。</summary>
    /// <param name="started">開始時の <see cref="Stopwatch.GetTimestamp"/>。</param>
    /// <returns>経過ミリ秒。</returns>
    public static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>CloudKit の要求が成功したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operation">操作（<c>private/records/query</c> など）。</param>
    /// <param name="recordType">レコード種別。ないときは空。</param>
    /// <param name="count">応答のレコードの件数。</param>
    /// <param name="status">HTTP の状態。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9001,
        EventName = "sync.request.completed",
        Level = LogLevel.Debug,
        Message = "CloudKit {Operation} {RecordType} → {Status}（{Count} 件、{ElapsedMs} ms）")]
    public static partial void RequestCompleted(ILogger logger, string operation, string recordType, int count, int status, long elapsedMs);

    /// <summary>CloudKit の要求が失敗したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="operation">操作。</param>
    /// <param name="recordType">レコード種別。ないときは空。</param>
    /// <param name="status">HTTP の状態。応答がなければ 0。</param>
    /// <param name="serverErrorCode">CloudKit のエラーコード。なければ空。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9002,
        EventName = "sync.request.failed",
        Level = LogLevel.Error,
        Message = "CloudKit の要求に失敗しました: {Operation} {RecordType} → {Status} {ServerErrorCode}（{ElapsedMs} ms）")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string operation, string recordType, int status, string serverErrorCode, long elapsedMs);

    /// <summary>CloudKit にサインインが必要と言われたことを書きます（トークンは消す）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operation">操作。</param>
    /// <param name="hasRedirect">サインイン用の URL が応答にあったか。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9003,
        EventName = "sync.request.auth-required",
        Level = LogLevel.Warning,
        Message = "CloudKit がサインインを求めています: {Operation}（リダイレクトあり={HasRedirect}、{ElapsedMs} ms）")]
    public static partial void AuthenticationRequired(ILogger logger, string operation, bool hasRedirect, long elapsedMs);

    /// <summary>CloudKit の要求がやめられたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operation">操作。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9004,
        EventName = "sync.request.canceled",
        Level = LogLevel.Debug,
        Message = "CloudKit の要求がやめられました: {Operation}（{ElapsedMs} ms）")]
    public static partial void RequestCanceled(ILogger logger, string operation, long elapsedMs);

    /// <summary>資産（大きなデータ）の転送が成功したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operation">操作（<c>asset/download</c>・<c>asset/upload</c>）。</param>
    /// <param name="bytes">バイト数。</param>
    /// <param name="status">HTTP の状態。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9005,
        EventName = "sync.asset.transferred",
        Level = LogLevel.Debug,
        Message = "CloudKit {Operation} → {Status}（{Bytes} バイト、{ElapsedMs} ms）")]
    public static partial void AssetTransferred(ILogger logger, string operation, long bytes, int status, long elapsedMs);

    /// <summary>資産の転送が失敗したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="operation">操作。</param>
    /// <param name="status">HTTP の状態。応答がなければ 0。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(
        EventId = 9006,
        EventName = "sync.asset.failed",
        Level = LogLevel.Error,
        Message = "CloudKit の資産の転送に失敗しました: {Operation} → {Status}（{ElapsedMs} ms）")]
    public static partial void AssetFailed(ILogger logger, Exception exception, string operation, int status, long elapsedMs);

    /// <summary>車両の同期に失敗し、端末内の車両で続けることを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    [LoggerMessage(
        EventId = 9010,
        EventName = "sync.vehicles.deferred",
        Level = LogLevel.Warning,
        Message = "車両の同期に失敗したため、端末内の車両で続けます（次回に再試行します）")]
    public static partial void VehiclesDeferred(ILogger logger, Exception exception);

    /// <summary>読めない車両カタログを使わず、上書きもしないことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    [LoggerMessage(
        EventId = 9011,
        EventName = "sync.vehicles.catalog.invalid",
        Level = LogLevel.Warning,
        Message = "CloudKit の車両カタログを読めないため使いません")]
    public static partial void VehicleCatalogInvalid(ILogger logger, Exception exception);

    /// <summary>削除済みの車両 ID の一覧を読めず、空として扱うことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    [LoggerMessage(
        EventId = 9012,
        EventName = "sync.vehicles.deleted-ids.invalid",
        Level = LogLevel.Warning,
        Message = "削除済みの車両 ID の一覧を読めないため、空として扱います")]
    public static partial void DeletedVehicleIdsInvalid(ILogger logger, Exception exception);

    /// <summary>キー値の同期の通信が失敗し、保留のまま次回に送り直すことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    [LoggerMessage(
        EventId = 9013,
        EventName = "sync.keyvalue.deferred",
        Level = LogLevel.Warning,
        Message = "設定の同期に失敗しました。保留のまま次回に送り直します")]
    public static partial void KeyValueDeferred(ILogger logger, Exception exception);

    /// <summary>接続セッションの転送データが壊れている（整合性エラー）ことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    /// <param name="payload">壊れていた種類（<c>metadata</c>・<c>package</c>）。</param>
    [LoggerMessage(
        EventId = 9020,
        EventName = "sync.transfer.payload.invalid",
        Level = LogLevel.Error,
        Message = "接続セッションの転送データを読めません（{Payload}）")]
    public static partial void TransferPayloadInvalid(ILogger logger, Exception exception, string payload);

    /// <summary>CloudKit が使えない状態を、利用側のエラーへ置き換えたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="reason">理由（<c>unavailable</c>・<c>authentication-required</c>）。</param>
    [LoggerMessage(
        EventId = 9021,
        EventName = "sync.unavailable.translated",
        Level = LogLevel.Debug,
        Message = "CloudKit が使えないため、利用側のエラーに置き換えます（{Reason}）")]
    public static partial void UnavailableTranslated(ILogger logger, string reason);
}
