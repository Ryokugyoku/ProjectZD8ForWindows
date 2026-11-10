namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services を使えるかどうかです。
/// </summary>
public enum CloudKitAvailability
{
    /// <summary>設定（API トークンなど）がない。ローカルモード。</summary>
    NotConfigured,

    /// <summary>設定はあるが、CloudKit にサインインしていない（ckWebAuthToken がない）。</summary>
    SignedOut,

    /// <summary>使える。</summary>
    Available,
}
