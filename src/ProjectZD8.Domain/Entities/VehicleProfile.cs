using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>登録済み車両の識別情報とユーザー編集可能なプロフィールです。</summary>
public sealed record VehicleProfile
{
    /// <summary>アプリ内で車両を識別する安定IDです。</summary>
    public VehicleID Id { get; }
    /// <summary>OBD観測で確認されたVINです。</summary>
    public string Vin { get; }
    /// <summary>VINと確認できないOBD由来の識別子です。</summary>
    public string? ObdIdentifier { get; }
    /// <summary>ユーザーが編集できる車両名称です。</summary>
    public string Name { get; init; }
    /// <summary>確認済みまたはユーザー入力のメーカー名です。</summary>
    public string Manufacturer { get; init; }
    /// <summary>確認済みまたはユーザー入力のエンジン型式です。</summary>
    public string EngineModel { get; init; }
    /// <summary>車両の動力構成です。</summary>
    public VehiclePowertrainKind Powertrain { get; init; }
    /// <summary>車両が使用する1件以上のエネルギー源です。</summary>
    public IReadOnlyList<VehicleEnergySource> EnergySources { get; init; }
    /// <summary>液体または気体燃料タンクの容量です。</summary>
    public double? TankCapacityLiters { get; init; }
    /// <summary>ユーザーが確認したモデル年です。</summary>
    public int? ModelYear { get; init; }
    /// <summary>ユーザーの任意メモです。</summary>
    public string Note { get; init; }
    /// <summary>カードへ表示する同期対象画像データです。</summary>
    public byte[]? PhotoData { get; init; }
    /// <summary>この車両が既定の接続対象かどうかです。</summary>
    public bool IsDefault { get; init; }
    /// <summary>最後にプロフィールを更新した日時です。</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>VINとOBD由来識別子を混同せず表示用の代表値を返します。</summary>
    public string DisplayIdentifier => Vin.Length == 0 ? (ObdIdentifier ?? "") : Vin;

    /// <summary>登録に必要な車両プロフィールを生成します。</summary>
    /// <remarks>責務: 1台分の識別値とユーザー編集値を登録可能なプロフィールへまとめます。</remarks>
    /// <param name="vin">OBD観測で確認されたVIN。</param>
    /// <param name="name">ユーザーが編集できる車両名称。</param>
    /// <param name="id">車両の安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="obdIdentifier">VINと確認できないOBD由来の識別子。</param>
    /// <param name="manufacturer">メーカー名。</param>
    /// <param name="engineModel">エンジン型式。</param>
    /// <param name="powertrain">車両の動力構成。</param>
    /// <param name="energySources">エネルギー源。<see langword="null"/> なら <see cref="VehicleEnergySource.GasolineRegular"/> だけです。</param>
    /// <param name="tankCapacityLiters">燃料タンクの容量。</param>
    /// <param name="modelYear">モデル年。</param>
    /// <param name="note">任意メモ。</param>
    /// <param name="photoData">カード表示用の画像データ。</param>
    /// <param name="isDefault">既定の接続対象かどうか。</param>
    /// <param name="updatedAt">最後に更新した日時。<see langword="null"/> なら現在時刻です。</param>
    public VehicleProfile(
        string vin,
        string name,
        VehicleID? id = null,
        string? obdIdentifier = null,
        string manufacturer = "",
        string engineModel = "",
        VehiclePowertrainKind powertrain = VehiclePowertrainKind.Combustion,
        IReadOnlyList<VehicleEnergySource>? energySources = null,
        double? tankCapacityLiters = null,
        int? modelYear = null,
        string note = "",
        byte[]? photoData = null,
        bool isDefault = false,
        DateTimeOffset? updatedAt = null)
    {
        Id = id ?? VehicleID.New();
        Vin = vin;
        ObdIdentifier = obdIdentifier;
        Name = name;
        Manufacturer = manufacturer;
        EngineModel = engineModel;
        Powertrain = powertrain;
        EnergySources = energySources?.ToArray() ?? [VehicleEnergySource.GasolineRegular];
        TankCapacityLiters = tankCapacityLiters;
        ModelYear = modelYear;
        Note = note;
        PhotoData = photoData?.ToArray();
        IsDefault = isDefault;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>エネルギー源列と画像データを含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく配列と画像データを要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(VehicleProfile? other) =>
        other is not null
        && Id.Equals(other.Id)
        && string.Equals(Vin, other.Vin, StringComparison.Ordinal)
        && string.Equals(ObdIdentifier, other.ObdIdentifier, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Manufacturer, other.Manufacturer, StringComparison.Ordinal)
        && string.Equals(EngineModel, other.EngineModel, StringComparison.Ordinal)
        && Powertrain == other.Powertrain
        && EnergySources.SequenceEqual(other.EnergySources)
        && TankCapacityLiters == other.TankCapacityLiters
        && ModelYear == other.ModelYear
        && string.Equals(Note, other.Note, StringComparison.Ordinal)
        && PhotoDataEquals(PhotoData, other.PhotoData)
        && IsDefault == other.IsDefault
        && UpdatedAt == other.UpdatedAt;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 配列と画像データを含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Vin, StringComparer.Ordinal);
        hash.Add(ObdIdentifier, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Manufacturer, StringComparer.Ordinal);
        hash.Add(EngineModel, StringComparer.Ordinal);
        hash.Add(Powertrain);
        foreach (var source in EnergySources)
        {
            hash.Add(source);
        }
        hash.Add(TankCapacityLiters);
        hash.Add(ModelYear);
        hash.Add(Note, StringComparer.Ordinal);
        hash.Add(PhotoData is null);
        if (PhotoData is not null)
        {
            hash.AddBytes(PhotoData);
        }
        hash.Add(IsDefault);
        hash.Add(UpdatedAt);
        return hash.ToHashCode();
    }

    /// <summary>任意の画像データ同士がバイト列として等しいかを返します。</summary>
    /// <remarks>責務: Swift の <c>Data?</c> の等価判定を <see langword="null"/> を含めて再現します。</remarks>
    /// <param name="left">比較元。</param>
    /// <param name="right">比較先。</param>
    /// <returns>両方なし、または同じバイト列なら <see langword="true"/>。</returns>
    private static bool PhotoDataEquals(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
