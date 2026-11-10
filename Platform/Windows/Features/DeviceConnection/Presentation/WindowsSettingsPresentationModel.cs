using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>
/// Windows 設定画面のアダプター探索・選択・既定アダプター保存を扱う表示モデルです（iOS 版の <c>MacOSSettingsPresentationModel</c>）。
/// </summary>
/// <remarks>UI フレームワークに依存しません。UI スレッドから呼ぶ前提です。</remarks>
public sealed class WindowsSettingsPresentationModel : INotifyPropertyChanged
{
    /// <summary>最新の探索だけを反映する探索ユースケースです。</summary>
    private readonly LatestAdapterDiscoveryUseCase _latestDiscovery;

    /// <summary>既定アダプターの保存ユースケースです。</summary>
    private readonly DefaultAdapterPreferenceUseCase _defaultAdapterPreference;

    /// <summary>現在の表示状態です。</summary>
    private WindowsSettingsState _state;

    /// <summary>依存を指定して作り、保存済みの既定アダプターを復元します。</summary>
    /// <param name="state">初期状態。</param>
    /// <param name="latestDiscovery">最新の探索だけを反映する探索ユースケース。</param>
    /// <param name="defaultAdapterPreference">既定アダプターの保存ユースケース。</param>
    public WindowsSettingsPresentationModel(
        WindowsSettingsState state,
        LatestAdapterDiscoveryUseCase latestDiscovery,
        DefaultAdapterPreferenceUseCase defaultAdapterPreference)
    {
        _state = state;
        _latestDiscovery = latestDiscovery;
        _defaultAdapterPreference = defaultAdapterPreference;
        RestoreDefaultAdapter();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>現在の表示状態です。</summary>
    public WindowsSettingsState State
    {
        get => _state;
        private set
        {
            if (Equals(_state, value))
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    /// <summary>画面の操作を状態へ反映します。</summary>
    /// <remarks>責務: 1 件の設定画面の操作を、探索・選択・保存の状態遷移へ変換します。</remarks>
    /// <param name="action">操作。</param>
    public void Send(WindowsSettingsAction action)
    {
        switch (action)
        {
            case WindowsSettingsAction.AdapterSelectionRequested request:
                State = State with { PresentedAdapterSlot = request.Role };
                StartDiscovery();
                break;
            case WindowsSettingsAction.AdapterTransportModeSelected selected:
                if (State.AdapterTransportMode == selected.Mode)
                {
                    return;
                }

                State = State with { AdapterTransportMode = selected.Mode };
                StartDiscovery();
                break;
            case WindowsSettingsAction.AdapterRefreshRequested:
                StartDiscovery();
                break;
            case WindowsSettingsAction.AdapterCandidateSelected candidate:
                State = State with { InspectedAdapter = candidate.Adapter, HasAdapterAssignmentConflict = false };
                break;
            case WindowsSettingsAction.InspectedAdapterConfirmed:
                ConfirmInspectedAdapter();
                break;
            case WindowsSettingsAction.InspectedAdapterDeclined:
            case WindowsSettingsAction.AdapterSelectionCancelled:
                CloseAdapterSelection();
                break;
            case WindowsSettingsAction.AdapterDetailsDismissed:
                State = State with { InspectedAdapter = null };
                break;
            case WindowsSettingsAction.AdapterAttentionRequested:
                State = State with { AdapterAttentionSequence = unchecked(State.AdapterAttentionSequence + 1) };
                break;
            case WindowsSettingsAction.AdapterAttentionConsumed consumed:
                if (consumed.Sequence == State.AdapterAttentionSequence)
                {
                    State = State with { ConsumedAdapterAttentionSequence = consumed.Sequence };
                }

                break;
        }
    }

    /// <summary>確認中のアダプターを役割へ割り当てます。</summary>
    /// <remarks>責務: 割り当ての衝突を確かめ、主アダプターなら既定アダプターとして保存します。</remarks>
    private void ConfirmInspectedAdapter()
    {
        if (State.PresentedAdapterSlot is not { } slot || State.InspectedAdapter is not { } adapter)
        {
            return;
        }

        if (AdapterAssignmentPolicy.ConflictingRole(adapter, slot, State.SelectedAdapters) is not null)
        {
            State = State with { HasAdapterAssignmentConflict = true };
            return;
        }

        var selected = new Dictionary<AdapterConnectionRole, DiscoveredAdapter>(State.SelectedAdapters) { [slot] = adapter };
        State = State with
        {
            SelectedAdapters = selected,
            DefaultAdapterPreference = slot == AdapterConnectionRole.Primary
                ? _defaultAdapterPreference.Save(adapter)
                : State.DefaultAdapterPreference,
        };
        CloseAdapterSelection();
    }

    /// <summary>保存済みの既定アダプターを復元します。</summary>
    /// <remarks>責務: 起動時に既定アダプターの設定を読み、その方式で探索を始めます。</remarks>
    private void RestoreDefaultAdapter()
    {
        if (_defaultAdapterPreference.Load() is not { } preference)
        {
            return;
        }

        State = State with { DefaultAdapterPreference = preference, AdapterTransportMode = preference.TransportMode };
        StartDiscovery();
    }

    /// <summary>現在の方式で探索を始めます。</summary>
    /// <remarks>責務: 探索中の表示へ切り替え、最新の探索結果だけを状態へ反映します。</remarks>
    private void StartDiscovery()
    {
        var requestedMode = State.AdapterTransportMode;
        State = State with { DiscoveredAdapters = [], AdapterDiscoveryStatus = WindowsAdapterDiscoveryStatus.Searching };
        _latestDiscovery.Start(requestedMode, outcome =>
        {
            switch (outcome)
            {
                case AdapterDiscoveryOutcome.Discovered discovered:
                    State = State with { DiscoveredAdapters = discovered.Adapters, AdapterDiscoveryStatus = WindowsAdapterDiscoveryStatus.Loaded };
                    SynchronizeDetectedDefaultAdapter(discovered.Adapters, requestedMode);
                    break;
                default:
                    State = State with { DiscoveredAdapters = [], AdapterDiscoveryStatus = WindowsAdapterDiscoveryStatus.Failed };
                    SynchronizeDetectedDefaultAdapter([], requestedMode);
                    break;
            }
        });
    }

    /// <summary>探索結果から、既定アダプターが見つかったかを主アダプターへ反映します。</summary>
    /// <remarks>責務: 既定アダプターと同じ方式の探索結果だけで、主アダプターの検出状態を更新します。</remarks>
    /// <param name="adapters">見つかったアダプター。</param>
    /// <param name="mode">探索した方式。</param>
    private void SynchronizeDetectedDefaultAdapter(IReadOnlyList<DiscoveredAdapter> adapters, AdapterTransportMode mode)
    {
        var selected = new Dictionary<AdapterConnectionRole, DiscoveredAdapter>(State.SelectedAdapters);
        if (State.DefaultAdapterPreference is not { } preference)
        {
            selected.Remove(AdapterConnectionRole.Primary);
            State = State with { SelectedAdapters = selected };
            return;
        }

        if (preference.TransportMode != mode)
        {
            return;
        }

        if (_defaultAdapterPreference.DetectedAdapter(preference, adapters) is { } detected)
        {
            selected[AdapterConnectionRole.Primary] = detected;
        }
        else
        {
            selected.Remove(AdapterConnectionRole.Primary);
        }

        State = State with { SelectedAdapters = selected };
    }

    /// <summary>アダプター選択を閉じます。</summary>
    /// <remarks>責務: 探索を止め、選択中の表示状態を消します。</remarks>
    private void CloseAdapterSelection()
    {
        _latestDiscovery.Cancel();
        State = State with { InspectedAdapter = null, HasAdapterAssignmentConflict = false, PresentedAdapterSlot = null };
    }

    /// <summary>プロパティの変更を通知します。</summary>
    /// <remarks>責務: 変更されたプロパティ名で <see cref="PropertyChanged"/> を発火します。</remarks>
    /// <param name="propertyName">プロパティ名。</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
