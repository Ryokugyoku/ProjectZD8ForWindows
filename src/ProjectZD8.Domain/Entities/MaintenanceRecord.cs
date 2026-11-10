using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>車両ごとの整備区分です。</summary>
public enum MaintenanceKind
{
    /// <summary>消耗品交換など日常的な軽整備です。</summary>
    Light,
    /// <summary>専門的な分解、修理、オーバーホールを含む重整備です。</summary>
    Heavy,
}

/// <summary><see cref="MaintenanceKind"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class MaintenanceKindRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<MaintenanceKind> AllCases { get; } = Array.AsReadOnly(new[]
    {
        MaintenanceKind.Light,
        MaintenanceKind.Heavy,
    });

    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this MaintenanceKind value) => value switch
    {
        MaintenanceKind.Light => "light",
        MaintenanceKind.Heavy => "heavy",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out MaintenanceKind value)
    {
        switch (rawValue)
        {
            case "light": value = MaintenanceKind.Light; return true;
            case "heavy": value = MaintenanceKind.Heavy; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>軽整備の消耗品と重整備の主要部品を共通選択肢として表します。</summary>
public enum MaintenanceComponent
{
    /// <summary>エンジンオイルです。</summary>
    EngineOil,
    /// <summary>オイルフィルターです。</summary>
    OilFilter,
    /// <summary>エアフィルターです。</summary>
    AirFilter,
    /// <summary>タイヤです。</summary>
    Tires,
    /// <summary>ブレーキパッドです。</summary>
    BrakePads,
    /// <summary>補機バッテリーです。</summary>
    AuxiliaryBattery,
    /// <summary>ワイパーです。</summary>
    Wipers,
    /// <summary>冷却液です。</summary>
    Coolant,
    /// <summary>トランスミッションフルードです。</summary>
    TransmissionFluid,
    /// <summary>デファレンシャルオイルです。</summary>
    DifferentialFluid,
    /// <summary>スパークプラグです。</summary>
    SparkPlugs,
    /// <summary>ベルト類です。</summary>
    Belts,
    /// <summary>灯火類です。</summary>
    Bulbs,
    /// <summary>エンジン本体です。</summary>
    EngineAssembly,
    /// <summary>トランスミッション本体です。</summary>
    Transmission,
    /// <summary>駆動系です。</summary>
    Driveline,
    /// <summary>サスペンションです。</summary>
    Suspension,
    /// <summary>ステアリングです。</summary>
    Steering,
    /// <summary>ブレーキシステムです。</summary>
    BrakingSystem,
    /// <summary>排気系です。</summary>
    Exhaust,
    /// <summary>燃料系です。</summary>
    FuelSystem,
    /// <summary>冷却系です。</summary>
    CoolingSystem,
    /// <summary>電装系です。</summary>
    Electrical,
    /// <summary>空調系です。</summary>
    AirConditioning,
    /// <summary>安全装置です。</summary>
    SafetySystem,
    /// <summary>高電圧バッテリーです。</summary>
    HighVoltageBattery,
    /// <summary>駆動モーターまたは発電機です。</summary>
    TractionMotor,
    /// <summary>ボディまたはフレームです。</summary>
    BodyFrame,
    /// <summary>一覧外の部品です。</summary>
    Other,
}

/// <summary><see cref="MaintenanceComponent"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class MaintenanceComponentRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<MaintenanceComponent> AllCases { get; } = Array.AsReadOnly(new[]
    {
        MaintenanceComponent.EngineOil,
        MaintenanceComponent.OilFilter,
        MaintenanceComponent.AirFilter,
        MaintenanceComponent.Tires,
        MaintenanceComponent.BrakePads,
        MaintenanceComponent.AuxiliaryBattery,
        MaintenanceComponent.Wipers,
        MaintenanceComponent.Coolant,
        MaintenanceComponent.TransmissionFluid,
        MaintenanceComponent.DifferentialFluid,
        MaintenanceComponent.SparkPlugs,
        MaintenanceComponent.Belts,
        MaintenanceComponent.Bulbs,
        MaintenanceComponent.EngineAssembly,
        MaintenanceComponent.Transmission,
        MaintenanceComponent.Driveline,
        MaintenanceComponent.Suspension,
        MaintenanceComponent.Steering,
        MaintenanceComponent.BrakingSystem,
        MaintenanceComponent.Exhaust,
        MaintenanceComponent.FuelSystem,
        MaintenanceComponent.CoolingSystem,
        MaintenanceComponent.Electrical,
        MaintenanceComponent.AirConditioning,
        MaintenanceComponent.SafetySystem,
        MaintenanceComponent.HighVoltageBattery,
        MaintenanceComponent.TractionMotor,
        MaintenanceComponent.BodyFrame,
        MaintenanceComponent.Other,
    });

    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this MaintenanceComponent value) => value switch
    {
        MaintenanceComponent.EngineOil => "engineOil",
        MaintenanceComponent.OilFilter => "oilFilter",
        MaintenanceComponent.AirFilter => "airFilter",
        MaintenanceComponent.Tires => "tires",
        MaintenanceComponent.BrakePads => "brakePads",
        MaintenanceComponent.AuxiliaryBattery => "auxiliaryBattery",
        MaintenanceComponent.Wipers => "wipers",
        MaintenanceComponent.Coolant => "coolant",
        MaintenanceComponent.TransmissionFluid => "transmissionFluid",
        MaintenanceComponent.DifferentialFluid => "differentialFluid",
        MaintenanceComponent.SparkPlugs => "sparkPlugs",
        MaintenanceComponent.Belts => "belts",
        MaintenanceComponent.Bulbs => "bulbs",
        MaintenanceComponent.EngineAssembly => "engineAssembly",
        MaintenanceComponent.Transmission => "transmission",
        MaintenanceComponent.Driveline => "driveline",
        MaintenanceComponent.Suspension => "suspension",
        MaintenanceComponent.Steering => "steering",
        MaintenanceComponent.BrakingSystem => "brakingSystem",
        MaintenanceComponent.Exhaust => "exhaust",
        MaintenanceComponent.FuelSystem => "fuelSystem",
        MaintenanceComponent.CoolingSystem => "coolingSystem",
        MaintenanceComponent.Electrical => "electrical",
        MaintenanceComponent.AirConditioning => "airConditioning",
        MaintenanceComponent.SafetySystem => "safetySystem",
        MaintenanceComponent.HighVoltageBattery => "highVoltageBattery",
        MaintenanceComponent.TractionMotor => "tractionMotor",
        MaintenanceComponent.BodyFrame => "bodyFrame",
        MaintenanceComponent.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out MaintenanceComponent value)
    {
        foreach (var candidate in AllCases)
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>整備対象へ実施した作業種別です。</summary>
public enum MaintenanceOperation
{
    /// <summary>点検です。</summary>
    Inspection,
    /// <summary>交換です。</summary>
    Replacement,
    /// <summary>修理です。</summary>
    Repair,
    /// <summary>調整です。</summary>
    Adjustment,
    /// <summary>オーバーホールです。</summary>
    Overhaul,
    /// <summary>清掃です。</summary>
    Cleaning,
    /// <summary>診断です。</summary>
    Diagnosis,
    /// <summary>加工または製作です。</summary>
    Fabrication,
}

/// <summary><see cref="MaintenanceOperation"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class MaintenanceOperationRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<MaintenanceOperation> AllCases { get; } = Array.AsReadOnly(new[]
    {
        MaintenanceOperation.Inspection,
        MaintenanceOperation.Replacement,
        MaintenanceOperation.Repair,
        MaintenanceOperation.Adjustment,
        MaintenanceOperation.Overhaul,
        MaintenanceOperation.Cleaning,
        MaintenanceOperation.Diagnosis,
        MaintenanceOperation.Fabrication,
    });

    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this MaintenanceOperation value) => value switch
    {
        MaintenanceOperation.Inspection => "inspection",
        MaintenanceOperation.Replacement => "replacement",
        MaintenanceOperation.Repair => "repair",
        MaintenanceOperation.Adjustment => "adjustment",
        MaintenanceOperation.Overhaul => "overhaul",
        MaintenanceOperation.Cleaning => "cleaning",
        MaintenanceOperation.Diagnosis => "diagnosis",
        MaintenanceOperation.Fabrication => "fabrication",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out MaintenanceOperation value)
    {
        foreach (var candidate in AllCases)
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>1件の整備で対象にした部品と作業内容です。</summary>
public sealed record MaintenanceWorkItem
{
    /// <summary>作業項目の安定IDです。</summary>
    public Guid Id { get; }
    /// <summary>選択した消耗品または主要部品です。</summary>
    public MaintenanceComponent Component { get; init; }
    /// <summary>実施した作業種別です。</summary>
    public MaintenanceOperation Operation { get; init; }
    /// <summary>使用または交換した部品名です。</summary>
    public string PartName { get; init; }
    /// <summary>こだわりを残す製品名です。</summary>
    public string ProductName { get; init; }
    /// <summary>製品メーカーです。</summary>
    public string Manufacturer { get; init; }
    /// <summary>粘度、認証、型番などの仕様です。</summary>
    public string Specification { get; init; }
    /// <summary>使用数量です。</summary>
    public double? Quantity { get; init; }
    /// <summary>数量の単位です。</summary>
    public string Unit { get; init; }
    /// <summary>項目固有の詳しい作業記録です。</summary>
    public string Notes { get; init; }

    /// <summary>選択部品と作業種別から作業項目を生成します。</summary>
    /// <remarks>責務: 1件の整備対象に製品情報と作業メモを関連付けます。</remarks>
    /// <param name="component">選択した消耗品または主要部品。</param>
    /// <param name="operation">実施した作業種別。</param>
    /// <param name="id">作業項目の安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="partName">使用または交換した部品名。</param>
    /// <param name="productName">製品名。</param>
    /// <param name="manufacturer">製品メーカー。</param>
    /// <param name="specification">仕様。</param>
    /// <param name="quantity">使用数量。</param>
    /// <param name="unit">数量の単位。</param>
    /// <param name="notes">項目固有の作業記録。</param>
    public MaintenanceWorkItem(
        MaintenanceComponent component,
        MaintenanceOperation operation,
        Guid? id = null,
        string partName = "",
        string productName = "",
        string manufacturer = "",
        string specification = "",
        double? quantity = null,
        string unit = "",
        string notes = "")
    {
        Id = id ?? Guid.NewGuid();
        Component = component;
        Operation = operation;
        PartName = partName;
        ProductName = productName;
        Manufacturer = manufacturer;
        Specification = specification;
        Quantity = quantity;
        Unit = unit;
        Notes = notes;
    }
}

/// <summary>整備記録に添付する写真エビデンスです。</summary>
public sealed record MaintenancePhoto
{
    /// <summary>写真を同期先でも識別する安定IDです。</summary>
    public Guid Id { get; }
    /// <summary>写真の画像データです。</summary>
    public byte[] Data { get; }
    /// <summary>写真が示す内容の説明です。</summary>
    public string Caption { get; init; }
    /// <summary>写真を記録へ追加した日時です。</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>画像データを説明付きの整備エビデンスへ変換します。</summary>
    /// <remarks>責務: 1枚の画像を追跡可能な整備写真として生成します。</remarks>
    /// <param name="data">写真の画像データ。</param>
    /// <param name="id">写真の安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="caption">写真が示す内容の説明。</param>
    /// <param name="capturedAt">写真を記録へ追加した日時。<see langword="null"/> なら現在時刻です。</param>
    public MaintenancePhoto(byte[] data, Guid? id = null, string caption = "", DateTimeOffset? capturedAt = null)
    {
        Id = id ?? Guid.NewGuid();
        Data = data.ToArray();
        Caption = caption;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>画像データを含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の <c>Data</c> と同じく画像データをバイトごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(MaintenancePhoto? other) =>
        other is not null
        && Id == other.Id
        && Data.AsSpan().SequenceEqual(other.Data)
        && string.Equals(Caption, other.Caption, StringComparison.Ordinal)
        && CapturedAt == other.CapturedAt;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 画像データのバイトを含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.AddBytes(Data);
        hash.Add(Caption, StringComparer.Ordinal);
        hash.Add(CapturedAt);
        return hash.ToHashCode();
    }
}

/// <summary>1本の締結部品に対する組立時エビデンスです。</summary>
public sealed record FastenerInstallationEvidence
{
    /// <summary>締結証跡の安定IDです。</summary>
    public Guid Id { get; }
    /// <summary>図面、写真、現物と照合できる取付位置です。</summary>
    public string Position { get; init; }
    /// <summary>実際に締め付けたトルク値です。</summary>
    public double? TorqueNewtonMeters { get; init; }
    /// <summary>使用した工具またはトルクレンチです。</summary>
    public string Tool { get; init; }
    /// <summary>締結を実施した担当者です。</summary>
    public string TightenedBy { get; init; }
    /// <summary>締結を実施した日時です。</summary>
    public DateTimeOffset? TightenedAt { get; init; }
    /// <summary>締結状態を示す写真IDです。</summary>
    public IReadOnlyList<Guid> PhotoIDs { get; init; }
    /// <summary>個別締結に関する補足です。</summary>
    public string Notes { get; init; }

    /// <summary>位置を起点に個別締結証跡を生成します。</summary>
    /// <remarks>責務: 1本の締結位置へトルクと実施情報を関連付けます。</remarks>
    /// <param name="position">取付位置。</param>
    /// <param name="id">締結証跡の安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="torqueNewtonMeters">締め付けトルク値。</param>
    /// <param name="tool">使用した工具。</param>
    /// <param name="tightenedBy">締結担当者。</param>
    /// <param name="tightenedAt">締結日時。</param>
    /// <param name="photoIDs">締結状態を示す写真ID。<see langword="null"/> なら空です。</param>
    /// <param name="notes">補足。</param>
    public FastenerInstallationEvidence(
        string position,
        Guid? id = null,
        double? torqueNewtonMeters = null,
        string tool = "",
        string tightenedBy = "",
        DateTimeOffset? tightenedAt = null,
        IReadOnlyList<Guid>? photoIDs = null,
        string notes = "")
    {
        Id = id ?? Guid.NewGuid();
        Position = position;
        TorqueNewtonMeters = torqueNewtonMeters;
        Tool = tool;
        TightenedBy = tightenedBy;
        TightenedAt = tightenedAt;
        PhotoIDs = photoIDs?.ToArray() ?? [];
        Notes = notes;
    }

    /// <summary>写真ID列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく写真ID列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(FastenerInstallationEvidence? other) =>
        other is not null
        && Id == other.Id
        && string.Equals(Position, other.Position, StringComparison.Ordinal)
        && TorqueNewtonMeters == other.TorqueNewtonMeters
        && string.Equals(Tool, other.Tool, StringComparison.Ordinal)
        && string.Equals(TightenedBy, other.TightenedBy, StringComparison.Ordinal)
        && TightenedAt == other.TightenedAt
        && PhotoIDs.SequenceEqual(other.PhotoIDs)
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 写真ID列の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Position, StringComparer.Ordinal);
        hash.Add(TorqueNewtonMeters);
        hash.Add(Tool, StringComparer.Ordinal);
        hash.Add(TightenedBy, StringComparer.Ordinal);
        hash.Add(TightenedAt);
        foreach (var photoID in PhotoIDs)
        {
            hash.Add(photoID);
        }
        hash.Add(Notes, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>分解時のネジ本数と組立時の個別締結証跡をまとめます。</summary>
public sealed record MaintenanceFastenerGroup
{
    /// <summary>締結グループの安定IDです。</summary>
    public Guid Id { get; }
    /// <summary>エンジン部位やカバー名などのグループ名称です。</summary>
    public string Name { get; init; }
    /// <summary>車両上の分解位置です。</summary>
    public string Location { get; init; }
    /// <summary>分解前に想定した本数です。</summary>
    public int? ExpectedCount { get; init; }
    /// <summary>分解時に実際に回収した本数です。</summary>
    public int RemovedCount { get; init; }
    /// <summary>分解順序、保管場所、異常などの記録です。</summary>
    public string RemovalNotes { get; init; }
    /// <summary>分解状態を示す写真IDです。</summary>
    public IReadOnlyList<Guid> RemovalPhotoIDs { get; init; }
    /// <summary>組立時に1本ずつ残す締結証跡です。</summary>
    public IReadOnlyList<FastenerInstallationEvidence> Installations { get; init; }

    /// <summary>任意のエンジン構成に対応する可変締結グループを生成します。</summary>
    /// <remarks>責務: 1か所の分解本数と個別組立証跡を追跡可能にします。</remarks>
    /// <param name="name">グループ名称。</param>
    /// <param name="id">締結グループの安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="location">分解位置。</param>
    /// <param name="expectedCount">分解前に想定した本数。</param>
    /// <param name="removedCount">分解時に回収した本数。</param>
    /// <param name="removalNotes">分解時の記録。</param>
    /// <param name="removalPhotoIDs">分解状態を示す写真ID。<see langword="null"/> なら空です。</param>
    /// <param name="installations">個別締結証跡。<see langword="null"/> なら空です。</param>
    public MaintenanceFastenerGroup(
        string name,
        Guid? id = null,
        string location = "",
        int? expectedCount = null,
        int removedCount = 0,
        string removalNotes = "",
        IReadOnlyList<Guid>? removalPhotoIDs = null,
        IReadOnlyList<FastenerInstallationEvidence>? installations = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Location = location;
        ExpectedCount = expectedCount;
        RemovedCount = removedCount;
        RemovalNotes = removalNotes;
        RemovalPhotoIDs = removalPhotoIDs?.ToArray() ?? [];
        Installations = installations?.ToArray() ?? [];
    }

    /// <summary>写真ID列と締結証跡列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく配列項目を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(MaintenanceFastenerGroup? other) =>
        other is not null
        && Id == other.Id
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Location, other.Location, StringComparison.Ordinal)
        && ExpectedCount == other.ExpectedCount
        && RemovedCount == other.RemovedCount
        && string.Equals(RemovalNotes, other.RemovalNotes, StringComparison.Ordinal)
        && RemovalPhotoIDs.SequenceEqual(other.RemovalPhotoIDs)
        && Installations.SequenceEqual(other.Installations);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 配列項目の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Location, StringComparer.Ordinal);
        hash.Add(ExpectedCount);
        hash.Add(RemovedCount);
        hash.Add(RemovalNotes, StringComparer.Ordinal);
        foreach (var photoID in RemovalPhotoIDs)
        {
            hash.Add(photoID);
        }
        foreach (var installation in Installations)
        {
            hash.Add(installation);
        }
        return hash.ToHashCode();
    }
}

