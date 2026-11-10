namespace ProjectZD8.Domain.Entities;

/// <summary>Raw Payloadを含めず端末間で常時共有する接続セッション概要です。</summary>
public sealed record ConnectionSessionCloudMetadata
{
    /// <summary>端末固有の保管状態を除いた接続セッションです。</summary>
    public ConnectionSession Session { get; }
    /// <summary>CloudKit上のRaw Payloadを識別するSHA-256です。</summary>
    public string ManifestDigest { get; }

    /// <summary>同期対象セッションとRaw Manifestを固定して生成します。</summary>
    /// <remarks>責務: 1件の接続セッションをRaw Payload非依存の同期概要へ変換します。</remarks>
    /// <param name="session">一覧表示へ共有する接続セッション。</param>
    /// <param name="manifestDigest">CloudKit上のRaw Payloadを識別するSHA-256。</param>
    public ConnectionSessionCloudMetadata(ConnectionSession session, string manifestDigest)
    {
        Session = session;
        ManifestDigest = manifestDigest;
    }
}
