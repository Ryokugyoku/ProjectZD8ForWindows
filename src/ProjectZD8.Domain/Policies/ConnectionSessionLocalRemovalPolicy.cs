using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>iPhoneからセッションRawログを除去する前の判断結果です。</summary>
public enum ConnectionSessionLocalRemovalDecision
{
    /// <summary>CloudKit上のRaw Manifestを確認できるため標準確認で除去できます。</summary>
    Safe,
    /// <summary>CloudKit保管を確認できないためデータ消失警告が必要です。</summary>
    RequiresDataLossWarning,
    /// <summary>取得中またはRawログが存在しないため除去できません。</summary>
    Unavailable,
}

/// <summary>セッション状態とCloudKit保管証跡からiPhoneローカル除去可否を決定します。</summary>
public sealed class ConnectionSessionLocalRemovalPolicy
{
    /// <summary>セッションの現在状態からローカル除去判断を返します。</summary>
    /// <remarks>責務: 1件の接続セッションを安全、警告必須、利用不能のいずれかへ分類します。</remarks>
    /// <param name="session">除去対象として選ばれた接続セッション。</param>
    /// <returns>現在の端末内RawログとCloudKit保管証跡に対応する判断。</returns>
    public ConnectionSessionLocalRemovalDecision Decision(ConnectionSession session)
    {
        if (session.EndedAt is null
            || session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available
            || session.RawLogSummary.RecordCount <= 0)
        {
            return ConnectionSessionLocalRemovalDecision.Unavailable;
        }
        return session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded
            && !string.IsNullOrEmpty(session.RawLogSummary.ManifestDigest)
            ? ConnectionSessionLocalRemovalDecision.Safe
            : ConnectionSessionLocalRemovalDecision.RequiresDataLossWarning;
    }
}