/// <summary>1台の登録車両へ必ず関連付く軽整備または重整備の記録です。</summary>
public sealed record MaintenanceRecord
{
    /// <summary>整備記録の安定IDです。</summary>
    public MaintenanceRecordID Id { get; }
    /// <summary>記録対象となる登録車両の安定IDです。</summary>
    public VehicleID VehicleID { get; }
    /// <summary>軽整備または重整備の区分です。</summary>
    public MaintenanceKind Kind { get; }
    /// <summary>一覧で作業を識別する表題です。</summary>
    public string Title { get; init; }
    /// <summary>整備を実施した日時です。</summary>
    public DateTimeOffset PerformedAt { get; init; }
    /// <summary>整備時点の走行距離です。</summary>
    public double? OdometerKilometers { get; init; }
    /// <summary>作業全体の詳しい記録です。</summary>
    public string Notes { get; init; }
    /// <summary>対象部品と実施作業です。</summary>
    public IReadOnlyList<MaintenanceWorkItem> WorkItems { get; init; }
    /// <summary>作業前後や部品状態を残す写真です。</summary>
    public IReadOnlyList<MaintenancePhoto> Photos { get; init; }
    /// <summary>オーバーホール時の分解・締結追跡です。</summary>
    public IReadOnlyList<MaintenanceFastenerGroup> FastenerGroups { get; init; }
    /// <summary>記録を作成した日時です。</summary>
    public DateTimeOffset CreatedAt { get; }
    /// <summary>最後に記録内容を更新した日時です。</summary>
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>全端末へ削除を伝播する墓石日時です。</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>車両IDを必須として整備記録を生成します。</summary>
    /// <remarks>責務: 1台の登録車両へ整備内容と証跡を関連付けます。</remarks>
    /// <param name="vehicleID">記録対象となる登録車両の安定ID。</param>
    /// <param name="kind">軽整備または重整備の区分。</param>
    /// <param name="title">一覧で作業を識別する表題。</param>
    /// <param name="id">整備記録の安定ID。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="performedAt">整備を実施した日時。<see langword="null"/> なら現在時刻です。</param>
    /// <param name="odometerKilometers">整備時点の走行距離。</param>
    /// <param name="notes">作業全体の記録。</param>
    /// <param name="workItems">対象部品と実施作業。<see langword="null"/> なら空です。</param>
    /// <param name="photos">写真。<see langword="null"/> なら空です。</param>
    /// <param name="fastenerGroups">分解・締結追跡。<see langword="null"/> なら空です。</param>
    /// <param name="createdAt">記録を作成した日時。<see langword="null"/> なら現在時刻です。</param>
    /// <param name="updatedAt">最後に記録内容を更新した日時。<see langword="null"/> なら現在時刻です。</param>
    /// <param name="deletedAt">削除の墓石日時。</param>
    public MaintenanceRecord(
        VehicleID vehicleID,
        MaintenanceKind kind,
        string title,
        MaintenanceRecordID? id = null,
        DateTimeOffset? performedAt = null,
        double? odometerKilometers = null,
        string notes = "",
        IReadOnlyList<MaintenanceWorkItem>? workItems = null,
        IReadOnlyList<MaintenancePhoto>? photos = null,
        IReadOnlyList<MaintenanceFastenerGroup>? fastenerGroups = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? deletedAt = null)
    {
        Id = id ?? MaintenanceRecordID.New();
        VehicleID = vehicleID;
        Kind = kind;
        Title = title;
        PerformedAt = performedAt ?? DateTimeOffset.UtcNow;
        OdometerKilometers = odometerKilometers;
        Notes = notes;
        WorkItems = workItems?.ToArray() ?? [];
        Photos = photos?.ToArray() ?? [];
        FastenerGroups = fastenerGroups?.ToArray() ?? [];
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
        DeletedAt = deletedAt;
    }

