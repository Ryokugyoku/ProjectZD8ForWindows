using System.Net;
using System.Net.Http.Headers;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// 送信の進み具合を知らせるバイト列の HTTP 本文です（資産のアップロード用）。
/// </summary>
internal sealed class CloudKitProgressContent : HttpContent
{
    /// <summary>1 回に書くバイト数です。</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>送るバイト列です。</summary>
    private readonly byte[] _data;

    /// <summary>進み具合（0〜1）を受け取る処理です。</summary>
    private readonly Action<double>? _progress;

    /// <summary>
    /// 送るバイト列と進み具合の通知先から作ります。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を octet-stream の本文として固定します。</remarks>
    /// <param name="data">送るバイト列。</param>
    /// <param name="progress">進み具合の通知先。</param>
    public CloudKitProgressContent(byte[] data, Action<double>? progress)
    {
        _data = data;
        _progress = progress;
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }

    /// <summary>
    /// 本文を少しずつ書き、そのたびに進み具合を知らせます。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列をストリームへ書き、書いた割合を通知します。</remarks>
    /// <param name="stream">書き込み先。</param>
    /// <param name="context">転送の情報。</param>
    /// <returns>書き終わる処理。</returns>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var written = 0;
        while (written < _data.Length)
        {
            var count = Math.Min(ChunkSize, _data.Length - written);
            await stream.WriteAsync(_data.AsMemory(written, count)).ConfigureAwait(false);
            written += count;
            _progress?.Invoke(_data.Length == 0 ? 1 : (double)written / _data.Length);
        }
    }

    /// <summary>
    /// 本文の長さを返します。
    /// </summary>
    /// <remarks>責務: Content-Length を付けられるように長さを知らせます。</remarks>
    /// <param name="length">本文の長さ。</param>
    /// <returns>常に <c>true</c>。</returns>
    protected override bool TryComputeLength(out long length)
    {
        length = _data.Length;
        return true;
    }
}
