using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>CloudKitを介してセッションID単位で移動する未デコードログです。</summary>
public sealed record ConnectionSessionTransferPackage
{
    /// <summary>Rawログを所有する安定セッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>Rawログを所有するAppleアカウント識別子です。</summary>
    public string AccountIdentifier { get; }
    /// <summary>セッション順序を保持する全Raw応答です。</summary>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries { get; }

    /// <summary>セッション識別情報と全Raw応答を1件の転送単位として生成します。</summary>
    /// <remarks>責務: 1件の安定セッションIDを端末間で再結合できるRaw Payloadへまとめます。</remarks>
    /// <param name="sessionID">Rawログを所有する安定セッションID。</param>
    /// <param name="accountIdentifier">Rawログを所有するAppleアカウント識別子。</param>
    /// <param name="entries">セッション順に並ぶ未デコードRaw応答。</param>
    public ConnectionSessionTransferPackage(
        ConnectionSessionID sessionID,
        string accountIdentifier,
        IReadOnlyList<ConnectionSessionRawLogEntry> entries)
    {
        SessionID = sessionID;
        AccountIdentifier = accountIdentifier;
        Entries = entries.ToArray();
    }

    /// <summary>接続セッションから安定識別情報だけを取り出してRaw転送単位を生成します。</summary>
    /// <remarks>責務: 1件の接続セッションを表示メタデータ非包含のRaw Payloadへ変換します。</remarks>
    /// <param name="session">安定IDと所有アカウントを提供する接続セッション。</param>
    /// <param name="entries">セッション順に並ぶ未デコードRaw応答。</param>
    public ConnectionSessionTransferPackage(ConnectionSession session, IReadOnlyList<ConnectionSessionRawLogEntry> entries)
        : this(session.Id, session.AccountIdentifier, entries)
    {
    }

    /// <summary>Raw応答列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じくRaw応答列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(ConnectionSessionTransferPackage? other) =>
        other is not null
        && SessionID.Equals(other.SessionID)
        && string.Equals(AccountIdentifier, other.AccountIdentifier, StringComparison.Ordinal)
        && Entries.SequenceEqual(other.Entries);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: Raw応答列の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SessionID);
        hash.Add(AccountIdentifier, StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }
}

/// <summary>CloudKitから検証済みで取得したセッションPayloadです。</summary>
public sealed record VerifiedConnectionSessionTransfer
{
    /// <summary>復元するセッションPayloadです。</summary>
    public ConnectionSessionTransferPackage Package { get; }
    /// <summary>CloudKit AssetバイトのSHA-256です。</summary>
    public string ManifestDigest { get; }

    /// <summary>検証済みPayloadとDigestを生成します。</summary>
    /// <remarks>責務: 1件の転送Payloadを検証済みManifest識別子へ結び付けます。</remarks>
    /// <param name="package">復元するセッションPayload。</param>
    /// <param name="manifestDigest">AssetバイトのSHA-256。</param>
    public VerifiedConnectionSessionTransfer(ConnectionSessionTransferPackage package, string manifestDigest)
    {
        Package = package;
        ManifestDigest = manifestDigest;
    }
}
