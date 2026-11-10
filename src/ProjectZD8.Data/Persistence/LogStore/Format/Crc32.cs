namespace ProjectZD8.Data.Persistence.LogStore.Format;

/// <summary>IEEE 802.3（zlibと同じ）のCRC-32を計算します。</summary>
/// <remarks>追加のNuGet（System.IO.Hashing）を使わないため、slicing-by-4の表引きで実装します。</remarks>
internal static class Crc32
{
    /// <summary>反転多項式です。</summary>
    private const uint Polynomial = 0xEDB88320u;

    /// <summary>slicing-by-4用の4本の表です。</summary>
    private static readonly uint[][] Tables = CreateTables();

    /// <summary>バイト列のCRC-32を返します。</summary>
    /// <remarks>責務: 1件のレコード本体の破損検知用チェックサムを計算します。</remarks>
    /// <param name="data">計算するバイト列。</param>
    /// <returns>CRC-32値。</returns>
    internal static uint Compute(ReadOnlySpan<byte> data)
    {
        var t0 = Tables[0];
        var t1 = Tables[1];
        var t2 = Tables[2];
        var t3 = Tables[3];
        var crc = 0xFFFFFFFFu;
        var index = 0;
        for (; index + 4 <= data.Length; index += 4)
        {
            crc ^= (uint)(data[index] | (data[index + 1] << 8) | (data[index + 2] << 16) | (data[index + 3] << 24));
            crc = t3[crc & 0xFF] ^ t2[(crc >> 8) & 0xFF] ^ t1[(crc >> 16) & 0xFF] ^ t0[crc >> 24];
        }
        for (; index < data.Length; index++)
        {
            crc = t0[(crc ^ data[index]) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    /// <summary>slicing-by-4の表を生成します。</summary>
    /// <remarks>責務: 多項式から4本の256要素表を1回だけ作ります。</remarks>
    /// <returns>4本の表。</returns>
    private static uint[][] CreateTables()
    {
        var tables = new uint[4][];
        for (var t = 0; t < 4; t++)
        {
            tables[t] = new uint[256];
        }
        for (uint value = 0; value < 256; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }
            tables[0][value] = crc;
        }
        for (var value = 0; value < 256; value++)
        {
            for (var t = 1; t < 4; t++)
            {
                var previous = tables[t - 1][value];
                tables[t][value] = (previous >> 8) ^ tables[0][previous & 0xFF];
            }
        }
        return tables;
    }
}
