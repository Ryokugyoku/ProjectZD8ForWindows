using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// iPhoneローカルRawログ除去前の安全判断を検証します。
/// </summary>
public sealed class ConnectionSessionLocalRemovalPolicyTests
{
    /// <summary>
    /// CloudKit保管済みの終了セッションはMac受領証なしでも安全に除去できます。
    /// </summary>
    [Fact]
    public void TestFinishedCloudStoredSessionWithoutMacReceiptIsSafe()
    {
        var session = MakeSession(receiptDigest: null);

        Assert.Equal(
            ConnectionSessionLocalRemovalDecision.Safe,
            new ConnectionSessionLocalRemovalPolicy().Decision(session));
    }

    /// <summary>
    /// 現在Manifestと一致するMac受領証があれば安全な除去として扱います。
    /// </summary>
    [Fact]
    public void TestMatchingMacReceiptAllowsSafeRemoval()
    {
        var session = MakeSession(receiptDigest: "manifest");

        Assert.Equal(
            ConnectionSessionLocalRemovalDecision.Safe,
            new ConnectionSessionLocalRemovalPolicy().Decision(session));
    }

    /// <summary>
    /// 古いMac受領証があっても現在のCloudKit Manifestを安全根拠にします。
    /// </summary>
    [Fact]
    public void TestMismatchedMacReceiptDoesNotOverrideCloudSafety()
    {
        var session = MakeSession(receiptDigest: "old-manifest");

        Assert.Equal(
            ConnectionSessionLocalRemovalDecision.Safe,
            new ConnectionSessionLocalRemovalPolicy().Decision(session));
    }

    /// <summary>
    /// CloudKit保管を確認できない終了済みRawログではデータ消失警告を要求します。
    /// </summary>
    [Fact]
    public void TestCloudUploadFailureRequiresWarning()
    {
        var session = MakeSession(receiptDigest: null);
        session = session with
        {
            RawLogSummary = session.RawLogSummary with
            {
                CloudState = ConnectionSessionCloudSyncState.Failed,
                ManifestDigest = null,
            },
        };

        Assert.Equal(
            ConnectionSessionLocalRemovalDecision.RequiresDataLossWarning,
            new ConnectionSessionLocalRemovalPolicy().Decision(session));
    }

    /// <summary>
    /// 取込受領証の条件を変えた終了済みセッションを生成します。
    /// </summary>
    /// <param name="receiptDigest">受領証へ設定するDigest。<c>null</c> の場合は受領証を設定しません。</param>
    /// <returns>除去判断が可能な完了済みセッション。</returns>
    private static ConnectionSession MakeSession(string? receiptDigest) =>
        new ConnectionSession(accountIdentifier: "account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100))
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(101),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 2,
                localState: ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: receiptDigest is null
                    ? null
                    : new ConnectionSessionMacImportReceipt(
                        deviceID: "mac",
                        deviceName: "Garage Mac",
                        importedAt: DateTimeOffset.FromUnixTimeSeconds(102),
                        manifestDigest: receiptDigest)),
        };
}
