using System;
using System.Collections.Generic;
using System.ComponentModel;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.Logging.Actions;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.Maintenance.Actions;
using ProjectZD8.Application.Settings.Actions;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Domain.Entities;
using ProjectZD8ForWindows.AppRoot.Composition;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

namespace ProjectZD8ForWindows.AppRoot.Flow;

/// <summary>
/// モデル同士をアプリの流れで結び付けます（iOS 版の <c>ProjectZD8App</c> の <c>onChange</c> とスコープ切り替え）。
/// </summary>
/// <remarks>
/// サインインの状態、アカウント設定、登録車両の変化を、ほかの機能のモデルへの操作に変換します。
/// 画面は持たず、UI スレッドから使う前提です。
/// </remarks>
public sealed class WindowsApplicationFlow
{
    /// <summary>アプリのルートの依存一式です。</summary>
    private readonly WindowsApplicationRoot _root;

    /// <summary>直前の認証の段階です。</summary>
    private AuthenticationPhase _lastPhase;

    /// <summary>直前の自動アップロードの設定です。</summary>
    private bool _lastAutomaticUpload;

    /// <summary>直前の登録車両の一覧です。</summary>
    private IReadOnlyList<VehicleProfile> _lastVehicles;

    /// <summary>ルートの依存一式を受け取り、変化の監視を始めます。</summary>
    /// <param name="root">アプリのルートの依存一式。</param>
    public WindowsApplicationFlow(WindowsApplicationRoot root)
    {
        _root = root;
        SettingsModel = root.MakeSettingsPresentationModel();
        _lastPhase = root.Authentication.State.Phase;
        _lastAutomaticUpload = root.AccountSettings.Settings.AutomaticSessionUploadEnabled;
        _lastVehicles = root.VehicleManagement.State.Vehicles;
        root.Authentication.PropertyChanged += OnAuthenticationChanged;
        root.AccountSettings.PropertyChanged += OnAccountSettingsChanged;
        root.VehicleManagement.PropertyChanged += OnVehicleManagementChanged;
    }

    /// <summary>設定画面の表示モデルを作り直したときに通知します（ログアウト時）。</summary>
    public event EventHandler? SettingsModelReplaced;

    /// <summary>アプリのルートの依存一式です。</summary>
    public WindowsApplicationRoot Root => _root;

    /// <summary>現在の設定画面の表示モデルです。</summary>
    public WindowsSettingsPresentationModel SettingsModel { get; private set; }

    /// <summary>
    /// サインイン済みのアカウントを、設定・車両・整備・接続履歴の保存範囲へ反映します。
    /// </summary>
    /// <remarks>責務: 認証済みのアカウント識別子を各機能へ通知します（シェルが表示されたときに呼ぶ）。</remarks>
    public void ActivateAuthenticatedScope()
    {
        var identifier = _root.Authentication.State.Session?.UserIdentifier;
        _root.AccountSettings.Send(new AccountSettingsAction.AccountIdentifierChanged(identifier));
        _root.ConnectionSessionLifecycle.Send(
            new ConnectionSessionLifecycleAction.AutomaticUploadChanged(_root.AccountSettings.Settings.AutomaticSessionUploadEnabled));
        _root.VehicleManagement.Send(new VehicleManagementAction.AccountIdentifierChanged(identifier));
        _root.Maintenance.Send(new MaintenanceAction.VehiclesChanged(_root.VehicleManagement.State.Vehicles));
        _root.Maintenance.Send(new MaintenanceAction.AccountIdentifierChanged(identifier));
        _root.ConnectionSessionLifecycle.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged(identifier));
        _root.ConnectionHistory.Send(new ConnectionHistoryAction.AccountIdentifierChanged(identifier));
        _root.ConnectionHistory.Send(
            new ConnectionHistoryAction.AutomaticUploadChanged(_root.AccountSettings.Settings.AutomaticSessionUploadEnabled));
    }

    /// <summary>
    /// ウインドウが再び操作できるようになったとき、接続履歴と整備記録を同期し直します。
    /// </summary>
    /// <remarks>責務: ウインドウの前面化を、サインイン中の履歴・整備の更新要求へ変換します。</remarks>
    public void WindowActivated()
    {
        if (_root.Authentication.State.Phase != AuthenticationPhase.SignedIn)
        {
            return;
        }

        _root.ConnectionHistory.Send(new ConnectionHistoryAction.RefreshRequested());
        _root.Maintenance.Send(new MaintenanceAction.RefreshRequested());
    }

    /// <summary>認証の段階が変わったときの処理です。</summary>
    /// <remarks>責務: ログアウトを、取得の停止・各機能の範囲解除・設定画面の作り直しへ変換します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAuthenticationChanged(object? sender, PropertyChangedEventArgs e)
    {
        var phase = _root.Authentication.State.Phase;
        if (phase == _lastPhase)
        {
            return;
        }

        _lastPhase = phase;
        if (phase != AuthenticationPhase.SignedOut)
        {
            return;
        }

        _root.LiveTelemetry.Send(new LiveTelemetryAction.StopRequested());
        _root.ConnectionSessionLifecycle.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged(null));
        _root.ConnectionHistory.Send(new ConnectionHistoryAction.AccountIdentifierChanged(null));
        _root.AccountSettings.Send(new AccountSettingsAction.AccountIdentifierChanged(null));
        _root.VehicleManagement.Send(new VehicleManagementAction.AccountIdentifierChanged(null));
        _root.Maintenance.Send(new MaintenanceAction.AccountIdentifierChanged(null));
        SettingsModel = _root.MakeSettingsPresentationModel();
        SettingsModelReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>アカウント設定が変わったときの処理です。</summary>
    /// <remarks>責務: 自動アップロードの設定変更を、接続セッションと接続履歴へ通知します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAccountSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isEnabled = _root.AccountSettings.Settings.AutomaticSessionUploadEnabled;
        if (isEnabled == _lastAutomaticUpload)
        {
            return;
        }

        _lastAutomaticUpload = isEnabled;
        _root.ConnectionSessionLifecycle.Send(new ConnectionSessionLifecycleAction.AutomaticUploadChanged(isEnabled));
        _root.ConnectionHistory.Send(new ConnectionHistoryAction.AutomaticUploadChanged(isEnabled));
    }

    /// <summary>車両管理の状態が変わったときの処理です。</summary>
    /// <remarks>責務: 登録車両の一覧の変化を整備記録へ通知します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnVehicleManagementChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vehicles = _root.VehicleManagement.State.Vehicles;
        if (ReferenceEquals(vehicles, _lastVehicles) || SameVehicles(vehicles, _lastVehicles))
        {
            return;
        }

        _lastVehicles = vehicles;
        _root.Maintenance.Send(new MaintenanceAction.VehiclesChanged(vehicles));
    }

    /// <summary>2 つの車両一覧が同じかどうかを返します。</summary>
    /// <remarks>責務: Swift の配列の == と同じく、要素ごとの等価で比べます。</remarks>
    /// <param name="left">左。</param>
    /// <param name="right">右。</param>
    /// <returns>同じなら true。</returns>
    private static bool SameVehicles(IReadOnlyList<VehicleProfile> left, IReadOnlyList<VehicleProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
