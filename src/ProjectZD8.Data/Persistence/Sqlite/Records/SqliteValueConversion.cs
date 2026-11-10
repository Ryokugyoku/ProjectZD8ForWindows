using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProjectZD8.Data.Persistence.Sqlite.Records;

/// <summary>Domain値とSQLite列値の共通変換を提供します。</summary>
internal static class SqliteValueConversion
{
    /// <summary>識別子をSQLiteの安定キー（小文字UUID）へ変換します。</summary>
    /// <remarks>責務: iOS版と同じ小文字ハイフン付きUUID文字列を返します。</remarks>
    /// <param name="value">変換する識別子。</param>
    /// <returns>小文字UUID文字列。</returns>
    internal static string Key(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>日時をUTCのTicksへ変換します。</summary>
    /// <remarks>責務: DateTimeOffsetを精度を落とさない整数列値へ変換します。</remarks>
    /// <param name="value">変換する日時。</param>
    /// <returns>UTCのTicks。</returns>
    internal static long Ticks(DateTimeOffset value) => value.UtcTicks;

    /// <summary>任意の日時をUTCのTicksへ変換します。</summary>
    /// <remarks>責務: 欠落を <see langword="null"/> のまま保った整数列値へ変換します。</remarks>
    /// <param name="value">変換する日時。</param>
    /// <returns>UTCのTicks、または <see langword="null"/>。</returns>
    internal static long? Ticks(DateTimeOffset? value) => value?.UtcTicks;

    /// <summary>UTCのTicksを日時へ戻します。</summary>
    /// <remarks>責務: 整数列値をオフセット0の日時へ変換します。</remarks>
    /// <param name="ticks">UTCのTicks。</param>
    /// <returns>UTCの日時。</returns>
    internal static DateTimeOffset Date(long ticks) => new(ticks, TimeSpan.Zero);

    /// <summary>NULLを許す文字列列を読みます。</summary>
    /// <remarks>責務: 1列の値をNULLを保った文字列へ変換します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <param name="ordinal">列番号。</param>
    /// <returns>文字列、または <see langword="null"/>。</returns>
    internal static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>NULLを許す整数列を読みます。</summary>
    /// <remarks>責務: 1列の値をNULLを保った整数へ変換します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <param name="ordinal">列番号。</param>
    /// <returns>整数、または <see langword="null"/>。</returns>
    internal static long? NullableInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    /// <summary>NULLを許す実数列を読みます。</summary>
    /// <remarks>責務: 1列の値をNULLを保った実数へ変換します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <param name="ordinal">列番号。</param>
    /// <returns>実数、または <see langword="null"/>。</returns>
    internal static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
