using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Persistence.Sqlite;

/// <summary>
/// SQLite の保存先が書く診断ログです（分類 <c>store.sqlite</c>、ID は 6100 番台。6001 は組み立てが使用済み）。
/// </summary>
/// <remarks>ファイルのパスは <see cref="Redact.Path(string?, string?)"/> を通します。SQL の引数や保存した値は書きません。</remarks>
internal static partial class SqliteStoreLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.StoreSqlite);

    /// <summary>DB のファイルを開いたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="file">DB のファイル（伏せたパス）。</param>
    [LoggerMessage(
        EventId = 6101,
        EventName = "store.sqlite.database.opened",
        Level = LogLevel.Debug,
        Message = "SQLite を開きました: {File}")]
    public static partial void DatabaseOpened(ILogger logger, string file);

    /// <summary>DB のファイルを開けなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">開けなかった理由。</param>
    /// <param name="file">DB のファイル（伏せたパス）。</param>
    [LoggerMessage(
        EventId = 6102,
        EventName = "store.sqlite.database.open.failed",
        Level = LogLevel.Error,
        Message = "SQLite を開けませんでした: {File}")]
    public static partial void DatabaseOpenFailed(ILogger logger, Exception exception, string file);

    /// <summary>スキーマの移行を 1 件適用したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="migration">移行の識別子。</param>
    [LoggerMessage(
        EventId = 6103,
        EventName = "store.sqlite.migration.applied",
        Level = LogLevel.Debug,
        Message = "スキーマを移行しました: {Migration}")]
    public static partial void MigrationApplied(ILogger logger, string migration);

    /// <summary>スキーマの移行に失敗したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="migration">失敗した移行の識別子（移行の記録表の作成なら <c>(table)</c>）。</param>
    [LoggerMessage(
        EventId = 6104,
        EventName = "store.sqlite.migration.failed",
        Level = LogLevel.Error,
        Message = "スキーマの移行に失敗しました: {Migration}")]
    public static partial void MigrationFailed(ILogger logger, Exception exception, string migration);

    /// <summary>リポジトリーの操作が保存先の失敗で終わったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="repository">リポジトリー名。</param>
    /// <param name="operation">操作の名前。</param>
    [LoggerMessage(
        EventId = 6105,
        EventName = "store.sqlite.operation.failed",
        Level = LogLevel.Error,
        Message = "保存先の操作に失敗しました（{Repository}、{Operation}）")]
    public static partial void OperationFailed(ILogger logger, Exception exception, string repository, string operation);
}
