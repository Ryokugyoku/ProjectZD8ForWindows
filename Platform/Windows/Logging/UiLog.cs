using System;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8ForWindows.Platform.Windows.Logging;

/// <summary>
/// 画面（<c>ui</c> 分類）の診断ログです。画面の移動と、利用者の主な操作（接続・切断・保存・削除・書き出し）を書きます。
/// </summary>
/// <remarks>
/// 入力の中身（車両の名前、整備記録の本文、ファイルのパスなど）は書きません。書くのは操作の種類と、数・種類の値だけです。
/// ID は 2000 番台（docs/LOGGING_STRATEGY.md の分類の順）。
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.UI)]
internal static partial class UiLog
{
    /// <summary>画面のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.UI);

    /// <summary>画面を移動したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="destination">行き先（<c>Home</c> など）。</param>
    [LoggerMessage(EventId = 2001, EventName = "ui.navigate.shown", Level = LogLevel.Debug, Message = "画面を開きました（{Destination}）")]
    public static partial void Navigated(ILogger logger, string destination);

    /// <summary>接続を要求したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">通信の種類（<c>BluetoothLowEnergy</c> など）。</param>
    [LoggerMessage(EventId = 2002, EventName = "ui.connect.requested", Level = LogLevel.Information, Message = "接続を要求しました（{Transport}）")]
    public static partial void ConnectRequested(ILogger logger, string transport);

    /// <summary>切断を要求したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2003, EventName = "ui.disconnect.requested", Level = LogLevel.Information, Message = "切断を要求しました")]
    public static partial void DisconnectRequested(ILogger logger);

    /// <summary>アダプターの設定を開いたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2004, EventName = "ui.adapter-setup.requested", Level = LogLevel.Debug, Message = "アダプターの設定を開きました")]
    public static partial void AdapterSetupRequested(ILogger logger);

    /// <summary>車両を保存したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2005, EventName = "ui.vehicle.save.requested", Level = LogLevel.Information, Message = "車両の保存を要求しました")]
    public static partial void VehicleSaveRequested(ILogger logger);

    /// <summary>車両の削除を確定したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2006, EventName = "ui.vehicle.delete.confirmed", Level = LogLevel.Information, Message = "車両の削除を確定しました")]
    public static partial void VehicleDeleteConfirmed(ILogger logger);

    /// <summary>整備記録を保存したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2007, EventName = "ui.maintenance.save.requested", Level = LogLevel.Information, Message = "整備記録の保存を要求しました")]
    public static partial void MaintenanceSaveRequested(ILogger logger);

    /// <summary>整備記録の削除を確定したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2008, EventName = "ui.maintenance.delete.confirmed", Level = LogLevel.Information, Message = "整備記録の削除を確定しました")]
    public static partial void MaintenanceDeleteConfirmed(ILogger logger);

    /// <summary>接続履歴のセッションのアップロードを要求したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2009, EventName = "ui.history.upload.requested", Level = LogLevel.Information, Message = "セッションのアップロードを要求しました")]
    public static partial void SessionUploadRequested(ILogger logger);

    /// <summary>接続履歴のセッションの削除を確定したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2010, EventName = "ui.history.delete.confirmed", Level = LogLevel.Information, Message = "セッションの削除を確定しました")]
    public static partial void SessionDeleteConfirmed(ILogger logger);

    /// <summary>端末内の生ログの削除を確定したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2011, EventName = "ui.history.raw-removal.confirmed", Level = LogLevel.Information, Message = "端末内の生ログの削除を確定しました")]
    public static partial void RawRemovalConfirmed(ILogger logger);

    /// <summary>アカウントの削除を確定したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2012, EventName = "ui.account.delete.confirmed", Level = LogLevel.Information, Message = "アカウントの削除を確定しました")]
    public static partial void AccountDeleteConfirmed(ILogger logger);

    /// <summary>不具合レポートを書き出したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="logFileCount">入れた診断ログの数。</param>
    /// <param name="errorCount">概要に載せた Error の数。</param>
    /// <param name="zipBytes">zip の大きさ。</param>
    [LoggerMessage(
        EventId = 2020,
        EventName = "ui.report.exported",
        Level = LogLevel.Information,
        Message = "不具合レポートを書き出しました（ログ {LogFileCount} 件、Error {ErrorCount} 件、{ZipBytes} バイト）")]
    public static partial void ReportExported(ILogger logger, int logFileCount, int errorCount, long zipBytes);

    /// <summary>不具合レポートの書き出しをやめたことを書きます（保存先を選ばなかった）。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2021, EventName = "ui.report.export.cancelled", Level = LogLevel.Debug, Message = "不具合レポートの書き出しをやめました")]
    public static partial void ReportExportCancelled(ILogger logger);

    /// <summary>不具合レポートを書き出せなかったことを書きます（利用者にエラーが見える）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">書き出せなかった理由。</param>
    [LoggerMessage(EventId = 2022, EventName = "ui.report.export.failed", Level = LogLevel.Error, Message = "不具合レポートを書き出せませんでした")]
    public static partial void ReportExportFailed(ILogger logger, Exception exception);

    /// <summary>診断ログを消したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="deletedFiles">消したファイルの数。</param>
    [LoggerMessage(EventId = 2023, EventName = "ui.logs.cleared", Level = LogLevel.Information, Message = "診断ログを消しました（{DeletedFiles} ファイル）")]
    public static partial void LogsCleared(ILogger logger, int deletedFiles);

    /// <summary>「詳細ログ」を切り替えたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="enabled">入れたなら true。</param>
    [LoggerMessage(EventId = 2024, EventName = "ui.verbose.changed", Level = LogLevel.Information, Message = "詳細ログを切り替えました（{Enabled}）")]
    public static partial void VerboseChanged(ILogger logger, bool enabled);

    /// <summary>前回の異常終了の知らせを出したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2025, EventName = "ui.crash-notice.shown", Level = LogLevel.Information, Message = "前回の異常終了の知らせを出しました")]
    public static partial void CrashNoticeShown(ILogger logger);

    /// <summary>前回の異常終了の知らせを閉じたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2026, EventName = "ui.crash-notice.dismissed", Level = LogLevel.Debug, Message = "前回の異常終了の知らせを閉じました")]
    public static partial void CrashNoticeDismissed(ILogger logger);

    /// <summary>報告用の番号をコピーしたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(EventId = 2027, EventName = "ui.report-number.copied", Level = LogLevel.Debug, Message = "報告用の番号をコピーしました")]
    public static partial void ReportNumberCopied(ILogger logger);
}
