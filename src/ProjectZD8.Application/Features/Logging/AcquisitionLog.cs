using System.Globalization;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging;

/// <summary>
/// 取得（<c>acq</c>）の診断ログです。取得の開始・停止、バッチの保存、落ちたバッチ、周期の遅れ、接続セッションの開始と終了を書きます。
/// </summary>
/// <remarks>
/// <para>
/// 取得のホットパス（PID 1 件ごと）には書きません。バッチ単位の要約（Debug）と、失敗・遅れだけを書きます。
/// 接続セッションの間に書くものは <see cref="DiagnosticLog.BeginConnectionSession"/> で <c>sid</c> を付けます
/// （<c>sid</c> は生ログのフォルダー名と同じ形。<see cref="SessionId"/>）。
/// </para>
/// <para>ID は 5001〜5099。</para>
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.Acquisition)]
internal static partial class AcquisitionLog
{
    /// <summary><c>acq</c> のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.Acquisition);

    /// <summary>
    /// 接続セッション ID を、診断ログの <c>sid</c> の形（生ログのフォルダー名と同じ）にします。
    /// </summary>
    /// <remarks>責務: セッション ID を小文字の 8-4-4-4-12 形式の文字列へ変換します。</remarks>
    /// <param name="sessionID">接続セッション ID。</param>
    /// <returns><c>sid</c>。</returns>
    public static string SessionId(ConnectionSessionID sessionID) =>
        sessionID.RawValue.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>取得を始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">終端の物理方式。</param>
    /// <param name="generation">取得の世代。</param>
    /// <param name="persisted">方式 B（保存つき）の取得か。</param>
    [LoggerMessage(
        EventId = 5001,
        EventName = "acq.started",
        Level = LogLevel.Information,
        Message = "取得を始めます（{Transport}、世代 {Generation}、保存つき {Persisted}）")]
    public static partial void Started(ILogger logger, string transport, ulong generation, bool persisted);

    /// <summary>取得を止めたことを理由つきで書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="reason">理由（<c>user</c> / <c>loop-ended</c> / <c>superseded</c> / <c>cancelled</c>）。</param>
    /// <param name="generation">止めた取得の世代。</param>
    [LoggerMessage(
        EventId = 5002,
        EventName = "acq.stopped",
        Level = LogLevel.Information,
        Message = "取得を止めました（{Reason}、世代 {Generation}）")]
    public static partial void Stopped(ILogger logger, string reason, ulong generation);

    /// <summary>取得が失敗し、画面にエラーを出すことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の例外。</param>
    /// <param name="reason">画面のエラーの種類（表示文言のキー）。</param>
    /// <param name="generation">取得の世代。</param>
    [LoggerMessage(
        EventId = 5003,
        EventName = "acq.failed",
        Level = LogLevel.Error,
        Message = "取得が失敗しました（{Reason}、世代 {Generation}）")]
    public static partial void Failed(ILogger logger, Exception exception, string reason, ulong generation);

    /// <summary>車両の無応答・接続の喪失で取得が終わったことを書きます（画面には切断として出る）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">終わった原因の例外。</param>
    /// <param name="reason">理由（<c>vehicle-no-response</c> / <c>connection-lost</c>）。</param>
    /// <param name="generation">取得の世代。</param>
    [LoggerMessage(
        EventId = 5004,
        EventName = "acq.disconnected",
        Level = LogLevel.Warning,
        Message = "取得が {Reason} で終わりました（世代 {Generation}）")]
    public static partial void Disconnected(ILogger logger, Exception exception, string reason, ulong generation);

