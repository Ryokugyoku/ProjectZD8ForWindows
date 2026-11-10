using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.Logging.Actions;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.State;

/// <summary>接続セッションの開始、車両関連付け、終了を永続化へ結び付けます。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class ConnectionSessionLifecycleModel : INotifyPropertyChanged
{
    /// <summary>現在接続中として管理しているセッションです。</summary>
    private ConnectionSession? _activeSession;

    /// <summary>セッションの永続化境界です。</summary>
    private readonly IConnectionSessionRepository _repository;

    /// <summary>未デコードOBD応答の永続化境界です。</summary>
    private readonly IConnectionSessionRawLogRepository? _rawLogRepository;

    /// <summary>sessionと取得証拠を部分成功なしで終了・回復する境界です。</summary>
    private readonly IConnectionSessionAcquisitionTerminationRepository? _acquisitionTerminationRepository;

    /// <summary>現在日時を生成する注入済み処理です。</summary>
    private readonly Func<DateTimeOffset> _now;

    /// <summary>新規セッションIDを生成する注入済み処理です。</summary>
    private readonly Func<ConnectionSessionID> _makeID;

    /// <summary>新しいセッションへ固定する現在端末情報です。</summary>
    private readonly ConnectionSessionAcquisitionDevice? _acquisitionDevice;

    /// <summary>履歴の再読込が必要になったことを通知する処理です。</summary>
    private readonly Action _historyDidChange;

    /// <summary>終了済みセッションのiCloudアップロード要求を通知する処理です。</summary>
    private readonly Action<ConnectionSession> _endedSessionUploadRequested;

    /// <summary>現在のAppleアカウント識別子です。</summary>
    private string? _accountIdentifier;

    /// <summary>セッション終了時にiCloudアップロードを要求するかを示します。</summary>
    private bool _automaticUploadEnabled;

    /// <summary>現在セッションの走行距離差分へ採用している累積距離取得元です。</summary>
    private ConnectionSessionDistanceSource? _selectedDistanceSource;

    /// <summary>セッション保存先と副作用依存を固定して生成します。</summary>
    /// <remarks>責務: 接続ライフサイクル操作を1件のセッション永続化境界へ結び付けます。</remarks>
    /// <param name="repository">セッションの保存と取得を行う境界。</param>
    /// <param name="rawLogRepository">数値化前のOBD応答を追記する境界。</param>
    /// <param name="acquisitionTerminationRepository">sessionと取得証拠を部分成功なしで終了・回復する境界。</param>
    /// <param name="now">開始日時と終了日時を生成する処理。<c>null</c> なら現在時刻。</param>
    /// <param name="makeID">新しいセッションIDを生成する処理。省略時はUUIDを生成します。</param>
    /// <param name="acquisitionDevice">新しいセッションへ固定する現在端末情報。</param>
    /// <param name="historyDidChange">保存後に履歴再読込を通知する処理。</param>
    /// <param name="automaticUploadEnabled">終了時のiCloud自動アップロード初期値。</param>
    /// <param name="endedSessionUploadRequested">自動アップロード対象の終了済みセッション通知先。</param>
    public ConnectionSessionLifecycleModel(
        IConnectionSessionRepository repository,
        IConnectionSessionRawLogRepository? rawLogRepository = null,
        IConnectionSessionAcquisitionTerminationRepository? acquisitionTerminationRepository = null,
        Func<DateTimeOffset>? now = null,
        Func<ConnectionSessionID>? makeID = null,
        ConnectionSessionAcquisitionDevice? acquisitionDevice = null,
        Action? historyDidChange = null,
        bool automaticUploadEnabled = false,
        Action<ConnectionSession>? endedSessionUploadRequested = null)
    {
        _repository = repository;
        _rawLogRepository = rawLogRepository;
        _acquisitionTerminationRepository = acquisitionTerminationRepository;
        _now = now ?? (() => DateTimeOffset.Now);
        _makeID = makeID ?? ConnectionSessionID.New;
        _acquisitionDevice = acquisitionDevice;
        _historyDidChange = historyDidChange ?? (() => { });
        _automaticUploadEnabled = automaticUploadEnabled;
        _endedSessionUploadRequested = endedSessionUploadRequested ?? (_ => { });
    }

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>現在接続中として管理しているセッションです。</summary>
    public ConnectionSession? ActiveSession
    {
        get => _activeSession;
        private set => SetProperty(ref _activeSession, value);
    }

    /// <summary>型付き操作をセッション永続化へ変換します。</summary>
    /// <remarks>責務: 1件のLogging操作を対応するライフサイクル変更へ振り分けます。</remarks>
    /// <param name="action">AppまたはApplicationから通知された操作。</param>
    public void Send(ConnectionSessionLifecycleAction action)
    {
        switch (action)
        {
            case ConnectionSessionLifecycleAction.AccountIdentifierChanged changed: ActivateAccount(changed.Identifier); break;
            case ConnectionSessionLifecycleAction.AutomaticUploadChanged changed: _automaticUploadEnabled = changed.IsEnabled; break;
            case ConnectionSessionLifecycleAction.StartRequested: StartSession(); break;
            case ConnectionSessionLifecycleAction.VehicleResolved resolved: BindVehicle(resolved.Vehicle); break;
            case ConnectionSessionLifecycleAction.DistanceObserved observed: RecordDistance(observed.Observation); break;
            case ConnectionSessionLifecycleAction.EndRequested requested: EndSession(requested.Reason); break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>数値化前のOBD応答を現在セッションへ追記します。</summary>
    /// <remarks>責務: 1件の未デコードOBD応答を現在の接続セッションへ関連付けて永続化します。</remarks>
    /// <param name="observation">取得境界から受け取った未デコードOBD応答。</param>
    /// <exception cref="ConnectionSessionRepositoryException">接続セッション不在またはRawログ永続化に失敗した場合のエラー。</exception>
    public void RecordRawResponse(OBDRawResponseObservation observation)
    {
        if (ActiveSession is not { } session || _rawLogRepository is null)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        _rawLogRepository.Append(observation, session.Id);
    }

    /// <summary>新しいアカウントを有効化し、残存中セッションを異常終了へ閉じます。</summary>
    /// <remarks>責務: 1件のアカウント変更をセッション保存範囲と再起動回復へ反映します。</remarks>
    /// <param name="identifier">新しいAppleアカウント識別子。</param>
    private void ActivateAccount(string? identifier)
    {
        if (string.Equals(identifier, _accountIdentifier, StringComparison.Ordinal))
        {
            return;
        }
        _accountIdentifier = identifier;
        ActiveSession = null;
        _selectedDistanceSource = null;
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }
        try
        {
            var unfinished = _repository.Sessions(identifier).Where(session => session.EndedAt is null).ToList();
            if (unfinished.Count == 0)
            {
                return;
            }
            var recoveredAt = _now();
            IReadOnlyList<ConnectionSession> endedSessions;
            if (_acquisitionTerminationRepository is { } terminationRepository)
            {
                endedSessions = terminationRepository.RecoverInterruptedSessionAcquisitions(identifier, recoveredAt);
            }
            else
            {
                var ended = new List<ConnectionSession>();
                foreach (var session in unfinished)
                {
                    var closed = session with
                    {
                        EndedAt = recoveredAt,
                        EndReason = ConnectionSessionEndReason.UnexpectedTermination,
                    };
                    _repository.Save(closed);
                    ended.Add(closed);
                }
                endedSessions = ended;
            }
            if (endedSessions.Count > 0)
            {
                _historyDidChange();
            }
            if (_automaticUploadEnabled)
            {
                foreach (var session in endedSessions)
                {
                    _endedSessionUploadRequested(session);
                }
            }
        }
        catch (Exception)
        {
            return;
        }
    }

    /// <summary>現在アカウントへ新しい未終了セッションを保存します。</summary>
    /// <remarks>責務: 1回のHOME接続開始を新規セッションとして永続化します。</remarks>
    private void StartSession()
    {
        if (string.IsNullOrEmpty(_accountIdentifier))
        {
            return;
        }
        if (ActiveSession is not null)
        {
            EndSession(ConnectionSessionEndReason.Superseded);
        }
        var session = new ConnectionSession(
            accountIdentifier: _accountIdentifier,
            id: _makeID(),
            startedAt: _now(),
            acquisitionDevice: _acquisitionDevice);
        try
        {
            _repository.Save(session);
            ActiveSession = session;
            _selectedDistanceSource = null;
            _historyDidChange();
        }
        catch (Exception)
        {
            ActiveSession = null;
        }
    }

    /// <summary>確定した登録車両を現在の未終了セッションへ保存します。</summary>
    /// <remarks>責務: 1台の登録車両スナップショットを現在セッションへ関連付けます。</remarks>
    /// <param name="vehicle">接続対象として確定した登録車両。</param>
    private void BindVehicle(VehicleProfile vehicle)
    {
        if (ActiveSession is not { } current)
        {
            return;
        }
        var session = current with { Vehicle = new ConnectionSessionVehicle(vehicle) };
        try
        {
            _repository.Save(session);
            ActiveSession = session;
            _historyDidChange();
        }
        catch (Exception)
        {
            return;
        }
    }

    /// <summary>優先順位を満たす累積距離の先頭値と最新値を現在セッションへ保存します。</summary>
    /// <remarks>責務: 1件の有効な累積距離観測を取得元の優先順位に従ってセッション差分境界へ反映します。</remarks>
    /// <param name="observation">取得元とキロメートル値を保持する累積距離観測。</param>
    private void RecordDistance(ConnectionSessionDistanceObservation observation)
    {
        if (!double.IsFinite(observation.Kilometers) || !(observation.Kilometers >= 0)
            || ActiveSession is not { } current)
        {
            return;
        }
        if (_selectedDistanceSource is { } selected
            && observation.Source != selected
            && !observation.Source.HasPriority(selected))
        {
            return;
        }
        ConnectionSession session;
        if (observation.Source != _selectedDistanceSource)
        {
            session = current with
            {
                StartingOdometerKilometers = observation.Kilometers,
                EndingOdometerKilometers = observation.Kilometers,
                DistanceSourceModelCode = observation.VehicleModelCode,
            };
        }
        else
        {
            if (current.EndingOdometerKilometers == observation.Kilometers)
            {
                return;
            }
            session = current with
            {
                EndingOdometerKilometers = observation.Kilometers,
                DistanceSourceModelCode = observation.VehicleModelCode,
            };
        }
        try
        {
            _repository.Save(session);
            ActiveSession = session;
            _selectedDistanceSource = observation.Source;
            _historyDidChange();
        }
        catch (Exception)
        {
            return;
        }
    }

    /// <summary>現在セッションへ終了日時と原因を保存します。</summary>
    /// <remarks>責務: 1件の未終了セッションを指定原因で終端状態へ遷移させます。</remarks>
    /// <param name="reason">セッションが終了した直接原因。</param>
    private void EndSession(ConnectionSessionEndReason reason)
    {
        if (ActiveSession is not { } currentSession)
        {
            return;
        }
        var endedAt = _now();
        try
        {
            ConnectionSession session;
            if (_acquisitionTerminationRepository is { } terminationRepository)
            {
                session = terminationRepository.FinishSessionAcquisition(currentSession, endedAt, reason);
            }
            else
            {
                var ended = currentSession with { EndedAt = endedAt, EndReason = reason };
                _repository.Save(ended);
                session = ended;
            }
            ActiveSession = null;
            _selectedDistanceSource = null;
            _historyDidChange();
            if (_automaticUploadEnabled)
            {
                _endedSessionUploadRequested(session);
            }
        }
        catch (Exception)
        {
            ActiveSession = currentSession;
        }
    }

    /// <summary>値が変わったときだけフィールドを更新して通知します。</summary>
    /// <remarks>責務: 1件の観測対象プロパティの変更をPropertyChangedへ変換します。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">通知するプロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
