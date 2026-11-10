using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.UseCases;

/// <summary>車両別の保存済み対応PIDを再利用し、未収集時だけService 01ビットマップを探索します。</summary>
public sealed class LoadVehiclePIDCapabilitiesUseCase
{
    /// <summary>車両別対応PIDの永続化境界です。</summary>
    private readonly IVehiclePIDCapabilityRepository repository;

    /// <summary>Service 01対応ビットマップを読み取る通信境界です。</summary>
    private readonly IOBDPIDTelemetryPort telemetry;

    /// <summary>車種専用定義を取得するPIDカタログ境界です。</summary>
    private readonly IOBDPIDDefinitionRepository? definitionRepository;

    /// <summary>対応確認日時を供給するクロックです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>永続化、通信、時刻境界を固定します。</summary>
    /// <remarks>責務: 車両別対応PIDの再利用または初回探索に必要な境界を保持します。</remarks>
    /// <param name="repository">車両別対応PID保存先。</param>
    /// <param name="telemetry">対応ビットマップ読取境界。</param>
    /// <param name="definitionRepository">車種専用PID定義の読込先。</param>
    /// <param name="now">対応確認日時の供給元。<c>null</c> なら現在時刻。</param>
    public LoadVehiclePIDCapabilitiesUseCase(
        IVehiclePIDCapabilityRepository repository,
        IOBDPIDTelemetryPort telemetry,
        IOBDPIDDefinitionRepository? definitionRepository = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository;
        this.telemetry = telemetry;
        this.definitionRepository = definitionRepository;
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>保存済み設定を返し、0件の場合だけ00から連鎖する0x20刻みの範囲を探索します。</summary>
    /// <remarks>責務: 1台の車両を収集選択付き対応PID一覧へ変換します。</remarks>
    /// <param name="vehicleID">対応PIDを取得するアプリ内車両ID。</param>
    /// <param name="endpoint">未収集時に探索へ使用するOBD終端。</param>
    /// <param name="vehicleModelCode">車種専用PID探索を許可する確認済み型式。</param>
    /// <returns>保存済みまたは新規登録した対応PID設定。</returns>
    /// <exception cref="Exception">永続化、通信、またはビットマップ応答が不正な場合のエラー。</exception>
    public async Task<IReadOnlyList<VehiclePIDCapability>> Execute(
        VehicleID vehicleID,
        OBDConnectionEndpoint endpoint,
        string? vehicleModelCode = null)
    {
        var stored = repository.Capabilities(vehicleID);
        var capabilities = stored.ToList();
        if (stored.Count == 0)
        {
            var requests = await Discover(endpoint);
            var discoveredAt = now();
            capabilities = requests
                .Select(request => new VehiclePIDCapability(vehicleID, request, isCollectionEnabled: true, discoveredAt: discoveredAt))
                .ToList();
            repository.InsertInitial(capabilities, vehicleID);
        }

        var added = await DiscoverVehicleSpecific(
            vehicleID,
            vehicleModelCode,
            capabilities.Select(capability => capability.Id.Request).ToHashSet(),
            endpoint);
        if (added.Count != 0)
        {
            repository.MergeDiscovered(added, vehicleID);
            capabilities.AddRange(added);
        }

        return capabilities
            .OrderBy(capability => capability.Id.Request.Service)
            .ThenBy(capability => capability.Id.Request.Pid)
            .ToList();
    }

    /// <summary>型式一致する専用PIDを個別送信し、応答済みだけを対応登録候補にします。</summary>
    /// <remarks>責務: 1台の型式一致車両を応答確認済み専用PID設定へ変換します。</remarks>
    /// <param name="vehicleID">登録先の車両ID。</param>
    /// <param name="vehicleModelCode">接続前識別で確認した型式。</param>
    /// <param name="excluding">既に登録済みのService/PID。</param>
    /// <param name="endpoint">探索へ使用するOBD終端。</param>
    /// <returns>今回初めて正応答を確認できた専用PID設定。</returns>
    /// <exception cref="Exception">定義読込または通信境界に失敗した場合のエラー。</exception>
    private async Task<List<VehiclePIDCapability>> DiscoverVehicleSpecific(
        VehicleID vehicleID,
        string? vehicleModelCode,
        IReadOnlySet<OBDPIDRequest> excluding,
        OBDConnectionEndpoint endpoint)
    {
        if (vehicleModelCode is null || definitionRepository is null)
        {
            return [];
        }

        var definitions = definitionRepository.Definitions()
            .Where(definition =>
                definition.VehicleModelCode is not null
                && string.Equals(definition.VehicleModelCode, vehicleModelCode, StringComparison.OrdinalIgnoreCase)
                && !excluding.Contains(new OBDPIDRequest(definition.Service, definition.Pid)))
            .ToList();
        if (definitions.Count == 0)
        {
            return [];
        }

        var responses = await telemetry.ReadVehicleSpecific(definitions, endpoint);
        var discoveredAt = now();
        var result = new List<VehiclePIDCapability>();
        foreach (var definition in definitions)
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            if (!responses.ContainsKey(request))
            {
                continue;
            }

            result.Add(new VehiclePIDCapability(vehicleID, request, isCollectionEnabled: true, discoveredAt: discoveredAt));
        }

        return result;
    }

    /// <summary>Service 01の対応範囲ビットマップを継続ビットに従って読み取ります。</summary>
    /// <remarks>責務: 00からE0までの0x20刻み照会連鎖を対応データPID集合へ変換します。</remarks>
    /// <param name="endpoint">探索へ使用するOBD終端。</param>
    /// <returns>対応が通知されたデータPIDの昇順一覧。</returns>
    /// <exception cref="OBDPIDTelemetryException">通信失敗または4バイト未満の応答の場合のエラー。</exception>
    private async Task<List<OBDPIDRequest>> Discover(OBDConnectionEndpoint endpoint)
    {
        var supported = new List<OBDPIDRequest>();
        byte @base = 0x00;
        while (@base <= 0xE0)
        {
            var query = new OBDPIDRequest(0x01, @base);
            var response = await telemetry.Read([query], endpoint);
            if (!response.TryGetValue(query, out var bytes) || bytes.Count < 4)
            {
                throw new OBDPIDTelemetryException(OBDPIDTelemetryError.NoVehicleResponse);
            }

            for (var offset = 1; offset <= 32; offset++)
            {
                if (!BitIsSet(offset, bytes))
                {
                    continue;
                }

                var value = @base + offset;
                if (value > 0xFF || value % 0x20 == 0)
                {
                    continue;
                }

                supported.Add(new OBDPIDRequest(0x01, (byte)value));
            }

            if (!BitIsSet(32, bytes) || @base >= 0xE0)
            {
                break;
            }

            @base = unchecked((byte)(@base + 0x20));
        }

        return supported;
    }

    /// <summary>4バイト対応ビットマップ内のPID位置を判定します。</summary>
    /// <remarks>責務: 1始まりのPIDオフセットをbig-endianビットマップの真偽へ変換します。</remarks>
    /// <param name="offset">1から32のPIDオフセット。</param>
    /// <param name="bytes">対応ビットマップ先頭4バイト。</param>
    /// <returns>対応ビットが立っている場合は <c>true</c>。</returns>
    private static bool BitIsSet(int offset, IReadOnlyList<byte> bytes)
    {
        var zeroBased = offset - 1;
        return (bytes[zeroBased / 8] & (0x80 >> (zeroBased % 8))) != 0;
    }
}
