using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>PID定義リポジトリと初期データを検証します。</summary>
/// <remarks>iOS版 <c>GRDBOBDPIDDefinitionRepositoryTests</c> の移植です。</remarks>
public sealed class SqliteOBDPIDDefinitionRepositoryTests
{
    /// <summary>Migrationと確認済み定義の登録を確かめます。</summary>
    [Fact]
    public void MigratesAndInstallsVerifiedDefinitions()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteOBDPIDDefinitionRepository(database);
        StandardOBDPIDSeed.Install(repository);

        Assert.Equal(StandardOBDPIDSeed.Definitions, repository.Definitions());
        Assert.Equal(167, repository.Definitions().Count);
    }

    /// <summary>標準の走行距離定義を確かめます。</summary>
    [Fact]
    public void InstallsStandardOdometerDefinition()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteOBDPIDDefinitionRepository(database);
        StandardOBDPIDSeed.Install(repository);

        var definition = repository.Definition(0x01, 0xA6);
        Assert.NotNull(definition);
        Assert.Equal("obd.pid.01.A6.name", definition.NameKey);
        Assert.Equal(4, definition.RequiredByteCount);
        Assert.Equal("(A * 16777216 + B * 65536 + C * 256 + D) / 10", definition.Formula);
        Assert.Equal("km", definition.Unit);
        Assert.Equal(0, definition.MinimumValue);
        Assert.Equal(429_496_729.5, definition.MaximumValue);
        Assert.Equal("https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979", definition.SourceURI);
    }

    /// <summary>ZD8専用定義だけが車種スコープ付きで登録されることを確かめます。</summary>
    [Fact]
    public void InstallsOnlyConfirmedZD8DefinitionsWithVehicleScope()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteOBDPIDDefinitionRepository(database);

        ZD8OBDPIDSeed.Install(repository);

        var definitions = repository.Definitions();
        Assert.Equal(ZD8OBDPIDSeed.Definitions, definitions);
        Assert.Equal(new ushort?[] { 0x7E0, 0x7E1 }, definitions.Select(definition => definition.Header));
        Assert.Equal(["ZD8", "ZD8"], definitions.Select(definition => definition.VehicleModelCode));
        Assert.Equal(new byte[] { 0x02, 0x17 }, definitions.Select(definition => definition.Pid));
    }

    /// <summary>車種スコープはヘッダーと型式の両方が必要なことを確かめます。</summary>
    [Fact]
    public void VehicleSpecificScopeRequiresHeaderAndModelCodeTogether()
    {
        using var database = SqliteDatabase.OpenInMemory();
        _ = new SqliteOBDPIDDefinitionRepository(database);

        Assert.ThrowsAny<Exception>(() => InsertVehicleScopeDirect(database, header: 0x7E0, modelCode: null, pid: 0x02));
        Assert.ThrowsAny<Exception>(() => InsertVehicleScopeDirect(database, header: null, modelCode: "ZD8", pid: 0x17));
    }

    /// <summary>逆順に登録してもService/PID順で返ることを確かめます。</summary>
    [Fact]
    public void DefinitionsReturnsAllRecordsInServiceAndPIDOrder()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteOBDPIDDefinitionRepository(database);
        foreach (var definition in StandardOBDPIDSeed.Definitions.Reverse())
        {
            repository.Upsert(definition);
        }

        Assert.Equal(StandardOBDPIDSeed.Definitions, repository.Definitions());
    }

    /// <summary>改訂番号が後退しないことを確かめます。</summary>
    [Fact]
    public void UpsertNeverRegressesRevision()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteOBDPIDDefinitionRepository(database);
        repository.Upsert(MakeDefinition("A", 2));
        repository.Upsert(MakeDefinition("A + 1", 1));
        Assert.Equal("A", repository.Definition(1, 5)?.Formula);

        repository.Upsert(MakeDefinition("A - 40", 3));
        Assert.Equal(3, repository.Definition(1, 5)?.Revision);
        Assert.Equal("A - 40", repository.Definition(1, 5)?.Formula);
    }

    /// <summary>範囲の制約がNULLと組の値を扱うことを確かめます。</summary>
    [Fact]
    public void RangeConstraintHandlesNullAndPairedValues()
    {
        using var database = SqliteDatabase.OpenInMemory();
        _ = new SqliteOBDPIDDefinitionRepository(database);
        InsertDirect(database, minimum: null, maximum: null, pid: 1);
        InsertDirect(database, minimum: -40, maximum: 215, pid: 2);
        Assert.ThrowsAny<Exception>(() => InsertDirect(database, minimum: null, maximum: 10, pid: 3));
        Assert.ThrowsAny<Exception>(() => InsertDirect(database, minimum: -40, maximum: null, pid: 4));
        Assert.ThrowsAny<Exception>(() => InsertDirect(database, minimum: 10, maximum: 0, pid: 5));
    }

    /// <summary>識別子とバイト数の制約が直接SQLも拒否することを確かめます。</summary>
    [Fact]
    public void IdentifierAndByteCountConstraintsRejectDirectSQLBypass()
    {
        using var database = SqliteDatabase.OpenInMemory();
        _ = new SqliteOBDPIDDefinitionRepository(database);
        Assert.ThrowsAny<Exception>(() => InsertRaw(database, service: 256, byteCount: 1));
        Assert.ThrowsAny<Exception>(() => InsertRaw(database, service: 1, byteCount: 0));
    }

    /// <summary>テスト用の定義を作ります。</summary>
    /// <remarks>責務: Service 1 / PID 5 の定義を数式と改訂番号を指定して作ります。</remarks>
    /// <param name="formula">数式。</param>
    /// <param name="revision">改訂番号。</param>
    /// <returns>定義。</returns>
    private static OBDPIDDefinition MakeDefinition(string formula, int revision) =>
        new(service: 1, pid: 5, nameKey: "test", requiredByteCount: 1, formula: formula, unit: "u", minimumValue: null, maximumValue: null, sourceURI: "test://source", revision: revision);

    /// <summary>範囲を指定した行を直接挿入します。</summary>
    /// <remarks>責務: リポジトリを通さずに制約を確かめます。</remarks>
    /// <param name="database">DB。</param>
    /// <param name="minimum">最小値。</param>
    /// <param name="maximum">最大値。</param>
    /// <param name="pid">PID。</param>
    private static void InsertDirect(SqliteDatabase database, double? minimum, double? maximum, int pid) =>
        database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            """
            INSERT INTO obd_pid_definitions
                (service, pid, nameKey, requiredByteCount, formula, unit, minimumValue, maximumValue,
                 sourceURI, revision, summaryKey, highValueKey, lowValueKey, correlationKey)
            VALUES (1, $1, 'x', 1, 'A', '', $2, $3, 's', 1, 's', 'h', 'l', 'c')
            """,
            pid,
            minimum,
            maximum));

    /// <summary>Serviceとバイト数を指定した行を直接挿入します。</summary>
    /// <remarks>責務: リポジトリを通さずに制約を確かめます。</remarks>
    /// <param name="database">DB。</param>
    /// <param name="service">Service。</param>
    /// <param name="byteCount">必要バイト数。</param>
    private static void InsertRaw(SqliteDatabase database, int service, int byteCount) =>
        database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            """
            INSERT INTO obd_pid_definitions
                (service, pid, nameKey, requiredByteCount, formula, unit, minimumValue, maximumValue,
                 sourceURI, revision, summaryKey, highValueKey, lowValueKey, correlationKey)
            VALUES ($1, 1, 'x', $2, 'A', '', NULL, NULL, 's', 1, 's', 'h', 'l', 'c')
            """,
            service,
            byteCount));

    /// <summary>車種スコープを指定した行を直接挿入します。</summary>
    /// <remarks>責務: リポジトリを通さずに制約を確かめます。</remarks>
    /// <param name="database">DB。</param>
    /// <param name="header">ヘッダー。</param>
    /// <param name="modelCode">型式。</param>
    /// <param name="pid">PID。</param>
    private static void InsertVehicleScopeDirect(SqliteDatabase database, int? header, string? modelCode, int pid) =>
        database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            """
            INSERT INTO obd_pid_definitions
                (service, pid, nameKey, requiredByteCount, formula, unit, minimumValue, maximumValue,
                 sourceURI, revision, summaryKey, highValueKey, lowValueKey, correlationKey, header, vehicleModelCode)
            VALUES (33, $1, 'x', 1, 'A', '', NULL, NULL, 's', 1, 's', 'h', 'l', 'c', $2, $3)
            """,
            pid,
            header,
            modelCode));
}
