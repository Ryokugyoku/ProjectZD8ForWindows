using System.Text.Json;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services のレコードのフィールドの値です（<c>{"value": ..., "type": "..."}</c>）。
/// </summary>
public abstract record CloudKitFieldValue
{
    /// <summary>
    /// 派生型だけが作れるようにします。
    /// </summary>
    /// <remarks>責務: 値の種類を下の派生型に限定します。</remarks>
    private protected CloudKitFieldValue()
    {
    }

    /// <summary>STRING の値です。</summary>
    /// <param name="Value">文字列。</param>
    public sealed record StringValue(string Value) : CloudKitFieldValue;

    /// <summary>INT64 の値です。</summary>
    /// <param name="Value">整数。</param>
    public sealed record Int64Value(long Value) : CloudKitFieldValue;

    /// <summary>DOUBLE の値です。</summary>
    /// <param name="Value">倍精度の数値。</param>
    public sealed record DoubleValue(double Value) : CloudKitFieldValue;

    /// <summary>TIMESTAMP の値です（1970 年からのミリ秒。iOS の <c>Date</c> フィールドと同じ）。</summary>
    /// <param name="Value">日時。</param>
    public sealed record TimestampValue(DateTimeOffset Value) : CloudKitFieldValue;

    /// <summary>BYTES の値です（JSON では base64）。</summary>
    public sealed record BytesValue : CloudKitFieldValue
    {
        /// <summary>
        /// バイト列から作ります。
        /// </summary>
        /// <remarks>責務: 渡されたバイト列を複製して保持します。</remarks>
        /// <param name="value">バイト列。</param>
        public BytesValue(byte[] value)
        {
            Value = value.ToArray();
        }

        /// <summary>バイト列。</summary>
        public byte[] Value { get; }

        /// <summary>
        /// バイト列の内容で比べます。
        /// </summary>
        /// <remarks>責務: 2 件の値をバイト単位で比べます。</remarks>
        /// <param name="other">比べる相手。</param>
        /// <returns>同じ内容なら <c>true</c>。</returns>
        public bool Equals(BytesValue? other) => other is not null && Value.AsSpan().SequenceEqual(other.Value);

        /// <summary>
        /// バイト列の内容からハッシュ値を作ります。
        /// </summary>
        /// <remarks>責務: <see cref="Equals(BytesValue?)"/> と矛盾しないハッシュ値を返します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.AddBytes(Value);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// 読み込んだレコードの ASSETID の値です（ダウンロード用の URL などを持ちます）。
    /// </summary>
    /// <param name="DownloadUrl">ダウンロード URL（<c>${f}</c> をファイル名に置き換えて使います）。ないこともあります。</param>
    /// <param name="FileChecksum">CloudKit のファイルのチェックサム。</param>
    /// <param name="Size">バイト数。</param>
    public sealed record AssetValue(string? DownloadUrl, string? FileChecksum, long? Size) : CloudKitFieldValue;

    /// <summary>
    /// 保存するレコードに入れる、アップロード済みの資産です（<c>assets/upload</c> の後のアップロードの応答の <c>singleFile</c>）。
    /// </summary>
    public sealed record AssetUploadValue : CloudKitFieldValue
    {
        /// <summary>
        /// アップロードの応答から作ります。
        /// </summary>
        /// <remarks>責務: <c>singleFile</c> の辞書を複製して保持します。</remarks>
        /// <param name="receipt">アップロードの応答の <c>singleFile</c>。</param>
        public AssetUploadValue(JsonElement receipt)
        {
            Receipt = receipt.Clone();
        }

        /// <summary>レコードの値にそのまま入れる辞書（wrappingKey、fileChecksum、receipt、referenceChecksum、size）。</summary>
        public JsonElement Receipt { get; }
    }

    /// <summary>
    /// この実装が扱わない型の値です（読むだけで、保存には使いません）。
    /// </summary>
    public sealed record UnknownValue : CloudKitFieldValue
    {
        /// <summary>
        /// 値と型の名前から作ります。
        /// </summary>
        /// <remarks>責務: 扱わない値を複製して保持します。</remarks>
        /// <param name="type">型の名前。</param>
        /// <param name="value">値。</param>
        public UnknownValue(string? type, JsonElement value)
        {
            Type = type;
            Value = value.Clone();
        }

        /// <summary>CloudKit の型の名前。</summary>
        public string? Type { get; }

        /// <summary>値。</summary>
        public JsonElement Value { get; }
    }
}