    /// <summary>作業項目、写真、締結グループを含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく配列項目を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(MaintenanceRecord? other) =>
        other is not null
        && Id.Equals(other.Id)
        && VehicleID.Equals(other.VehicleID)
        && Kind == other.Kind
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && PerformedAt == other.PerformedAt
        && OdometerKilometers == other.OdometerKilometers
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal)
        && WorkItems.SequenceEqual(other.WorkItems)
        && Photos.SequenceEqual(other.Photos)
        && FastenerGroups.SequenceEqual(other.FastenerGroups)
        && CreatedAt == other.CreatedAt
        && UpdatedAt == other.UpdatedAt
        && DeletedAt == other.DeletedAt;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 配列項目の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(VehicleID);
        hash.Add(Kind);
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(PerformedAt);
        hash.Add(OdometerKilometers);
        hash.Add(Notes, StringComparer.Ordinal);
        foreach (var workItem in WorkItems)
        {
            hash.Add(workItem);
        }
        foreach (var photo in Photos)
        {
            hash.Add(photo);
        }
        foreach (var group in FastenerGroups)
        {
            hash.Add(group);
        }
        hash.Add(CreatedAt);
        hash.Add(UpdatedAt);
        hash.Add(DeletedAt);
        return hash.ToHashCode();
    }
}
