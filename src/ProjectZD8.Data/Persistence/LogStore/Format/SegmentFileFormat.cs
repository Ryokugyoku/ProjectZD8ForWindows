using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ProjectZD8.Data.Persistence.LogStore.Format;

/// <summary>セグメントファイル（非圧縮 <c>.log</c> と圧縮 <c>.logz</c>）の形式を定義します。</summary>
/// <remarks>
/// 非圧縮セグメントは16バイトのヘッダー（マジック <c>ZD8LOGv1</c>、セグメント番号）の後に枠付きレコードを並べます。
/// 圧縮セグメントは非圧縮ファイルの全バイト（ヘッダーを含む）を固定長ブロックごとにDeflate圧縮したもので、
/// 索引が持つオフセット（非圧縮ファイル上の位置）は圧縮後もそのまま使えます。
/// </remarks>
internal static class SegmentFileFormat
{
    /// <summary>非圧縮セグメントのヘッダーバイト数です。</summary>
    internal const int HeaderSize = 16;

    /// <summary>圧縮セグメントのヘッダーバイト数です。</summary>
    internal const int CompressedHeaderSize = 24;

    /// <summary>圧縮ブロックヘッダーのバイト数です。</summary>
    internal const int CompressedBlockHeaderSize = 12;

    /// <summary>圧縮ブロックの非圧縮バイト数です。</summary>
    internal const int CompressedBlockSize = 64 * 1024;

    /// <summary>非圧縮セグメントのマジックです。</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ZD8LOGv1");

    /// <summary>圧縮セグメントのマジックです。</summary>
    private static readonly byte[] CompressedMagic = Encoding.ASCII.GetBytes("ZD8LOGZ1");

    /// <summary>非圧縮セグメントのファイルパスを返します。</summary>
    /// <remarks>責務: 世代ディレクトリーとセグメント番号から非圧縮ファイル名を組み立てます。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">セグメント番号。</param>
    /// <returns>ファイルパス。</returns>
    internal static string PlainPath(string directory, int index) =>
        Path.Combine(directory, $"seg-{index.ToString("D6", CultureInfo.InvariantCulture)}.log");

    /// <summary>圧縮セグメントのファイルパスを返します。</summary>
    /// <remarks>責務: 世代ディレクトリーとセグメント番号から圧縮ファイル名を組み立てます。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">セグメント番号。</param>
    /// <returns>ファイルパス。</returns>
    internal static string CompressedPath(string directory, int index) => PlainPath(directory, index) + "z";

