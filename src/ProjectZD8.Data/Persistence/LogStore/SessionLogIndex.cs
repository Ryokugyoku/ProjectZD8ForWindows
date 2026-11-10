using System.Buffers.Binary;
using ProjectZD8.Data.Persistence.LogStore.Format;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>ジャーナル上の論理位置（セグメント番号とセグメント内オフセット）です。</summary>
/// <param name="Segment">セグメント番号。</param>
/// <param name="Offset">セグメント内の論理オフセット。</param>
internal readonly record struct SessionLogPosition(int Segment, long Offset) : IComparable<SessionLogPosition>
{
    /// <summary>位置の前後を比べます。</summary>
    /// <remarks>責務: セグメント番号、オフセットの順で比較します。</remarks>
    /// <param name="other">比べる位置。</param>
    /// <returns>前なら負、同じなら0、後なら正。</returns>
    public int CompareTo(SessionLogPosition other) =>
        Segment != other.Segment ? Segment.CompareTo(other.Segment) : Offset.CompareTo(other.Offset);
}

/// <summary>1ブロック（連続した生ログ <c>stride</c> 件）の索引項目です。</summary>
/// <param name="FirstSequence">ブロック先頭の生ログのsequence。</param>
/// <param name="Position">ブロック先頭の生ログを含むレコードの位置。</param>
/// <param name="MinimumTicks">ブロック内の観測日時の最小値。</param>
/// <param name="MaximumTicks">ブロック内の観測日時の最大値。</param>
/// <param name="Count">ブロック内の生ログ件数。</param>
internal readonly record struct SessionLogIndexBlock(
    long FirstSequence,
    SessionLogPosition Position,
    long MinimumTicks,
    long MaximumTicks,
    int Count);

/// <summary>生ログのsequenceと観測日時からジャーナル位置を引く疎な索引です。</summary>
/// <remarks>
/// <c>stride</c> 件ごとに1項目を持ち、各項目にブロック内の観測日時の最小・最大を記録します。
/// sequence範囲の読取は該当ブロックの先頭から、日時範囲の読取は最小・最大が重なるブロックだけを読みます。
/// 観測日時が単調でなくても正しく動きます（単調なら読むブロックが連続して最小になります）。
/// 呼出元（<see cref="SessionLog"/>）のロック内で使います。
/// </remarks>
internal sealed class SessionLogIndex
{
    /// <summary>索引ファイルのマジックです。</summary>
    private const ulong SidecarMagic = 0x3130584449384435UL;

    /// <summary>ブロック項目です。</summary>
    private readonly List<SessionLogIndexBlock> _blocks = [];

    /// <summary>1ブロックの生ログ件数を指定して生成します。</summary>
    /// <remarks>責務: 空の索引を作ります。</remarks>
    /// <param name="stride">1ブロックの生ログ件数。</param>
    internal SessionLogIndex(int stride) => Stride = Math.Max(1, stride);

    /// <summary>1ブロックの生ログ件数です。</summary>
    internal int Stride { get; }

    /// <summary>生ログの件数です。</summary>
    internal long Count { get; private set; }

    /// <summary>生ログの応答データの合計バイト数です。</summary>
    internal long ByteCount { get; private set; }

    /// <summary>次に採番するsequenceです。</summary>
    internal long NextSequence { get; private set; }

    /// <summary>ブロック項目の数です。</summary>
    internal int BlockCount => _blocks.Count;

    /// <summary>生ログ1件を索引へ加えます。</summary>
    /// <remarks>責務: 追記またはジャーナル走査で見つけた生ログをブロックへ反映します。</remarks>
    /// <param name="sequence">生ログのsequence。</param>
    /// <param name="observedTicks">観測日時のTicks。</param>
    /// <param name="payloadLength">応答データのバイト数。</param>
    /// <param name="position">生ログを含むレコードの位置。</param>
    internal void Add(long sequence, long observedTicks, int payloadLength, SessionLogPosition position)
    {
        if (_blocks.Count == 0 || _blocks[^1].Count >= Stride)
        {
            _blocks.Add(new SessionLogIndexBlock(sequence, position, observedTicks, observedTicks, 1));
        }
        else
        {
            var last = _blocks[^1];
            _blocks[^1] = last with
            {
                MinimumTicks = Math.Min(last.MinimumTicks, observedTicks),
                MaximumTicks = Math.Max(last.MaximumTicks, observedTicks),
                Count = last.Count + 1,
            };
        }
        Count += 1;
        ByteCount += payloadLength;
        NextSequence = Math.Max(NextSequence, sequence + 1);
    }

