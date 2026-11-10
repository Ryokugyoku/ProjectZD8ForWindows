using System.Buffers.Binary;
using System.Text;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.LogStore.Format;

/// <summary>セッションログのレコードと枠付きバイト列を相互変換します。</summary>
/// <remarks>
/// 枠の形式は <c>[本体長 u32 LE][本体のCRC32 u32 LE][本体]</c> です。本体の先頭1バイトがレコード種別です。
/// 列挙値はDomainの宣言順の番号で保存し、読むときに範囲を検証します（宣言順を変えないこと）。
/// </remarks>
internal static class SessionLogRecordCodec
{
    /// <summary>枠ヘッダーのバイト数です。</summary>
    internal const int FrameHeaderSize = 8;

    /// <summary>1レコード本体の最大バイト数です。これを超える長さは破損とみなします。</summary>
    internal const int MaximumPayloadSize = 64 * 1024 * 1024;

    /// <summary>生ログの種別です。</summary>
    private const byte RawKind = 1;
    /// <summary>batch開始の種別です。</summary>
    private const byte BatchBegunKind = 2;
    /// <summary>dispatch開始の種別です。</summary>
    private const byte DispatchBegunKind = 3;
    /// <summary>Rawなしrequest終端の種別です。</summary>
    private const byte RequestTerminalKind = 4;
    /// <summary>正応答の種別です。</summary>
    private const byte RespondedKind = 5;
    /// <summary>batch終端の種別です。</summary>
    private const byte BatchSealedKind = 6;
    /// <summary>batch写しの種別です。</summary>
    private const byte BatchSnapshotKind = 7;

