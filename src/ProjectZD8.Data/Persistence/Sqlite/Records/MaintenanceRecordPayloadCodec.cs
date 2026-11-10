using System.Text.Json;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Records;

/// <summary>整備記録をSQLiteの <c>payload</c> 列に入れる内部JSONへ相互変換します。</summary>
/// <remarks>
/// iOS版は <c>JSONEncoder</c> で完全な整備記録を保存します。Windows版のJSONはこの端末の内部保存専用で、
/// CloudKitのJSON互換（Data/Mapping の担当）とは無関係です。日時はUTCのTicks、写真はbase64です。
/// </remarks>
internal static class MaintenanceRecordPayloadCodec
{
    /// <summary>内部JSONの直列化設定です。</summary>
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>整備記録を内部JSONへ符号化します。</summary>
    /// <remarks>責務: 1件の整備記録を写真と締結証跡を含む完全なJSONへ変換します。</remarks>
    /// <param name="record">符号化する整備記録。</param>
    /// <returns>UTF-8のJSON。</returns>
    internal static byte[] Encode(MaintenanceRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(RecordPayload.From(record), Options);

    /// <summary>内部JSONを整備記録へ復号します。</summary>
    /// <remarks>責務: 1件の保存JSONをDomain整備記録へ復元します。</remarks>
    /// <param name="payload">UTF-8のJSON。</param>
    /// <returns>復元した整備記録。</returns>
    /// <exception cref="InvalidDataException">JSONまたは列挙値が不正な場合。</exception>
    internal static MaintenanceRecord Decode(byte[] payload)
    {
        try
        {
            var decoded = JsonSerializer.Deserialize<RecordPayload>(payload, Options)
                ?? throw new InvalidDataException("Maintenance payload is empty");
            return decoded.ToDomain();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Maintenance payload is invalid", error);
        }
    }

    /// <summary>raw valueを列挙値へ変換します。</summary>
    /// <remarks>責務: 保存済み文字列を検証して列挙値へ戻します。</remarks>
    /// <typeparam name="T">列挙型。</typeparam>
    /// <param name="parsed">変換に成功したか。</param>
    /// <param name="value">変換結果。</param>
    /// <returns>変換結果。</returns>
    /// <exception cref="InvalidDataException">未知のraw valueの場合。</exception>
    private static T Require<T>(bool parsed, T value) =>
        parsed ? value : throw new InvalidDataException("Maintenance payload contains an unknown raw value");

    /// <summary>整備記録の内部JSON表現です。</summary>
    private sealed class RecordPayload
    {
        /// <summary>整備記録IDです。</summary>
        public Guid Id { get; set; }
        /// <summary>車両IDです。</summary>
        public Guid VehicleID { get; set; }
        /// <summary>整備区分のraw valueです。</summary>
        public string Kind { get; set; } = "";
        /// <summary>題名です。</summary>
        public string Title { get; set; } = "";
        /// <summary>実施日時のTicksです。</summary>
        public long PerformedAt { get; set; }
        /// <summary>走行距離です。</summary>
        public double? OdometerKilometers { get; set; }
        /// <summary>メモです。</summary>
        public string Notes { get; set; } = "";
        /// <summary>作業項目です。</summary>
        public List<WorkItemPayload> WorkItems { get; set; } = [];
        /// <summary>写真です。</summary>
        public List<PhotoPayload> Photos { get; set; } = [];
        /// <summary>締結部品群です。</summary>
        public List<FastenerGroupPayload> FastenerGroups { get; set; } = [];
        /// <summary>作成日時のTicksです。</summary>
        public long CreatedAt { get; set; }
        /// <summary>更新日時のTicksです。</summary>
        public long UpdatedAt { get; set; }
        /// <summary>削除墓石日時のTicksです。</summary>
        public long? DeletedAt { get; set; }

        /// <summary>Domain値から内部表現を作ります。</summary>
        /// <remarks>責務: 1件の整備記録を内部JSON表現へ写像します。</remarks>
        /// <param name="record">写像する整備記録。</param>
        /// <returns>内部表現。</returns>
        public static RecordPayload From(MaintenanceRecord record) => new()
        {
            Id = record.Id.RawValue,
            VehicleID = record.VehicleID.RawValue,
            Kind = record.Kind.ToRawValue(),
            Title = record.Title,
            PerformedAt = record.PerformedAt.UtcTicks,
            OdometerKilometers = record.OdometerKilometers,
            Notes = record.Notes,
            WorkItems = record.WorkItems.Select(WorkItemPayload.From).ToList(),
            Photos = record.Photos.Select(PhotoPayload.From).ToList(),
            FastenerGroups = record.FastenerGroups.Select(FastenerGroupPayload.From).ToList(),
            CreatedAt = record.CreatedAt.UtcTicks,
            UpdatedAt = record.UpdatedAt.UtcTicks,
            DeletedAt = record.DeletedAt?.UtcTicks,
        };

        /// <summary>Domain値へ戻します。</summary>
        /// <remarks>責務: 内部表現を1件の整備記録へ写像します。</remarks>
        /// <returns>整備記録。</returns>
        public MaintenanceRecord ToDomain() => new(
            vehicleID: new VehicleID(VehicleID),
            kind: Require(MaintenanceKindRawValue.TryParse(Kind, out var kind), kind),
            title: Title,
            id: new MaintenanceRecordID(Id),
            performedAt: SqliteValueConversion.Date(PerformedAt),
            odometerKilometers: OdometerKilometers,
            notes: Notes,
            workItems: WorkItems.Select(item => item.ToDomain()).ToArray(),
            photos: Photos.Select(photo => photo.ToDomain()).ToArray(),
            fastenerGroups: FastenerGroups.Select(group => group.ToDomain()).ToArray(),
            createdAt: SqliteValueConversion.Date(CreatedAt),
            updatedAt: SqliteValueConversion.Date(UpdatedAt),
            deletedAt: DeletedAt is { } deleted ? SqliteValueConversion.Date(deleted) : null);
    }

    /// <summary>作業項目の内部JSON表現です。</summary>
    private sealed class WorkItemPayload
    {
        /// <summary>作業項目IDです。</summary>
        public Guid Id { get; set; }
        /// <summary>部位のraw valueです。</summary>
        public string Component { get; set; } = "";
        /// <summary>作業種別のraw valueです。</summary>
        public string Operation { get; set; } = "";
        /// <summary>部品名です。</summary>
        public string PartName { get; set; } = "";
        /// <summary>製品名です。</summary>
        public string ProductName { get; set; } = "";
        /// <summary>メーカーです。</summary>
        public string Manufacturer { get; set; } = "";
        /// <summary>規格です。</summary>
        public string Specification { get; set; } = "";
        /// <summary>数量です。</summary>
        public double? Quantity { get; set; }
        /// <summary>単位です。</summary>
        public string Unit { get; set; } = "";
        /// <summary>メモです。</summary>
        public string Notes { get; set; } = "";

        /// <summary>Domain値から内部表現を作ります。</summary>
        /// <remarks>責務: 1件の作業項目を内部JSON表現へ写像します。</remarks>
        /// <param name="item">写像する作業項目。</param>
        /// <returns>内部表現。</returns>
        public static WorkItemPayload From(MaintenanceWorkItem item) => new()
        {
            Id = item.Id,
            Component = item.Component.ToRawValue(),
            Operation = item.Operation.ToRawValue(),
            PartName = item.PartName,
            ProductName = item.ProductName,
            Manufacturer = item.Manufacturer,
            Specification = item.Specification,
            Quantity = item.Quantity,
            Unit = item.Unit,
            Notes = item.Notes,
        };

        /// <summary>Domain値へ戻します。</summary>
        /// <remarks>責務: 内部表現を1件の作業項目へ写像します。</remarks>
        /// <returns>作業項目。</returns>
        public MaintenanceWorkItem ToDomain() => new(
            component: Require(MaintenanceComponentRawValue.TryParse(Component, out var component), component),
            operation: Require(MaintenanceOperationRawValue.TryParse(Operation, out var operation), operation),
            id: Id,
            partName: PartName,
            productName: ProductName,
            manufacturer: Manufacturer,
            specification: Specification,
            quantity: Quantity,
            unit: Unit,
            notes: Notes);
    }

    /// <summary>写真の内部JSON表現です。</summary>
    private sealed class PhotoPayload
    {
        /// <summary>写真IDです。</summary>
        public Guid Id { get; set; }
        /// <summary>画像データです（base64で保存されます）。</summary>
        public byte[] Data { get; set; } = [];
        /// <summary>説明です。</summary>
        public string Caption { get; set; } = "";
        /// <summary>撮影日時のTicksです。</summary>
        public long CapturedAt { get; set; }

        /// <summary>Domain値から内部表現を作ります。</summary>
        /// <remarks>責務: 1件の写真を内部JSON表現へ写像します。</remarks>
        /// <param name="photo">写像する写真。</param>
        /// <returns>内部表現。</returns>
        public static PhotoPayload From(MaintenancePhoto photo) => new()
        {
            Id = photo.Id,
            Data = photo.Data,
            Caption = photo.Caption,
            CapturedAt = photo.CapturedAt.UtcTicks,
        };

        /// <summary>Domain値へ戻します。</summary>
        /// <remarks>責務: 内部表現を1件の写真へ写像します。</remarks>
        /// <returns>写真。</returns>
        public MaintenancePhoto ToDomain() => new(Data, Id, Caption, SqliteValueConversion.Date(CapturedAt));
    }

    /// <summary>締結証跡の内部JSON表現です。</summary>
    private sealed class InstallationPayload
    {
        /// <summary>証跡IDです。</summary>
        public Guid Id { get; set; }
        /// <summary>締結位置です。</summary>
        public string Position { get; set; } = "";
        /// <summary>トルクです。</summary>
        public double? TorqueNewtonMeters { get; set; }
        /// <summary>工具です。</summary>
        public string Tool { get; set; } = "";
        /// <summary>作業者です。</summary>
        public string TightenedBy { get; set; } = "";
        /// <summary>締結日時のTicksです。</summary>
        public long? TightenedAt { get; set; }
        /// <summary>関連写真IDです。</summary>
        public List<Guid> PhotoIDs { get; set; } = [];
        /// <summary>メモです。</summary>
        public string Notes { get; set; } = "";

        /// <summary>Domain値から内部表現を作ります。</summary>
        /// <remarks>責務: 1件の締結証跡を内部JSON表現へ写像します。</remarks>
        /// <param name="evidence">写像する締結証跡。</param>
        /// <returns>内部表現。</returns>
        public static InstallationPayload From(FastenerInstallationEvidence evidence) => new()
        {
            Id = evidence.Id,
            Position = evidence.Position,
            TorqueNewtonMeters = evidence.TorqueNewtonMeters,
            Tool = evidence.Tool,
            TightenedBy = evidence.TightenedBy,
            TightenedAt = evidence.TightenedAt?.UtcTicks,
            PhotoIDs = evidence.PhotoIDs.ToList(),
            Notes = evidence.Notes,
        };

        /// <summary>Domain値へ戻します。</summary>
        /// <remarks>責務: 内部表現を1件の締結証跡へ写像します。</remarks>
        /// <returns>締結証跡。</returns>
        public FastenerInstallationEvidence ToDomain() => new(
            position: Position,
            id: Id,
            torqueNewtonMeters: TorqueNewtonMeters,
            tool: Tool,
            tightenedBy: TightenedBy,
            tightenedAt: TightenedAt is { } ticks ? SqliteValueConversion.Date(ticks) : null,
            photoIDs: PhotoIDs,
            notes: Notes);
    }

    /// <summary>締結部品群の内部JSON表現です。</summary>
    private sealed class FastenerGroupPayload
    {
        /// <summary>部品群IDです。</summary>
        public Guid Id { get; set; }
        /// <summary>名称です。</summary>
        public string Name { get; set; } = "";
        /// <summary>位置です。</summary>
        public string Location { get; set; } = "";
        /// <summary>予定本数です。</summary>
        public int? ExpectedCount { get; set; }
        /// <summary>取外し本数です。</summary>
        public int RemovedCount { get; set; }
        /// <summary>取外しメモです。</summary>
        public string RemovalNotes { get; set; } = "";
        /// <summary>取外し写真IDです。</summary>
        public List<Guid> RemovalPhotoIDs { get; set; } = [];
        /// <summary>締結証跡です。</summary>
        public List<InstallationPayload> Installations { get; set; } = [];

        /// <summary>Domain値から内部表現を作ります。</summary>
        /// <remarks>責務: 1件の締結部品群を内部JSON表現へ写像します。</remarks>
        /// <param name="group">写像する締結部品群。</param>
        /// <returns>内部表現。</returns>
        public static FastenerGroupPayload From(MaintenanceFastenerGroup group) => new()
        {
            Id = group.Id,
            Name = group.Name,
            Location = group.Location,
            ExpectedCount = group.ExpectedCount,
            RemovedCount = group.RemovedCount,
            RemovalNotes = group.RemovalNotes,
            RemovalPhotoIDs = group.RemovalPhotoIDs.ToList(),
            Installations = group.Installations.Select(InstallationPayload.From).ToList(),
        };

        /// <summary>Domain値へ戻します。</summary>
        /// <remarks>責務: 内部表現を1件の締結部品群へ写像します。</remarks>
        /// <returns>締結部品群。</returns>
        public MaintenanceFastenerGroup ToDomain() => new(
            name: Name,
            id: Id,
            location: Location,
            expectedCount: ExpectedCount,
            removedCount: RemovedCount,
            removalNotes: RemovalNotes,
            removalPhotoIDs: RemovalPhotoIDs,
            installations: Installations.Select(installation => installation.ToDomain()).ToArray());
    }
}
