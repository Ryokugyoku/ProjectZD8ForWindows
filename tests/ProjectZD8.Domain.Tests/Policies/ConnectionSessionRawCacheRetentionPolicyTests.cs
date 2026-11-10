using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// CloudKit保存済みRawログの3日保持判断を検証します。
/// </summary>
public sealed class ConnectionSessionRawCacheRetentionPolicyTests
{
    /// <summary>
    /// 最終閲覧から3日未満のRawログは端末へ保持します。
    /// </summary>
    [Fact]
    public void TestKeepsRawBeforeThreeDaysFromLastAccess()
    {
        var session = MakeSession(lastAccessedAt: DateTimeOffset.FromUnixTimeSeconds(100));

        Assert.False(
            new ConnectionSessionRawCacheRetentionPolicy().ShouldEvict(
                session,
                DateTimeOffset.FromUnixTimeSeconds(100 + 3 * 24 * 60 * 60 - 1)));
    }

    /// <summary>
    /// 最終閲覧から3日経過したRawログは端末退避対象にします。
    /// </summary>
    [Fact]
    public void TestEvictsRawAtThreeDaysFromLastAccess()
    {
        var session = MakeSession(lastAccessedAt: DateTimeOffset.FromUnixTimeSeconds(100));

        Assert.True(
            new ConnectionSessionRawCacheRetentionPolicy().ShouldEvict(
                session,
                DateTimeOffset.FromUnixTimeSeconds(100 + 3 * 24 * 60 * 60)));
    }

    /// <summary>
    /// CloudKit保存が未確認のRawログは期限を過ぎても保持します。
    /// </summary>
    [Fact]
    public void TestKeepsRawWithoutVerifiedCloudManifest()
    {
        var session = MakeSession(lastAccessedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        session = session with
        {
            RawLogSummary = session.RawLogSummary with { CloudState = ConnectionSessionCloudSyncState.Failed },
        };

        Assert.False(
            new ConnectionSessionRawCacheRetentionPolicy().ShouldEvict(
                session,
                DateTimeOffset.FromUnixTimeSeconds(1_000_000)));
    }

    /// <summary>
    /// 指定最終閲覧日時を持つCloudKit保存済みセッションを生成します。
    /// </summary>
    /// <param name="lastAccessedAt">現在端末でRawを最後に閲覧した日時。</param>
    /// <returns>CloudKit保存済みRawを持つ終了セッション。</returns>
    private static ConnectionSession MakeSession(DateTimeOffset? lastAccessedAt) =>
        new ConnectionSession(accountIdentifier: "account", startedAt: DateTimeOffset.FromUnixTimeSeconds(50))
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(60),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 2,
                localState: ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: null,
                lastAccessedAt: lastAccessedAt),
        };
}