    /// <summary>世代ディレクトリー内のセグメント番号を昇順で返します。</summary>
    /// <remarks>責務: 非圧縮と圧縮のどちらかがある番号を列挙します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <returns>昇順のセグメント番号。</returns>
    internal static List<int> ListSegments(string directory)
    {
        var indexes = new SortedSet<int>();
        foreach (var path in Directory.EnumerateFiles(directory, "seg-*.log*"))
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(".log", StringComparison.Ordinal) && !name.EndsWith(".logz", StringComparison.Ordinal))
            {
                continue;
            }
            var digits = name.AsSpan(4, 6);
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                indexes.Add(index);
            }
        }
        return indexes.ToList();
    }

    /// <summary>非圧縮セグメントのヘッダーを書きます。</summary>
    /// <remarks>責務: 新しいセグメントファイルの先頭16バイトを書きます。</remarks>
    /// <param name="stream">書込み先（先頭位置）。</param>
    /// <param name="index">セグメント番号。</param>
    internal static void WriteHeader(Stream stream, int index)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], index);
        stream.Write(header);
    }

    /// <summary>非圧縮セグメントのヘッダーを検証します。</summary>
    /// <remarks>責務: 先頭16バイトのマジックとセグメント番号を確かめます。</remarks>
    /// <param name="header">先頭16バイト。</param>
    /// <param name="index">期待するセグメント番号。</param>
    /// <returns>正しいヘッダーなら <see langword="true"/>。</returns>
    internal static bool IsValidHeader(ReadOnlySpan<byte> header, int index) =>
        header.Length >= HeaderSize
        && header[..8].SequenceEqual(Magic)
        && BinaryPrimitives.ReadInt32LittleEndian(header[8..]) == index;

    /// <summary>セグメントの論理長（非圧縮ファイル上のバイト数）を返します。</summary>
    /// <remarks>責務: 非圧縮ならファイル長、圧縮ならヘッダーに記録した元の長さを返します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">セグメント番号。</param>
    /// <returns>論理長。セグメントがなければ -1。</returns>
    internal static long LogicalLength(string directory, int index)
    {
        var plain = PlainPath(directory, index);
        if (File.Exists(plain))
        {
            return new FileInfo(plain).Length;
        }
        var compressed = CompressedPath(directory, index);
        if (!File.Exists(compressed))
        {
            return -1;
        }
        using var stream = new FileStream(compressed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[CompressedHeaderSize];
        stream.ReadExactly(header);
        return header[..8].SequenceEqual(CompressedMagic) ? BinaryPrimitives.ReadInt64LittleEndian(header[16..]) : -1;
    }

    /// <summary>セグメントを論理バイト列として読むストリームを開きます。</summary>
    /// <remarks>責務: 非圧縮ならファイルを、圧縮ならブロック単位で展開するストリームを返します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">セグメント番号。</param>
    /// <returns>シーク可能な読取専用ストリーム。</returns>
    /// <exception cref="FileNotFoundException">セグメントがない場合。</exception>
    internal static Stream OpenRead(string directory, int index)
    {
        var plain = PlainPath(directory, index);
        if (File.Exists(plain))
        {
            return new FileStream(plain, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        }
        var compressed = CompressedPath(directory, index);
        var file = new FileStream(compressed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        return new CompressedSegmentStream(file);
    }

    /// <summary>非圧縮セグメントを圧縮セグメントへ書き出します。</summary>
    /// <remarks>
    /// 責務: 確定済みセグメントの全バイトをブロックごとに圧縮し、一時ファイルへ書いてから確定名へ置き換えます。
    /// 元の非圧縮ファイルは呼出元が削除します。
    /// </remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">セグメント番号。</param>
    /// <returns>書き出した圧縮ファイルのパス（一時ファイル）。</returns>
    internal static string WriteCompressedTemporary(string directory, int index)
    {
        var source = PlainPath(directory, index);
        var temporary = CompressedPath(directory, index) + ".tmp";
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        Span<byte> header = stackalloc byte[CompressedHeaderSize];
        CompressedMagic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], index);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], CompressedBlockSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], input.Length);
        output.Write(header);
        var block = new byte[CompressedBlockSize];
        using var compressedBlock = new MemoryStream();
        Span<byte> blockHeader = stackalloc byte[CompressedBlockHeaderSize];
        int read;
        while ((read = input.ReadAtLeast(block, block.Length, throwOnEndOfStream: false)) > 0)
        {
            compressedBlock.SetLength(0);
            using (var deflate = new DeflateStream(compressedBlock, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(block, 0, read);
            }
            var compressed = compressedBlock.GetBuffer().AsSpan(0, (int)compressedBlock.Length);
            BinaryPrimitives.WriteInt32LittleEndian(blockHeader, read);
            BinaryPrimitives.WriteInt32LittleEndian(blockHeader[4..], compressed.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(blockHeader[8..], Crc32.Compute(compressed));
            output.Write(blockHeader);
            output.Write(compressed);
        }
        output.Flush(flushToDisk: true);
        return temporary;
    }

    /// <summary>圧縮セグメントの論理バイト列をブロック単位で展開して読むストリームです。</summary>
    private sealed class CompressedSegmentStream : Stream
    {
        /// <summary>圧縮ファイルごとのブロック表です（圧縮ファイルは作成後に変わらないため、長さと更新日時が同じ間は再利用します）。</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BlockTable> BlockTables = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>圧縮ファイルです。</summary>
        private readonly FileStream _file;
        /// <summary>各ブロックの (論理開始位置, ファイル内位置, 非圧縮長, 圧縮長, CRC) です。</summary>
        private readonly IReadOnlyList<(long LogicalStart, long FileOffset, int RawLength, int CompressedLength, uint Checksum)> _blocks;
        /// <summary>論理長です。</summary>
        private readonly long _length;
        /// <summary>展開済みブロックの番号です。</summary>
        private int _cachedBlock = -1;
        /// <summary>展開済みブロックの内容です。</summary>
        private byte[] _cache = [];
        /// <summary>論理読取位置です。</summary>
        private long _position;

        /// <summary>圧縮ファイルのブロック表を読み込みます。</summary>
        /// <remarks>責務: キャッシュがあれば再利用し、なければヘッダーと全ブロックヘッダーを走査して論理位置の対応表を作ります。</remarks>
        /// <param name="file">開いた圧縮ファイル。</param>
        /// <exception cref="InvalidDataException">形式が不正な場合。</exception>
        public CompressedSegmentStream(FileStream file)
        {
            _file = file;
            var stamp = File.GetLastWriteTimeUtc(file.Name).Ticks;
            if (!BlockTables.TryGetValue(file.Name, out var table) || table.FileLength != file.Length || table.Stamp != stamp)
            {
                table = ReadBlockTable(file, stamp);
                BlockTables[file.Name] = table;
            }
            _length = table.LogicalLength;
            _blocks = table.Blocks;
        }

        /// <summary>ヘッダーと全ブロックヘッダーを走査してブロック表を作ります。</summary>
        /// <remarks>責務: 圧縮ファイルの論理位置とファイル内位置の対応を検証しながら読みます。</remarks>
        /// <param name="file">開いた圧縮ファイル。</param>
        /// <param name="stamp">ファイルの更新日時。</param>
        /// <returns>ブロック表。</returns>
        /// <exception cref="InvalidDataException">形式が不正な場合。</exception>
        private static BlockTable ReadBlockTable(FileStream file, long stamp)
        {
            file.Position = 0;
            Span<byte> header = stackalloc byte[CompressedHeaderSize];
            file.ReadExactly(header);
            if (!header[..8].SequenceEqual(CompressedMagic))
            {
                throw new InvalidDataException("Invalid compressed segment header");
            }
            var length = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
            var blocks = new List<(long, long, int, int, uint)>();
            Span<byte> blockHeader = stackalloc byte[CompressedBlockHeaderSize];
            long logical = 0;
            while (logical < length)
            {
                file.ReadExactly(blockHeader);
                var raw = BinaryPrimitives.ReadInt32LittleEndian(blockHeader);
                var compressed = BinaryPrimitives.ReadInt32LittleEndian(blockHeader[4..]);
                var checksum = BinaryPrimitives.ReadUInt32LittleEndian(blockHeader[8..]);
                if (raw <= 0 || raw > CompressedBlockSize || compressed <= 0 || file.Position + compressed > file.Length)
                {
                    throw new InvalidDataException("Invalid compressed block header");
                }
                blocks.Add((logical, file.Position, raw, compressed, checksum));
                file.Seek(compressed, SeekOrigin.Current);
                logical += raw;
            }
            return new BlockTable(file.Length, stamp, length, blocks);
        }

        /// <summary>1つの圧縮ファイルのブロック表です。</summary>
        /// <param name="FileLength">圧縮ファイルの長さ。</param>
        /// <param name="Stamp">圧縮ファイルの更新日時。</param>
        /// <param name="LogicalLength">論理長。</param>
        /// <param name="Blocks">ブロックの対応表。</param>
        private sealed record BlockTable(
            long FileLength,
            long Stamp,
            long LogicalLength,
            IReadOnlyList<(long LogicalStart, long FileOffset, int RawLength, int CompressedLength, uint Checksum)> Blocks);

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => _length;

        /// <inheritdoc/>
        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        /// <summary>論理位置から読み取ります。</summary>
        /// <remarks>責務: 必要なブロックを展開して論理バイト列を返します。</remarks>
        /// <param name="buffer">読取先。</param>
        /// <param name="offset">読取先の開始位置。</param>
        /// <param name="count">最大読取バイト数。</param>
        /// <returns>読んだバイト数。終端では0。</returns>
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <summary>論理位置から読み取ります。</summary>
        /// <remarks>責務: 必要なブロックを展開して論理バイト列を返します。</remarks>
        /// <param name="buffer">読取先。</param>
        /// <returns>読んだバイト数。終端では0。</returns>
        public override int Read(Span<byte> buffer)
        {
            var total = 0;
            while (buffer.Length > 0 && _position < _length)
            {
                var blockIndex = FindBlock(_position);
                LoadBlock(blockIndex);
                var block = _blocks[blockIndex];
                var within = (int)(_position - block.LogicalStart);
                var available = Math.Min(block.RawLength - within, buffer.Length);
                _cache.AsSpan(within, available).CopyTo(buffer);
                buffer = buffer[available..];
                _position += available;
                total += available;
            }
            return total;
        }

        /// <summary>論理位置を移動します。</summary>
        /// <remarks>責務: 基準位置からの相対移動を論理位置へ反映します。</remarks>
        /// <param name="offset">移動量。</param>
        /// <param name="origin">基準位置。</param>
        /// <returns>新しい論理位置。</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            return _position;
        }

        /// <summary>何もしません（読取専用）。</summary>
        /// <remarks>責務: 読取専用ストリームとして書込み系を無効にします。</remarks>
        public override void Flush()
        {
        }

        /// <summary>長さは変更できません。</summary>
        /// <remarks>責務: 読取専用ストリームとして長さ変更を拒否します。</remarks>
        /// <param name="value">新しい長さ。</param>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>書込みはできません。</summary>
        /// <remarks>責務: 読取専用ストリームとして書込みを拒否します。</remarks>
        /// <param name="buffer">書込み元。</param>
        /// <param name="offset">開始位置。</param>
        /// <param name="count">バイト数。</param>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>論理位置を含むブロック番号を探します。</summary>
        /// <remarks>責務: ブロック表を二分探索します。</remarks>
        /// <param name="position">論理位置。</param>
        /// <returns>ブロック番号。</returns>
        private int FindBlock(long position)
        {
            var low = 0;
            var high = _blocks.Count - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (_blocks[middle].LogicalStart <= position)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return low;
        }

        /// <summary>ブロックを展開してキャッシュします。</summary>
        /// <remarks>責務: CRCを検証してから1ブロックを展開します。</remarks>
        /// <param name="blockIndex">ブロック番号。</param>
        /// <exception cref="InvalidDataException">CRCや長さが一致しない場合。</exception>
        private void LoadBlock(int blockIndex)
        {
            if (_cachedBlock == blockIndex)
            {
                return;
            }
            var block = _blocks[blockIndex];
            var compressed = new byte[block.CompressedLength];
            _file.Position = block.FileOffset;
            _file.ReadExactly(compressed);
            if (Crc32.Compute(compressed) != block.Checksum)
            {
                throw new InvalidDataException("Compressed block checksum mismatch");
            }
            if (_cache.Length < block.RawLength)
            {
                _cache = new byte[CompressedBlockSize];
            }
            using var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            deflate.ReadExactly(_cache, 0, block.RawLength);
            _cachedBlock = blockIndex;
        }

        /// <summary>圧縮ファイルを閉じます。</summary>
        /// <remarks>責務: 基になるファイルを解放します。</remarks>
        /// <param name="disposing">破棄中かどうか。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
