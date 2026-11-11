using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Data.Windows.Devices.Bluetooth;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.ValueObjects;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace ProjectZD8.Data.Windows.Devices;

/// <summary>BLE のアドバタイズを一定時間受信して、既知のOBDアダプター候補を探索します。</summary>
/// <remarks>
/// iOS 版の <c>AppleCoreBluetoothAdapterDiscovery</c> に対応します。状態確定2秒、スキャン10秒は iOS と同じです。
/// Windows はアドバタイズとスキャン応答を別々に通知するため、アドレスごとに名前と Service UUID を集めてから
/// <see cref="OBDBluetoothCandidatePolicy"/> で判定します。UI スレッドから呼ぶ前提です。
/// </remarks>
public sealed class WindowsBluetoothLEAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>スキャンを続ける時間です。</summary>
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    /// <summary>検出値をDomain候補へ変換します。</summary>
    private readonly BluetoothAdvertisementMapper mapper = new();

    /// <summary>BLE候補を既知OBDアダプターへ限定する判定規則です。</summary>
    private readonly OBDBluetoothCandidatePolicy candidatePolicy = new();

    /// <summary>進行中のスキャンです。</summary>
    private ScanSession? activeScan;

    /// <summary>Bluetooth Low Energy接続方式だけで周辺候補を取得します。</summary>
    /// <remarks>責務: BLE 探索要求を期限付きスキャンへ委譲します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>スキャン期間内に取得できた既知OBD候補。</returns>
    /// <exception cref="AdapterDiscoveryException">USB要求、Bluetooth利用不可の場合。</exception>
    /// <exception cref="OperationCanceledException">新しい探索に置き換えられた場合。</exception>
    public async Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
    {
        if (mode != AdapterTransportMode.Bluetooth)
        {
            throw new AdapterDiscoveryException(AdapterDiscoveryError.TransportUnsupported);
        }

        activeScan?.Cancel();
        var scan = new ScanSession(mapper, candidatePolicy);
        activeScan = scan;
        try
        {
            await WindowsBluetoothAvailability.Ensure(requiresLowEnergy: true);
            return await scan.Run(ScanDuration);
        }
        finally
        {
            if (ReferenceEquals(activeScan, scan))
            {
                activeScan = null;
            }
        }
    }

    /// <summary>1回のスキャンの受信値と完了を保持します。</summary>
    private sealed class ScanSession
    {
        /// <summary>受信値を守るロックです。</summary>
        private readonly Lock gate = new();

        /// <summary>アドレスごとの受信値です（初めて受信した順）。</summary>
        private readonly Dictionary<ulong, Observation> observations = [];

        /// <summary>初めて受信した順のアドレスです。</summary>
        private readonly List<ulong> order = [];

        /// <summary>スキャンの完了です。</summary>
        private readonly TaskCompletionSource<IReadOnlyList<DiscoveredAdapter>> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>検出値の変換規則です。</summary>
        private readonly BluetoothAdvertisementMapper mapper;

        /// <summary>候補の判定規則です。</summary>
        private readonly OBDBluetoothCandidatePolicy policy;

        /// <summary>スキャンを始めた時刻印です（診断ログの所要時間）。</summary>
        private readonly long started = Stopwatch.GetTimestamp();

        /// <summary>使用中の受信器です。</summary>
        private BluetoothLEAdvertisementWatcher? watcher;

        /// <summary>変換規則と判定規則を保持して生成します。</summary>
        /// <remarks>責務: 1回のスキャンの空の状態を構築します。</remarks>
        /// <param name="mapper">検出値の変換規則。</param>
        /// <param name="policy">候補の判定規則。</param>
        public ScanSession(BluetoothAdvertisementMapper mapper, OBDBluetoothCandidatePolicy policy)
        {
            this.mapper = mapper;
            this.policy = policy;
        }

        /// <summary>スキャンを指定時間続けて候補を返します。</summary>
        /// <remarks>責務: 受信器を開始し、期限、エラー停止、取消のいずれかで1回だけ完了します。</remarks>
        /// <param name="duration">スキャンを続ける時間。</param>
        /// <returns>既知OBD候補。</returns>
        public async Task<IReadOnlyList<DiscoveredAdapter>> Run(TimeSpan duration)
        {
            if (completion.Task.IsCompleted)
            {
                return await completion.Task;
            }

            var created = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            created.Received += OnReceived;
            created.Stopped += OnStopped;
            watcher = created;
            try
            {
                created.Start();
            }
            catch (Exception error)
            {
                OBDDiscoveryLog.StepFailed(OBDDiscoveryLog.Logger, error, "ble-watcher-start");
                Finish(new AdapterDiscoveryException(AdapterDiscoveryError.BluetoothStateUnavailable));
                return await completion.Task;
            }

            if (await Task.WhenAny(completion.Task, Task.Delay(duration)) != completion.Task)
            {
                Finish(null);
            }

            return await completion.Task;
        }

        /// <summary>新しい探索に置き換えられたため取消で完了します。</summary>
        /// <remarks>責務: 以前の BLE 探索を確実に終了します。</remarks>
        public void Cancel() => Finish(new OperationCanceledException());

        /// <summary>1件のアドバタイズを受信値へ反映します。</summary>
        /// <remarks>責務: 名前、Service UUID、Manufacturer Data の有無をアドレスごとに集めます。</remarks>
        /// <param name="sender">受信器。</param>
        /// <param name="args">受信したアドバタイズ。</param>
        private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var advertisement = args.Advertisement;
            var localName = advertisement.LocalName;
            var services = advertisement.ServiceUuids.Select(BluetoothUUIDText.Format).ToArray();
            var hasManufacturerData = advertisement.ManufacturerData.Count > 0;
            lock (gate)
            {
                if (!observations.TryGetValue(args.BluetoothAddress, out var observation))
                {
                    observation = new Observation();
                    observations[args.BluetoothAddress] = observation;
                    order.Add(args.BluetoothAddress);
                }

                if (!string.IsNullOrWhiteSpace(localName))
                {
                    observation.LocalName = localName;
                }

                observation.Services.UnionWith(services);
                observation.HasManufacturerData |= hasManufacturerData;
            }
        }

        /// <summary>受信器が止まったときにエラーなら探索を失敗で完了します。</summary>
        /// <remarks>責務: 受信器のエラーを区別可能な探索エラーへ変換します。</remarks>
        /// <param name="sender">受信器。</param>
        /// <param name="args">停止理由。</param>
        private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            var error = args.Error switch
            {
                BluetoothError.Success => (AdapterDiscoveryError?)null,
                BluetoothError.RadioNotAvailable => AdapterDiscoveryError.BluetoothPoweredOff,
                BluetoothError.DisabledByPolicy or BluetoothError.DisabledByUser or BluetoothError.ConsentRequired =>
                    AdapterDiscoveryError.BluetoothUnauthorized,
                BluetoothError.NotSupported => AdapterDiscoveryError.BluetoothUnsupported,
                _ => AdapterDiscoveryError.BluetoothStateUnavailable,
            };
            if (error is { } reason)
            {
                OBDDiscoveryLog.BluetoothLEScanStopped(OBDDiscoveryLog.Logger, args.Error.ToString());
                Finish(new AdapterDiscoveryException(reason));
            }
        }

        /// <summary>スキャンを停止して待機中の呼び出しへ結果を返します。</summary>
        /// <remarks>責務: 1回のBLE探索に属する受信器を止め、単一結果で完了します。</remarks>
        /// <param name="error">失敗で完了する場合のエラー。成功なら <c>null</c>。</param>
        private void Finish(Exception? error)
        {
            var active = Interlocked.Exchange(ref watcher, null);
            if (active is not null)
            {
                active.Received -= OnReceived;
                active.Stopped -= OnStopped;
                try
                {
                    active.Stop();
                }
                catch (Exception stopError)
                {
                    // 既に止まっている受信器では失敗することがあります。
                    OBDDiscoveryLog.StepFailed(OBDDiscoveryLog.Logger, stopError, "ble-watcher-stop");
                }
            }

            if (error is not null)
            {
                completion.TrySetException(error);
                return;
            }

            completion.TrySetResult(MakeAdapters());
        }

        /// <summary>集めた受信値を既知OBD候補へ変換します。</summary>
        /// <remarks>責務: アドレスごとの受信値を候補へ写像し、判定規則で絞り込みます。</remarks>
        /// <returns>初めて受信した順の候補。</returns>
        private IReadOnlyList<DiscoveredAdapter> MakeAdapters()
        {
            lock (gate)
            {
                var candidates = order
                    .Select(address => (Address: address, Observation: observations[address]))
                    .Select(entry => mapper.MakeAdapter(
                        BluetoothAddressText.Format(entry.Address),
                        entry.Observation.LocalName,
                        peripheralName: null,
                        DiscoveredAdapterConnectionState.Disconnected,
                        entry.Observation.HasManufacturerData,
                        entry.Observation.Services.ToArray()))
                    .ToArray();
                var accepted = candidates.Where(policy.Accepts).ToArray();
                LogScan(candidates, accepted.Length);
                return accepted;
            }
        }

        /// <summary>スキャンで見えた機器ごとの判断と、件数の要約を書きます。</summary>
        /// <remarks>
        /// 責務: 既知の名前・既知の UART Service のどちらで候補にしたか、どちらもなくて外したかを Debug で残します。
        /// アドレスは末尾 2 バイトだけにします。名前のない機器は件数だけに数えます（周りの機器が多いと行が増えるため）。
        /// </remarks>
        /// <param name="candidates">見えた機器を候補の形にしたもの。</param>
        /// <param name="acceptedCount">候補にした件数。</param>
        private void LogScan(IReadOnlyList<DiscoveredAdapter> candidates, int acceptedCount)
        {
            var logger = OBDDiscoveryLog.Logger;
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                var device = Redact.BluetoothAddress(candidate.SystemIdentifier);
                if (policy.Accepts(candidate))
                {
                    OBDDiscoveryLog.CandidateAccepted(
                        logger,
                        OBDDiagnosticText.TransportBLE,
                        candidate.DisplayName,
                        device,
                        candidate.BluetoothServiceIdentifiers.Count > 0
                            ? $"services={string.Join(',', candidate.BluetoothServiceIdentifiers)}"
                            : "known-name");
                }
                else if (!string.IsNullOrWhiteSpace(candidate.AdvertisementLocalName))
                {
                    OBDDiscoveryLog.CandidateRejected(
                        logger,
                        OBDDiagnosticText.TransportBLE,
                        candidate.DisplayName,
                        device,
                        $"unknown-name-and-services（services={candidate.BluetoothServiceIdentifiers.Count}）");
                }
            }

            OBDDiscoveryLog.SourceCompleted(
                logger,
                OBDDiagnosticText.TransportBLE,
                candidates.Count,
                acceptedCount,
                OBDDiagnosticText.ElapsedMilliseconds(started));
        }
    }

    /// <summary>1件のアドレスについて集めた受信値です。</summary>
    private sealed class Observation
    {
        /// <summary>最後に受信した空でないローカル名です。</summary>
        public string? LocalName { get; set; }

        /// <summary>受信した Service UUID です。</summary>
        public HashSet<string> Services { get; } = new(StringComparer.Ordinal);

        /// <summary>Manufacturer Data を受信したかどうかです。</summary>
        public bool HasManufacturerData { get; set; }
    }
}