    /// <summary>取得する PID 定義を決めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="definitions">取得する定義の件数。</param>
    /// <param name="capabilities">対応を確かめた PID の件数。</param>
    /// <param name="elapsedMs">対応 PID の読込（初回は探索）にかかった時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 5005,
        EventName = "acq.capabilities.loaded",
        Level = LogLevel.Debug,
        Message = "取得する PID を決めました（定義 {Definitions} 件、対応 {Capabilities} 件、{ElapsedMs} ms）")]
    public static partial void CapabilitiesLoaded(ILogger logger, int definitions, int capabilities, long elapsedMs);

    /// <summary>取得の周期が予定より遅れて起きたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="tick">周期の番号。</param>
    /// <param name="delayMs">予定からの遅れ（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 5006,
        EventName = "acq.schedule.delayed",
        Level = LogLevel.Warning,
        Message = "取得の周期 {Tick} が予定より {DelayMs} ms 遅れました")]
    public static partial void ScheduleDelayed(ILogger logger, ulong tick, long delayMs);

    /// <summary>PID 定義の一覧を読めなかった元の例外を書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    [LoggerMessage(
        EventId = 5007,
        EventName = "acq.catalog.failed",
        Level = LogLevel.Warning,
        Message = "PID 定義の一覧を読めませんでした")]
    public static partial void CatalogFailed(ILogger logger, Exception exception);

    /// <summary>取得の証拠（マニフェストと開始の境界）を保存し、取得を許可したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="generation">取得の世代。</param>
    /// <param name="requests">要求する PID の件数。</param>
    /// <param name="elapsedMs">保存にかかった時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 5011,
        EventName = "acq.evidence.started",
        Level = LogLevel.Information,
        Message = "取得の証拠を保存しました（世代 {Generation}、要求 {Requests} 件、{ElapsedMs} ms）")]
    public static partial void EvidenceStarted(ILogger logger, ulong generation, int requests, long elapsedMs);

    /// <summary>接続セッションがないため取得を始められないことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="generation">取得の世代。</param>
    [LoggerMessage(
        EventId = 5012,
        EventName = "acq.evidence.no-session",
        Level = LogLevel.Warning,
        Message = "接続セッションがないため、取得（世代 {Generation}）を始められません")]
    public static partial void EvidenceNoSession(ILogger logger, ulong generation);

    /// <summary>バッチを保存せずに落としたことを書きます（世代が古い・取消）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="generation">バッチの世代。</param>
    /// <param name="batch">バッチの番号。</param>
    /// <param name="reason">理由（<c>stale-generation</c> / <c>inactive-generation</c>）。</param>
    [LoggerMessage(
        EventId = 5013,
        EventName = "acq.batch.dropped",
        Level = LogLevel.Warning,
        Message = "バッチ {Batch}（世代 {Generation}）を落としました（{Reason}）")]
    public static partial void BatchDropped(ILogger logger, ulong generation, long batch, string reason);

    /// <summary>バッチ 1 件の要約を書きます（PID 1 件ごとには書かない）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="batch">バッチの番号。</param>
    /// <param name="tick">周期の番号。</param>
    /// <param name="requests">要求した PID の件数。</param>
    /// <param name="responded">応答があった件数。</param>
    /// <param name="timedOut">期限切れの件数。</param>
    /// <param name="transportFailed">通信の失敗の件数。</param>
    /// <param name="unclassified">読めない応答の件数。</param>
    /// <param name="cancelled">取消の件数。</param>
    /// <param name="samples">数値にできた件数。</param>
    /// <param name="elapsedMs">バッチの所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 5014,
        EventName = "acq.batch.completed",
        Level = LogLevel.Debug,
        Message = "バッチ {Batch}（周期 {Tick}）：要求 {Requests}、応答 {Responded}、期限切れ {TimedOut}、通信失敗 {TransportFailed}、不明 {Unclassified}、取消 {Cancelled}、数値 {Samples}（{ElapsedMs} ms）")]
    public static partial void BatchCompleted(
        ILogger logger,
        long batch,
        ulong tick,
        int requests,
        int responded,
        int timedOut,
        int transportFailed,
        int unclassified,
        int cancelled,
        int samples,
        long elapsedMs);

    /// <summary>バッチの保存に失敗したことを、元の例外つきで書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    /// <param name="batch">バッチの番号。</param>
    /// <param name="generation">バッチの世代。</param>
    [LoggerMessage(
        EventId = 5015,
        EventName = "acq.batch.persist.failed",
        Level = LogLevel.Error,
        Message = "バッチ {Batch}（世代 {Generation}）を保存できませんでした")]
    public static partial void BatchPersistFailed(ILogger logger, Exception exception, long batch, ulong generation);

    /// <summary>保存済みのバッチを再開したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="batch">バッチの番号。</param>
    /// <param name="state">再開したときの状態（<c>completed</c> / <c>dispatch-begun</c>）。</param>
    [LoggerMessage(
        EventId = 5016,
        EventName = "acq.batch.resumed",
        Level = LogLevel.Warning,
        Message = "保存済みのバッチ {Batch} を再開しました（{State}）")]
    public static partial void BatchResumed(ILogger logger, long batch, string state);

    /// <summary>PID の要求で想定外の例外が起き、読めない応答として扱ったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="pid">Service と PID（<c>010C</c> など）。</param>
    [LoggerMessage(
        EventId = 5017,
        EventName = "acq.request.unexpected-error",
        Level = LogLevel.Warning,
        Message = "要求 {Pid} で想定外の例外が起きたため、読めない応答として扱います")]
    public static partial void RequestUnexpectedError(ILogger logger, Exception exception, string pid);

    /// <summary>応答を数値にできなかったことを書きます（想定内）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="pid">Service と PID。</param>
    [LoggerMessage(
        EventId = 5018,
        EventName = "acq.decode.failed",
        Level = LogLevel.Debug,
        Message = "{Pid} の応答を数値にできませんでした")]
    public static partial void DecodeFailed(ILogger logger, Exception exception, string pid);

    /// <summary>取得の世代を止めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="generation">止めた世代。</param>
    [LoggerMessage(
        EventId = 5019,
        EventName = "acq.generation.cancelled",
        Level = LogLevel.Debug,
        Message = "取得の世代 {Generation} を止めました")]
    public static partial void GenerationCancelled(ILogger logger, ulong generation);

    /// <summary>接続セッションを始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="superseded">前のセッションを置き換えたか。</param>
    [LoggerMessage(
        EventId = 5021,
        EventName = "acq.session.started",
        Level = LogLevel.Information,
        Message = "接続セッションを始めました（前のセッションを置き換え {Superseded}）")]
    public static partial void SessionStarted(ILogger logger, bool superseded);

    /// <summary>接続セッションを終えたことを理由つきで書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="reason">終了の理由。</param>
    /// <param name="hasVehicle">車両を関連付けていたか。</param>
    [LoggerMessage(
        EventId = 5022,
        EventName = "acq.session.ended",
        Level = LogLevel.Information,
        Message = "接続セッションを終えました（{Reason}、車両あり {HasVehicle}）")]
    public static partial void SessionEnded(ILogger logger, string reason, bool hasVehicle);

    /// <summary>接続セッションを保存できなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    /// <param name="operation">保存しようとした操作（<c>start</c> / <c>end</c>）。</param>
    [LoggerMessage(
        EventId = 5023,
        EventName = "acq.session.save.failed",
        Level = LogLevel.Error,
        Message = "接続セッションを保存できませんでした（{Operation}）")]
    public static partial void SessionSaveFailed(ILogger logger, Exception exception, string operation);

    /// <summary>前回の実行で終わっていなかった接続セッションを、異常終了として閉じたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="count">閉じた件数。</param>
    [LoggerMessage(
        EventId = 5024,
        EventName = "acq.session.recovered",
        Level = LogLevel.Warning,
        Message = "終わっていなかった接続セッション {Count} 件を、異常終了として閉じました")]
    public static partial void SessionRecovered(ILogger logger, int count);

    /// <summary>終わっていなかった接続セッションを閉じられなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    [LoggerMessage(
        EventId = 5025,
        EventName = "acq.session.recovery.failed",
        Level = LogLevel.Error,
        Message = "終わっていなかった接続セッションを閉じられませんでした")]
    public static partial void SessionRecoveryFailed(ILogger logger, Exception exception);

    /// <summary>接続セッションの更新（車両の関連付け・走行距離）を保存できなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    /// <param name="operation">操作（<c>bind-vehicle</c> / <c>distance</c>）。</param>
    [LoggerMessage(
        EventId = 5026,
        EventName = "acq.session.update.failed",
        Level = LogLevel.Warning,
        Message = "接続セッションの更新を保存できませんでした（{Operation}）")]
    public static partial void SessionUpdateFailed(ILogger logger, Exception exception, string operation);
}
