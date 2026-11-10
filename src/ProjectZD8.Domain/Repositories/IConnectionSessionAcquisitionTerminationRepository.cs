using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>接続sessionと取得証拠を部分成功なしで終了・回復する能力です。</summary>
public interface IConnectionSessionAcquisitionTerminationRepository
{
    /// <summary>現在session、open batch、取得終了境界を1回の永続操作で確定します。</summary>
    /// <remarks>責務: 1件の接続sessionを未確定取得証拠の回復を含む原子的な終了状態へ変換します。</remarks>
    /// <param name="session">終了直前の現在session。</param>
    /// <param name="endedAt">sessionと取得境界へ共通で使用する終了日時。</param>
    /// <param name="reason">接続と取得が停止した直接原因。</param>
    /// <returns>永続化後にcanonical readbackした終了済みsession。</returns>
    /// <exception cref="Exception">session不在、競合、取得開始欠落、または原子保存失敗。</exception>
    ConnectionSession FinishSessionAcquisition(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason);

    /// <summary>アカウントに残る未終了sessionとopen取得証拠を原子的に回復します。</summary>
    /// <remarks>責務: process終了後に残った各sessionを決定的な異常終了状態へ回復します。</remarks>
    /// <param name="accountIdentifier">回復対象sessionの所有アカウント。</param>
    /// <param name="recoveredAt">回復結果へ固定する終了日時。</param>
    /// <returns>今回回復した終了済みsession。</returns>
    /// <exception cref="Exception">保存値不正または原子回復失敗。</exception>
    IReadOnlyList<ConnectionSession> RecoverInterruptedSessionAcquisitions(string accountIdentifier, DateTimeOffset recoveredAt);
}