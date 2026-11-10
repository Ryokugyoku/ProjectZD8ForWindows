using System.Security.Cryptography;
using System.Text;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// Apple の識別子を平文で保存しないための SHA-256 フィンガープリントです（iOS 版と同じ計算）。
/// </summary>
public static class CloudKitAccountFingerprint
{
    /// <summary>
    /// 文字列の UTF-8 の SHA-256 を小文字 16 進で返します。
    /// </summary>
    /// <remarks>責務: 1 件の識別子を、iOS の <c>SHA256.hash(data: Data(id.utf8))</c> と同じ 64 文字へ変換します。</remarks>
    /// <param name="identifier">Apple のユーザー識別子など。</param>
    /// <returns>小文字 16 進の SHA-256。</returns>
    public static string Of(string identifier) => Sha256Hex(Encoding.UTF8.GetBytes(identifier));

    /// <summary>
    /// バイト列の SHA-256 を小文字 16 進で返します。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を、転送の整合性に使う安定した要約値へ変換します。</remarks>
    /// <param name="data">バイト列。</param>
    /// <returns>小文字 16 進の SHA-256。</returns>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
