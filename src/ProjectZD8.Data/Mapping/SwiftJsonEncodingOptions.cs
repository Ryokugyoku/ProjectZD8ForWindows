namespace ProjectZD8.Data.Mapping;

/// <summary>
/// Swift の <c>JSONEncoder</c> の設定（日付の方式、キーの並べ替え、スラッシュのエスケープ）を表します。
/// </summary>
/// <param name="Dates">日付の方式。</param>
/// <param name="SortedKeys"><c>.sortedKeys</c> を付けたときと同じ順にキーを並べるか。</param>
/// <param name="EscapeSlashes"><c>/</c> を <c>\/</c> と書くか（Swift の既定。<c>.withoutEscapingSlashes</c> なら <c>false</c>）。</param>
public sealed record SwiftJsonEncodingOptions(SwiftJsonDateEncoding Dates, bool SortedKeys, bool EscapeSlashes)
{
    /// <summary>
    /// Swift の既定の <c>JSONEncoder()</c> と同じ設定です（車両カタログ、整備カタログ、端末内設定）。
    /// </summary>
    public static SwiftJsonEncodingOptions Default { get; } =
        new(SwiftJsonDateEncoding.SecondsSinceReferenceDate, SortedKeys: false, EscapeSlashes: true);

    /// <summary>
    /// 接続セッションの CloudKit 転送（<c>[.sortedKeys, .withoutEscapingSlashes]</c>、<c>.millisecondsSince1970</c>）と同じ設定です。
    /// </summary>
    public static SwiftJsonEncodingOptions SessionTransfer { get; } =
        new(SwiftJsonDateEncoding.MillisecondsSince1970, SortedKeys: true, EscapeSlashes: false);

    /// <summary>
    /// アカウント設定の CloudKit レコード（settingsData）用の設定です。キーを並べ替え、日付は含みません。
    /// </summary>
    public static SwiftJsonEncodingOptions SortedDefault { get; } =
        new(SwiftJsonDateEncoding.SecondsSinceReferenceDate, SortedKeys: true, EscapeSlashes: true);
}