    /// <summary>レコードを枠付きバイト列へ符号化します。</summary>
    /// <remarks>責務: 1件のレコードを長さとCRC32付きの追記単位へ変換します。</remarks>
    /// <param name="record">符号化するレコード。</param>
    /// <returns>枠ヘッダーを含むバイト列。</returns>
    internal static byte[] EncodeFrame(SessionLogRecord record)
    {
        var writer = new ByteWriter(64);
        writer.Skip(FrameHeaderSize);
        switch (record)
        {
            case SessionLogRecord.Raw raw:
                writer.Byte(RawKind);
                WriteEntry(writer, raw.Entry);
                break;
            case SessionLogRecord.BatchBegun begun:
                writer.Byte(BatchBegunKind);
                writer.Int64(begun.Ordinal);
                writer.UInt64(begun.Generation);
                writer.UInt64(begun.PolicyTick);
                writer.Bool(begun.IsSelectionEvaluationComplete);
                writer.Int64(begun.StartedTicks);
                writer.Int32(begun.ManifestOrdinals.Length);
                foreach (var ordinal in begun.ManifestOrdinals)
                {
                    writer.Int32(ordinal);
                }
                break;
            case SessionLogRecord.DispatchBegun dispatch:
                writer.Byte(DispatchBegunKind);
                writer.Int64(dispatch.Ordinal);
                writer.Int32(dispatch.RequestOrdinal);
                break;
            case SessionLogRecord.RequestTerminal terminal:
                writer.Byte(RequestTerminalKind);
                writer.Int64(terminal.Ordinal);
                WriteRequest(writer, terminal.Request);
                break;
            case SessionLogRecord.Responded responded:
                writer.Byte(RespondedKind);
                WriteEntry(writer, responded.Entry);
                writer.Int64(responded.Ordinal);
                WriteRequest(writer, responded.Request);
                break;
            case SessionLogRecord.BatchSealed sealedBatch:
                writer.Byte(BatchSealedKind);
                writer.Int64(sealedBatch.Ordinal);
                writer.Byte((byte)sealedBatch.CompletionState);
                writer.Int64(sealedBatch.CompletedTicks);
                writer.Bool(sealedBatch.Failure is not null);
                writer.Byte(sealedBatch.Failure is { } failure ? (byte)failure : (byte)0);
                writer.Int32(sealedBatch.Updates.Length);
                foreach (var update in sealedBatch.Updates)
                {
                    WriteRequest(writer, update);
                }
                break;
            case SessionLogRecord.BatchSnapshot snapshot:
                writer.Byte(BatchSnapshotKind);
                WriteBatch(writer, snapshot.Batch);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(record), record, null);
        }
        var buffer = writer.ToArray();
        var payload = buffer.AsSpan(FrameHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), Crc32.Compute(payload));
        return buffer;
    }

    /// <summary>枠ヘッダーを読み、本体長とCRC32を返します。</summary>
    /// <remarks>責務: 8バイトの枠ヘッダーを長さ検証付きで解釈します。</remarks>
    /// <param name="header">枠ヘッダー8バイト。</param>
    /// <param name="length">本体長。</param>
    /// <param name="checksum">本体のCRC32。</param>
    /// <returns>長さが有効範囲なら <see langword="true"/>。</returns>
    internal static bool TryReadFrameHeader(ReadOnlySpan<byte> header, out int length, out uint checksum)
    {
        var rawLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        length = (int)Math.Min(rawLength, int.MaxValue);
        return rawLength is > 0 and <= MaximumPayloadSize;
    }

    /// <summary>本体がRawを含む種別なら生ログを復号します。</summary>
    /// <remarks>責務: 読取の高速経路として、Rawを含むレコードだけから生ログを取り出します。</remarks>
    /// <param name="payload">枠を除いた本体。</param>
    /// <param name="entry">復号した生ログ。</param>
    /// <returns>Rawを含む種別なら <see langword="true"/>。</returns>
    /// <exception cref="InvalidDataException">本体が壊れている場合。</exception>
    internal static bool TryDecodeEntry(ReadOnlySpan<byte> payload, out ConnectionSessionRawLogEntry entry)
    {
        if (payload.Length == 0 || (payload[0] != RawKind && payload[0] != RespondedKind))
        {
            entry = null!;
            return false;
        }
        var reader = new ByteReader(payload[1..]);
        entry = ReadEntry(ref reader);
        return true;
    }

    /// <summary>本体の生ログのsequenceと観測日時だけを読みます。</summary>
    /// <remarks>責務: 索引構築のため、Rawを含むレコードから並べ替えキーだけを取り出します。</remarks>
    /// <param name="payload">枠を除いた本体。</param>
    /// <param name="sequence">生ログのsequence。</param>
    /// <param name="observedTicks">観測日時のTicks。</param>
    /// <param name="payloadLength">応答データのバイト数。</param>
    /// <returns>Rawを含む種別なら <see langword="true"/>。</returns>
    internal static bool TryPeekEntry(ReadOnlySpan<byte> payload, out long sequence, out long observedTicks, out int payloadLength)
    {
        if (payload.Length < 1 + 8 + 8 + 8 + 1 + 1 + 4 || (payload[0] != RawKind && payload[0] != RespondedKind))
        {
            sequence = 0;
            observedTicks = 0;
            payloadLength = 0;
            return false;
        }
        sequence = BinaryPrimitives.ReadInt64LittleEndian(payload[1..]);
        observedTicks = BinaryPrimitives.ReadInt64LittleEndian(payload[9..]);
        payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[27..]);
        return true;
    }

    /// <summary>本体をレコードへ復号します。</summary>
    /// <remarks>責務: 1件の検証済み本体を種別ごとのレコードへ変換します。</remarks>
    /// <param name="payload">枠を除いた本体。</param>
    /// <returns>復号したレコード。</returns>
    /// <exception cref="InvalidDataException">種別や値が不正な場合。</exception>
    internal static SessionLogRecord Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            throw new InvalidDataException("Empty log record");
        }
        var reader = new ByteReader(payload[1..]);
        try
        {
            SessionLogRecord record = payload[0] switch
            {
                RawKind => new SessionLogRecord.Raw(ReadEntry(ref reader)),
                BatchBegunKind => ReadBatchBegun(ref reader),
                DispatchBegunKind => new SessionLogRecord.DispatchBegun(reader.Int64(), reader.Int32()),
                RequestTerminalKind => new SessionLogRecord.RequestTerminal(reader.Int64(), ReadRequest(ref reader)),
                RespondedKind => new SessionLogRecord.Responded(ReadEntry(ref reader), reader.Int64(), ReadRequest(ref reader)),
                BatchSealedKind => ReadBatchSealed(ref reader),
                BatchSnapshotKind => new SessionLogRecord.BatchSnapshot(ReadBatch(ref reader)),
                _ => throw new InvalidDataException("Unknown log record kind"),
            };
            if (!reader.IsAtEnd)
            {
                throw new InvalidDataException("Trailing bytes in log record");
            }
            return record;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new InvalidDataException("Invalid log record", error);
        }
    }

    /// <summary>生ログを書きます。</summary>
    /// <remarks>責務: 1件の生ログを固定長部と応答データへ書き出します。</remarks>
    /// <param name="writer">書込み先。</param>
    /// <param name="entry">書く生ログ。</param>
    private static void WriteEntry(ByteWriter writer, ConnectionSessionRawLogEntry entry)
    {
        writer.Int64(entry.Sequence);
        writer.Int64(entry.ObservedAt.UtcTicks);
        writer.UInt64(entry.BatchElapsedNanoseconds);
        writer.Byte(entry.Service);
        writer.Byte(entry.Pid);
        writer.Int32(entry.Payload.Count);
        foreach (var value in entry.Payload)
        {
            writer.Byte(value);
        }
    }

    /// <summary>生ログを読みます。</summary>
    /// <remarks>責務: 固定長部と応答データから1件の生ログを復元します。</remarks>
    /// <param name="reader">読取元。</param>
    /// <returns>生ログ。</returns>
    private static ConnectionSessionRawLogEntry ReadEntry(ref ByteReader reader)
    {
        var sequence = reader.Int64();
        var ticks = reader.Int64();
        var elapsed = reader.UInt64();
        var service = reader.Byte();
        var pid = reader.Byte();
        var length = reader.Int32();
        var bytes = reader.Bytes(length).ToArray();
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            throw new InvalidDataException("Raw log timestamp is out of range");
        }
        return new ConnectionSessionRawLogEntry(sequence, new DateTimeOffset(ticks, TimeSpan.Zero), elapsed, service, pid, bytes);
    }

    /// <summary>request証拠を書きます。</summary>
    /// <remarks>責務: 1件のrequest証拠を任意項目の有無付きで書き出します。</remarks>
    /// <param name="writer">書込み先。</param>
    /// <param name="request">書くrequest証拠。</param>
    private static void WriteRequest(ByteWriter writer, PIDRequestEvidence request)
    {
        writer.Int32(request.RequestOrdinal);
        writer.Int32(request.ManifestPIDOrdinal);
        writer.Byte((byte)request.DispatchState);
        writer.Bool(request.TransportOutcome is not null);
        writer.Byte(request.TransportOutcome is { } outcome ? (byte)outcome : (byte)0);
        writer.Byte((byte)request.ValueOutcome);
        writer.Bool(request.RawSequence is not null);
        writer.Int64(request.RawSequence ?? 0);
        writer.Bool(request.ElapsedNanoseconds is not null);
        writer.UInt64(request.ElapsedNanoseconds ?? 0);
        writer.String(request.ReasonCode);
    }

    /// <summary>request証拠を読みます。</summary>
    /// <remarks>責務: 書き出し形式からDomain不変条件を検証したrequest証拠を復元します。</remarks>
    /// <param name="reader">読取元。</param>
    /// <returns>request証拠。</returns>
    private static PIDRequestEvidence ReadRequest(ref ByteReader reader)
    {
        var requestOrdinal = reader.Int32();
        var manifestOrdinal = reader.Int32();
        var dispatch = ToEnum<PIDRequestDispatchState>(reader.Byte());
        var hasTransport = reader.Bool();
        var transport = reader.Byte();
        var value = ToEnum<PIDRequestValueOutcome>(reader.Byte());
        var hasRaw = reader.Bool();
        var raw = reader.Int64();
        var hasElapsed = reader.Bool();
        var elapsed = reader.UInt64();
        var reason = reader.String();
        return new PIDRequestEvidence(
            requestOrdinal,
            manifestOrdinal,
            dispatch,
            hasTransport ? ToEnum<PIDRequestTransportOutcome>(transport) : null,
            value,
            hasRaw ? raw : null,
            hasElapsed ? elapsed : null,
            reason);
    }

    /// <summary>batch証拠の全体を書きます。</summary>
    /// <remarks>責務: 1件のbatch証拠をrequest列とともに書き出します。</remarks>
    /// <param name="writer">書込み先。</param>
    /// <param name="batch">書くbatch証拠。</param>
    private static void WriteBatch(ByteWriter writer, AcquisitionBatchEvidence batch)
    {
        writer.Int64(batch.Identity.Ordinal);
        writer.UInt64(batch.Generation);
        writer.UInt64(batch.PolicyTick);
        writer.Bool(batch.IsSelectionEvaluationComplete);
        writer.Int64(batch.StartedAt.UtcTicks);
        writer.Bool(batch.CompletionState is not null);
        writer.Byte(batch.CompletionState is { } state ? (byte)state : (byte)0);
        writer.Bool(batch.CompletedAt is not null);
        writer.Int64(batch.CompletedAt?.UtcTicks ?? 0);
        writer.Bool(batch.Failure is not null);
        writer.Byte(batch.Failure is { } failure ? (byte)failure : (byte)0);
        writer.Int32(batch.Requests.Count);
        foreach (var request in batch.Requests)
        {
            WriteRequest(writer, request);
        }
    }

    /// <summary>batch証拠の全体を読みます。</summary>
    /// <remarks>責務: 書き出し形式からDomain不変条件を検証したbatch証拠を復元します。</remarks>
    /// <param name="reader">読取元。</param>
    /// <returns>batch証拠。</returns>
    private static AcquisitionBatchEvidence ReadBatch(ref ByteReader reader)
    {
        var ordinal = reader.Int64();
        var generation = reader.UInt64();
        var tick = reader.UInt64();
        var selection = reader.Bool();
        var started = reader.Int64();
        var hasState = reader.Bool();
        var state = reader.Byte();
        var hasCompleted = reader.Bool();
        var completed = reader.Int64();
        var hasFailure = reader.Bool();
        var failure = reader.Byte();
        var count = reader.Int32();
        if (count < 0 || count > 1_000_000)
        {
            throw new InvalidDataException("Invalid request count");
        }
        var requests = new PIDRequestEvidence[count];
        for (var index = 0; index < count; index++)
        {
            requests[index] = ReadRequest(ref reader);
        }
        return new AcquisitionBatchEvidence(
            new AcquisitionBatchIdentity(ordinal),
            generation,
            tick,
            selection,
            new DateTimeOffset(started, TimeSpan.Zero),
            hasState ? ToEnum<AcquisitionBatchCompletionState>(state) : null,
            hasCompleted ? new DateTimeOffset(completed, TimeSpan.Zero) : null,
            hasFailure ? ToEnum<AcquisitionBatchFailure>(failure) : null,
            requests);
    }

    /// <summary>batch開始を読みます。</summary>
    /// <remarks>責務: 書き出し形式から1件のbatch開始レコードを復元します。</remarks>
    /// <param name="reader">読取元。</param>
    /// <returns>batch開始レコード。</returns>
    private static SessionLogRecord.BatchBegun ReadBatchBegun(ref ByteReader reader)
    {
        var ordinal = reader.Int64();
        var generation = reader.UInt64();
        var tick = reader.UInt64();
        var selection = reader.Bool();
        var started = reader.Int64();
        var count = reader.Int32();
        if (count < 0 || count > 1_000_000)
        {
            throw new InvalidDataException("Invalid request count");
        }
        var ordinals = new int[count];
        for (var index = 0; index < count; index++)
        {
            ordinals[index] = reader.Int32();
        }
        return new SessionLogRecord.BatchBegun(ordinal, generation, tick, selection, started, ordinals);
    }

    /// <summary>batch終端を読みます。</summary>
    /// <remarks>責務: 書き出し形式から1件のbatch終端レコードを復元します。</remarks>
    /// <param name="reader">読取元。</param>
    /// <returns>batch終端レコード。</returns>
    private static SessionLogRecord.BatchSealed ReadBatchSealed(ref ByteReader reader)
    {
        var ordinal = reader.Int64();
        var state = ToEnum<AcquisitionBatchCompletionState>(reader.Byte());
        var completed = reader.Int64();
        var hasFailure = reader.Bool();
        var failure = reader.Byte();
        var count = reader.Int32();
        if (count < 0 || count > 1_000_000)
        {
            throw new InvalidDataException("Invalid update count");
        }
        var updates = new PIDRequestEvidence[count];
        for (var index = 0; index < count; index++)
        {
            updates[index] = ReadRequest(ref reader);
        }
        return new SessionLogRecord.BatchSealed(
            ordinal,
            state,
            completed,
            hasFailure ? ToEnum<AcquisitionBatchFailure>(failure) : null,
            updates);
    }

    /// <summary>番号を列挙値へ戻します。</summary>
    /// <remarks>責務: 保存済み番号が宣言済みの列挙値であることを検証します。</remarks>
    /// <typeparam name="T">列挙型。</typeparam>
    /// <param name="value">保存済み番号。</param>
    /// <returns>列挙値。</returns>
    /// <exception cref="InvalidDataException">未定義の番号の場合。</exception>
    private static T ToEnum<T>(byte value) where T : struct, System.Enum
    {
        var candidate = (T)(object)(int)value;
        return System.Enum.IsDefined(candidate) ? candidate : throw new InvalidDataException("Unknown enum value in log record");
    }

    /// <summary>リトルエンディアンで追記するバッファーです。</summary>
    private sealed class ByteWriter
    {
        /// <summary>書込み先の配列です。</summary>
        private byte[] _buffer;
        /// <summary>書込み済みバイト数です。</summary>
        private int _length;

        /// <summary>初期容量を指定して生成します。</summary>
        /// <remarks>責務: 指定容量の空バッファーを用意します。</remarks>
        /// <param name="capacity">初期容量。</param>
        public ByteWriter(int capacity) => _buffer = new byte[capacity];

        /// <summary>指定バイト数を0で予約します。</summary>
        /// <remarks>責務: 後から書く枠ヘッダーの領域を確保します。</remarks>
        /// <param name="count">予約するバイト数。</param>
        public void Skip(int count) => Reserve(count);

        /// <summary>1バイトを書きます。</summary>
        /// <remarks>責務: 符号なし8ビット値を追記します。</remarks>
        /// <param name="value">書く値。</param>
        public void Byte(byte value) => Reserve(1)[0] = value;

        /// <summary>真偽値を1バイトで書きます。</summary>
        /// <remarks>責務: 真偽値を0/1として追記します。</remarks>
        /// <param name="value">書く値。</param>
        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        /// <summary>32ビット整数を書きます。</summary>
        /// <remarks>責務: 符号付き32ビット値をリトルエンディアンで追記します。</remarks>
        /// <param name="value">書く値。</param>
        public void Int32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

        /// <summary>64ビット整数を書きます。</summary>
        /// <remarks>責務: 符号付き64ビット値をリトルエンディアンで追記します。</remarks>
        /// <param name="value">書く値。</param>
        public void Int64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);

        /// <summary>符号なし64ビット整数を書きます。</summary>
        /// <remarks>責務: 符号なし64ビット値をリトルエンディアンで追記します。</remarks>
        /// <param name="value">書く値。</param>
        public void UInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);

        /// <summary>任意の文字列を書きます。</summary>
        /// <remarks>責務: 有無フラグとUTF-8長付きで文字列を追記します。</remarks>
        /// <param name="value">書く文字列。</param>
        public void String(string? value)
        {
            Bool(value is not null);
            if (value is null)
            {
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            Int32(bytes.Length);
            bytes.CopyTo(Reserve(bytes.Length));
        }

        /// <summary>書込み済みの内容を配列で返します。</summary>
        /// <remarks>責務: 書込み済み範囲だけの新しい配列を返します。</remarks>
        /// <returns>書込み済みバイト列。</returns>
        public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

        /// <summary>末尾に指定バイト数の領域を確保します。</summary>
        /// <remarks>責務: 必要なら容量を倍に広げて書込み領域を返します。</remarks>
        /// <param name="count">確保するバイト数。</param>
        /// <returns>確保した領域。</returns>
        private Span<byte> Reserve(int count)
        {
            if (_length + count > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
            }
            var span = _buffer.AsSpan(_length, count);
            _length += count;
            return span;
        }
    }

    /// <summary>リトルエンディアンで読み進めるリーダーです。</summary>
    private ref struct ByteReader
    {
        /// <summary>読取元です。</summary>
        private readonly ReadOnlySpan<byte> _data;
        /// <summary>読取位置です。</summary>
        private int _position;

        /// <summary>読取元を指定して生成します。</summary>
        /// <remarks>責務: 先頭から読む状態を作ります。</remarks>
        /// <param name="data">読取元。</param>
        public ByteReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        /// <summary>すべて読み終えたかどうかです。</summary>
        public readonly bool IsAtEnd => _position == _data.Length;

        /// <summary>1バイトを読みます。</summary>
        /// <remarks>責務: 符号なし8ビット値を読みます。</remarks>
        /// <returns>読んだ値。</returns>
        public byte Byte() => Take(1)[0];

        /// <summary>真偽値を読みます。</summary>
        /// <remarks>責務: 0/1以外を破損として拒否します。</remarks>
        /// <returns>読んだ値。</returns>
        public bool Bool() => Byte() switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid boolean in log record"),
        };

        /// <summary>32ビット整数を読みます。</summary>
        /// <remarks>責務: リトルエンディアンの符号付き32ビット値を読みます。</remarks>
        /// <returns>読んだ値。</returns>
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        /// <summary>64ビット整数を読みます。</summary>
        /// <remarks>責務: リトルエンディアンの符号付き64ビット値を読みます。</remarks>
        /// <returns>読んだ値。</returns>
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        /// <summary>符号なし64ビット整数を読みます。</summary>
        /// <remarks>責務: リトルエンディアンの符号なし64ビット値を読みます。</remarks>
        /// <returns>読んだ値。</returns>
        public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        /// <summary>指定長のバイト列を読みます。</summary>
        /// <remarks>責務: 負や範囲外の長さを破損として拒否します。</remarks>
        /// <param name="count">読むバイト数。</param>
        /// <returns>読んだ範囲。</returns>
        public ReadOnlySpan<byte> Bytes(int count) =>
            count < 0 ? throw new InvalidDataException("Negative length in log record") : Take(count);

        /// <summary>任意の文字列を読みます。</summary>
        /// <remarks>責務: 有無フラグとUTF-8長付きの文字列を読みます。</remarks>
        /// <returns>読んだ文字列、または <see langword="null"/>。</returns>
        public string? String()
        {
            if (!Bool())
            {
                return null;
            }
            return Encoding.UTF8.GetString(Bytes(Int32()));
        }

        /// <summary>指定長の範囲を読み進めます。</summary>
        /// <remarks>責務: 範囲外の読取を破損として拒否します。</remarks>
        /// <param name="count">読むバイト数。</param>
        /// <returns>読んだ範囲。</returns>
        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length)
            {
                throw new InvalidDataException("Truncated log record");
            }
            var span = _data.Slice(_position, count);
            _position += count;
            return span;
        }
    }
}
