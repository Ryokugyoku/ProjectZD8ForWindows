using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Analysis.UseCases;

/// <summary>セッション解析前にRawログをローカルで利用可能にします。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です（Swift 版の <c>@MainActor</c>）。</remarks>
public sealed class PrepareConnectionSessionRawLogUseCase
{
    /// <summary>解析対象セッションの最新ローカル状態を取得する保存先です。</summary>
    private readonly IConnectionSessionRepository _sessionRepository;
    /// <summary>Rawログのローカル保存先です。</summary>
    private readonly IConnectionSessionRawLogRepository _rawLogRepository;
    /// <summary>CloudKitのオンデマンド転送境界です。</summary>
    private readonly IConnectionSessionTransferRepository _transferRepository;
    /// <summary>最終閲覧日時を生成するクロックです。</summary>
    private readonly Func<DateTimeOffset> _now;

    /// <summary>セッション保存先、Raw保存先、CloudKit境界、クロックを固定して生成します。</summary>
    /// <remarks>責務: 解析対象セッションをRawログ利用可能状態へする依存関係を固定します。</remarks>
    /// <param name="sessionRepository">解析対象セッションの最新ローカル状態を取得する保存先。</param>
    /// <param name="rawLogRepository">Rawログのローカル保存先。</param>
    /// <param name="transferRepository">CloudKitのオンデマンド転送境界。</param>
    /// <param name="now">最終閲覧日時を生成するクロック。<see langword="null"/> なら現在時刻を返します。</param>
    public PrepareConnectionSessionRawLogUseCase(
        IConnectionSessionRepository sessionRepository,
        IConnectionSessionRawLogRepository rawLogRepository,
        IConnectionSessionTransferRepository transferRepository,
        Func<DateTimeOffset>? now = null)
    {
        _sessionRepository = sessionRepository;
        _rawLogRepository = rawLogRepository;
        _transferRepository = transferRepository;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>指定セッションのRawログを必要時だけCloudKitから復元します。</summary>
    /// <remarks>責務: 1件の解析対象セッションをオンデマンド取得と最終閲覧記録へ変換します。</remarks>
    /// <param name="session">解析対象の接続セッション概要。</param>
    /// <param name="downloadProgress">CloudKit取得時に <c>0.0...1.0</c> で通知する進捗。<see langword="null"/> なら通知しません。</param>
    /// <returns>準備の完了を表すタスク。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">セッションが見つからない場合（<c>InvalidState</c>）、または転送の所有者が一致しない場合（<c>IntegrityConflict</c>）。</exception>
    /// <exception cref="Exception">CloudKit取得、整合性検証、ローカル復元、または閲覧日時保存に失敗した場合のエラー。</exception>
    public async Task Execute(ConnectionSession session, Action<double>? downloadProgress = null)
    {
        var progress = downloadProgress ?? (_ => { });
        var currentSession = _sessionRepository
            .Sessions(session.AccountIdentifier)
            .FirstOrDefault(candidate => candidate.Id == session.Id)
            ?? throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        if (currentSession.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            var transfer = await _transferRepository.DownloadTransfer(
                currentSession.Id,
                currentSession.AccountIdentifier,
                progress);
            if (transfer.Package.SessionID != currentSession.Id
                || !string.Equals(transfer.Package.AccountIdentifier, currentSession.AccountIdentifier, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            _rawLogRepository.RestoreVerifiedTransfer(transfer);
        }
        _rawLogRepository.MarkRawLogAccessed(_now(), currentSession.Id);
    }
}