    /// <summary>指定sequenceを含みうる最初のブロックの位置を返します。</summary>
    /// <remarks>責務: sequence範囲読取の開始位置を二分探索で求めます。</remarks>
    /// <param name="sequence">読取を始めるsequence。</param>
    /// <returns>読取開始位置。生ログがなければ <see langword="null"/>。</returns>
    internal SessionLogPosition? StartPosition(long sequence)
    {
        if (_blocks.Count == 0)
        {
            return null;
        }
        var low = 0;
        var high = _blocks.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_blocks[middle].FirstSequence <= sequence)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return _blocks[low].Position;
    }

    /// <summary>日時範囲と重なるブロックの読取区間を返します。</summary>
    /// <remarks>責務: 最小・最大が範囲と重なる連続ブロックを1区間にまとめて返します。</remarks>
    /// <param name="fromTicks">範囲の開始（含む）。</param>
    /// <param name="toTicks">範囲の終了（含む）。</param>
    /// <returns>(開始位置, 終了位置。<see langword="null"/>なら末尾まで) の区間。</returns>
    internal List<(SessionLogPosition Start, SessionLogPosition? End)> TimeRanges(long fromTicks, long toTicks)
    {
        var ranges = new List<(SessionLogPosition, SessionLogPosition?)>();
        var index = 0;
        while (index < _blocks.Count)
        {
            if (!Overlaps(_blocks[index], fromTicks, toTicks))
            {
                index += 1;
                continue;
            }
            var start = _blocks[index].Position;
            while (index < _blocks.Count && Overlaps(_blocks[index], fromTicks, toTicks))
            {
                index += 1;
            }
            ranges.Add((start, index < _blocks.Count ? _blocks[index].Position : null));
        }
        return ranges;
    }

    /// <summary>索引を索引ファイルの本体へ書き出します。</summary>
    /// <remarks>責務: 再起動時にジャーナル走査を省くため、ブロック項目と件数を書き出します。</remarks>
    /// <param name="stream">書込み先。</param>
    internal void WriteTo(Stream stream)
    {
        var buffer = new byte[8 + 4 + 8 * 3 + 4 + _blocks.Count * (8 + 4 + 8 + 8 + 8 + 4)];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt64LittleEndian(span, SidecarMagic);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], Stride);
        BinaryPrimitives.WriteInt64LittleEndian(span[12..], Count);
        BinaryPrimitives.WriteInt64LittleEndian(span[20..], ByteCount);
        BinaryPrimitives.WriteInt64LittleEndian(span[28..], NextSequence);
        BinaryPrimitives.WriteInt32LittleEndian(span[36..], _blocks.Count);
        var offset = 40;
        foreach (var block in _blocks)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span[offset..], block.FirstSequence);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], block.Position.Segment);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 12)..], block.Position.Offset);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 20)..], block.MinimumTicks);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 28)..], block.MaximumTicks);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 36)..], block.Count);
            offset += 40;
        }
        stream.Write(buffer);
    }

    /// <summary>索引ファイルの本体から索引を読み込みます。</summary>
    /// <remarks>責務: 書き出し形式を検証して索引を復元します。</remarks>
    /// <param name="data">索引ファイルの本体。</param>
    /// <returns>復元した索引。形式が不正なら <see langword="null"/>。</returns>
    internal static SessionLogIndex? ReadFrom(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40 || BinaryPrimitives.ReadUInt64LittleEndian(data) != SidecarMagic)
        {
            return null;
        }
        var index = new SessionLogIndex(BinaryPrimitives.ReadInt32LittleEndian(data[8..]))
        {
            Count = BinaryPrimitives.ReadInt64LittleEndian(data[12..]),
            ByteCount = BinaryPrimitives.ReadInt64LittleEndian(data[20..]),
            NextSequence = BinaryPrimitives.ReadInt64LittleEndian(data[28..]),
        };
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[36..]);
        if (count < 0 || data.Length != 40 + (long)count * 40)
        {
            return null;
        }
        var offset = 40;
        for (var block = 0; block < count; block++)
        {
            index._blocks.Add(new SessionLogIndexBlock(
                BinaryPrimitives.ReadInt64LittleEndian(data[offset..]),
                new SessionLogPosition(
                    BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 8)..]),
                    BinaryPrimitives.ReadInt64LittleEndian(data[(offset + 12)..])),
                BinaryPrimitives.ReadInt64LittleEndian(data[(offset + 20)..]),
                BinaryPrimitives.ReadInt64LittleEndian(data[(offset + 28)..]),
                BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 36)..])));
            offset += 40;
        }
        return index;
    }

    /// <summary>ブロックが日時範囲と重なるかを返します。</summary>
    /// <remarks>責務: ブロックの最小・最大と範囲の重なりを判定します。</remarks>
    /// <param name="block">判定するブロック。</param>
    /// <param name="fromTicks">範囲の開始（含む）。</param>
    /// <param name="toTicks">範囲の終了（含む）。</param>
    /// <returns>重なる場合は <see langword="true"/>。</returns>
    private static bool Overlaps(SessionLogIndexBlock block, long fromTicks, long toTicks) =>
        block.MinimumTicks <= toTicks && block.MaximumTicks >= fromTicks;
}
