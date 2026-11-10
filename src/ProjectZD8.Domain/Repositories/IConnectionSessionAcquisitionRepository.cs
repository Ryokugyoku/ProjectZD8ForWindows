using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>sessionのimmutable manifestとappend-only Raw境界を整合して保存する能力です。</summary>
public interface IConnectionSessionAcquisitionRepository
{
    /// <summary>未登録sessionへmanifestとRaw開始境界を1回の原子操作で保存します。</summary>
    /// <remarks>責務: immutable manifestと最初のRaw要求前の開始境界を部分成功なしで新規関連付けします。</remarks>
    /// <param name="manifest">保存後に更新しない取得manifest。</param>
    /// <param name="startedAt">最初のRaw要求を許可する直前の開始境界時刻。</param>
    /// <param name="sessionID">semantic identityへ含めず両証拠の所有関係だけを結ぶsession識別子。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">同じ組の重複、異なる組との競合、または保存先失敗を区別するエラー。</exception>
    void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID);

    /// <summary>開始済みsessionへRaw終了境界を追記します。</summary>
    /// <remarks>責務: 保存済みmanifestや開始境界を更新せず1件の終了境界だけを追記します。</remarks>
    /// <param name="endedAt">Raw取得が停止した境界時刻。</param>
    /// <param name="reason">Raw取得が停止した直接原因。</param>
    /// <param name="sessionID">終了境界を所有するsession識別子。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">重複、競合、開始欠落、時系列違反、または保存先失敗を区別するエラー。</exception>
    void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID);

    /// <summary>sessionに関連付けられた取得manifestを読み取ります。</summary>
    /// <remarks>責務: sessionに原子的に保存されたimmutable manifestを変更せず返します。</remarks>
    /// <param name="sessionID">読取対象のsession識別子。</param>
    /// <returns>保存済みのimmutable取得manifest。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">未登録または読取先失敗を区別するエラー。</exception>
    ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID);

    /// <summary>sessionへ保存済みのRaw取得境界を追記順で読み取ります。</summary>
    /// <remarks>責務: 原子的に保存された開始と後から追記された終了を変更せず返します。</remarks>
    /// <param name="sessionID">読取対象のsession識別子。</param>
    /// <returns>開始から終了の順に並ぶ保存済み境界event。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">読取先を利用できない場合（<c>Unavailable</c>）。</exception>
    IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID);
}