namespace ProjectZD8.Domain.Entities;

/// <summary>OBD識別処理から得た1回分の改変しない車両観測結果です。</summary>
public sealed record VehicleIdentificationSnapshot
{
    /// <summary>VINとして取得された未加工文字列です。</summary>
    public string? Vin { get; }
    /// <summary>VINと確認できないOBD応答から取得した識別子です。</summary>
    public string? ObdIdentifier { get; }
    /// <summary>ECUなどから取得できた全表示可能フィールドです。</summary>
    public IReadOnlyList<Field> Fields { get; }
    /// <summary>後続の未加工保存へ渡せるコマンド単位の原文応答です。</summary>
    public IReadOnlyList<RawResponse> RawResponses { get; }
    /// <summary>観測が完了した日時です。</summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>OBD識別結果に含まれる1件の名称付き観測値です。</summary>
    public sealed record Field
    {
        /// <summary>同一観測内で安定するフィールド識別子です。</summary>
        public string Id { get; }
        /// <summary>ユーザーへ表示できる項目名です。</summary>
        public string Label { get; }
        /// <summary>ECUから取得した未加工の表示値です。</summary>
        public string Value { get; }
        /// <summary>値を返したECUまたは識別サービスです。</summary>
        public string Source { get; }

        /// <summary>名称、値、取得元を持つ観測フィールドを生成します。</summary>
        /// <remarks>責務: 1件のOBD観測値を取得元付き表示フィールドとして固定します。</remarks>
        /// <param name="id">フィールド識別子。</param>
        /// <param name="label">表示項目名。</param>
        /// <param name="value">未加工の表示値。</param>
        /// <param name="source">取得元。</param>
        public Field(string id, string label, string value, string source)
        {
            Id = id;
            Label = label;
            Value = value;
            Source = source;
        }
    }

    /// <summary>1件のOBD要求に対してアダプターから返された加工前文字列です。</summary>
    public sealed record RawResponse
    {
        /// <summary>実行した型付き要求の安定識別子です。</summary>
        public string RequestID { get; }
        /// <summary>アダプターが返したプロンプト終端前の原文です。</summary>
        public string Payload { get; }

        /// <summary>要求識別子と原文応答を固定します。</summary>
        /// <remarks>責務: 1件のOBD要求と加工前応答を再解析可能な組として保持します。</remarks>
        /// <param name="requestID">型付き要求の安定識別子。</param>
        /// <param name="payload">改行や空白を保持した原文応答。</param>
        public RawResponse(string requestID, string payload)
        {
            RequestID = requestID;
            Payload = payload;
        }
    }

    /// <summary>1回分の車両識別観測を生成します。</summary>
    /// <remarks>責務: VINまたは非VINのOBD識別子と全取得フィールドを同じ観測時点へ固定します。</remarks>
    /// <param name="vin">VINとして取得された文字列。</param>
    /// <param name="fields">取得できた全表示可能フィールド。</param>
    /// <param name="observedAt">観測完了日時。</param>
    /// <param name="obdIdentifier">VINと確認できないOBD由来識別子。</param>
    /// <param name="rawResponses">要求単位の加工前応答。<see langword="null"/> なら空です。</param>
    public VehicleIdentificationSnapshot(
        string? vin,
        IReadOnlyList<Field> fields,
        DateTimeOffset observedAt,
        string? obdIdentifier = null,
        IReadOnlyList<RawResponse>? rawResponses = null)
    {
        Vin = vin;
        ObdIdentifier = obdIdentifier;
        Fields = fields.ToArray();
        RawResponses = rawResponses?.ToArray() ?? [];
        ObservedAt = observedAt;
    }

    /// <summary>フィールド列と原文応答列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく配列項目を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(VehicleIdentificationSnapshot? other) =>
        other is not null
        && string.Equals(Vin, other.Vin, StringComparison.Ordinal)
        && string.Equals(ObdIdentifier, other.ObdIdentifier, StringComparison.Ordinal)
        && Fields.SequenceEqual(other.Fields)
        && RawResponses.SequenceEqual(other.RawResponses)
        && ObservedAt == other.ObservedAt;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 配列項目の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Vin, StringComparer.Ordinal);
        hash.Add(ObdIdentifier, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }
        foreach (var response in RawResponses)
        {
            hash.Add(response);
        }
        hash.Add(ObservedAt);
        return hash.ToHashCode();
    }
}
