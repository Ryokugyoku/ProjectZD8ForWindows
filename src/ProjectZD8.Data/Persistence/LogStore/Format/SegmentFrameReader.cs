namespace ProjectZD8.Data.Persistence.LogStore.Format;

/// <summary>枠付きレコードの読取結果です。</summary>
internal enum SegmentFrameReadStatus
{
    /// <summary>1件の正しいレコードを読みました。</summary>
    Frame,

    /// <summary>読取範囲の終わりに達しました。</summary>
    End,

    /// <summary>長さかCRCが不正なレコード（書きかけの末尾を含む）に達しました。</summary>
    Corrupt,
}

/// <summary>セグメントの論理バイト列から枠付きレコードを順に読みます。</summary>
/// <remarks>64KB単位でまとめて読み、本体をコピーせずにバッファー上の範囲として返します。</remarks>
internal sealed class SegmentFrameReader : IDisposable
{
    /// <summary>読取元のストリームです。</summary>
    private readonly Stream _stream;
    /// <summary>読取を止める論理位置（この位置を超える枠は読みません）です。</summary>
    private readonly long _end;
    /// <summary>読込バッファーです。</summary>
    private byte[] _buffer = new byte[64 * 1024];
    /// <summary>バッファー先頭の論理位置です。</summary>
    private long _bufferStart;
    /// <summary>バッファー内の有効バイト数です。</summary>
    private int _bufferLength;
    /// <summary>次に読む枠のバッファー内位置です。</summary>
    private int _cursor;

    /// <summary>読取元と範囲を指定して生成します。</summary>
    /// <remarks>責務: 指定論理位置から指定位置までを読む状態を作ります。</remarks>
    /// <param name="stream">シーク可能な論理バイト列。破棄はこのクラスが行います。</param>
    /// <param name="start">最初の枠の論理位置。</param>
    /// <param name="end">読取を止める論理位置。</param>
    internal SegmentFrameReader(Stream stream, long start, long end)
    {
        _stream = stream;
        _end = Math.Min(end, stream.Length);
        _stream.Position = start;
        _bufferStart = start;
    }

    /// <summary>次の枠の論理位置です。</summary>
    internal long NextOffset => _bufferStart + _cursor;

    /// <summary>次の枠を読みます。</summary>
    /// <remarks>責務: 1件の枠を長さとCRCで検証し、本体をバッファー上の範囲として返します。</remarks>
    /// <param name="frameOffset">読んだ枠の論理位置。</param>
    /// <param name="payload">枠を除いた本体（次の呼出しまで有効）。</param>
    /// <returns>読取結果。</returns>
    internal SegmentFrameReadStatus TryReadNext(out long frameOffset, out ReadOnlySpan<byte> payload)
    {
        frameOffset = NextOffset;
        payload = default;
        if (frameOffset >= _end)
        {
            return SegmentFrameReadStatus.End;
        }
        if (_end - frameOffset < SessionLogRecordCodec.FrameHeaderSize || !Ensure(SessionLogRecordCodec.FrameHeaderSize))
        {
            return SegmentFrameReadStatus.Corrupt;
        }
        if (!SessionLogRecordCodec.TryReadFrameHeader(_buffer.AsSpan(_cursor, SessionLogRecordCodec.FrameHeaderSize), out var length, out var checksum))
        {
            return SegmentFrameReadStatus.Corrupt;
        }
        var total = SessionLogRecordCodec.FrameHeaderSize + length;
        if (_end - frameOffset < total || !Ensure(total))
        {
            return SegmentFrameReadStatus.Corrupt;
        }
        var body = _buffer.AsSpan(_cursor + SessionLogRecordCodec.FrameHeaderSize, length);
        if (Crc32.Compute(body) != checksum)
        {
            return SegmentFrameReadStatus.Corrupt;
        }
        _cursor += total;
        payload = body;
        return SegmentFrameReadStatus.Frame;
    }

    /// <summary>バッファーの読取位置から指定バイト数を用意します。</summary>
    /// <remarks>責務: 足りない分をストリームから読み足し、必要ならバッファーを広げます。</remarks>
    /// <param name="count">必要なバイト数。</param>
    /// <returns>用意できた場合は <see langword="true"/>。</returns>
    private bool Ensure(int count)
    {
        if (_bufferLength - _cursor >= count)
        {
            return true;
        }
        var remaining = _bufferLength - _cursor;
        if (count > _buffer.Length)
        {
            var larger = new byte[Math.Max(count, _buffer.Length * 2)];
            _buffer.AsSpan(_cursor, remaining).CopyTo(larger);
            _buffer = larger;
        }
        else
        {
            _buffer.AsSpan(_cursor, remaining).CopyTo(_buffer);
        }
        _bufferStart += _cursor;
        _cursor = 0;
        _bufferLength = remaining;
        while (_bufferLength < count)
        {
            var read = _stream.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength);
            if (read == 0)
            {
                return false;
            }
            _bufferLength += read;
        }
        return true;
    }

    /// <summary>読取元を閉じます。</summary>
    /// <remarks>責務: ストリームを解放します。</remarks>
    public void Dispose() => _stream.Dispose();
}
