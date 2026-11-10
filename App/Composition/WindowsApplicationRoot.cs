using System;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.Logging.State;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Application.Maintenance.State;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// 組み立てが作ったアプリのルートの依存一式です（iOS 版の <c>ProjectZD8App</c> が <c>@State</c> で持つモデル群）。
/// </summary>
/// <param name="Authentication">認証セッションのモデル。</param>
/// <param name="AccountSettings">アカウント設定のモデル。</param>
/// <param name="VehicleManagement">車両管理のモデル。</param>
/// <param name="Maintenance">整備記録のモデル。</param>
/// <param name="LiveTelemetry">ライブ取得のモデル。</param>
/// <param name="ConnectionSessionLifecycle">接続セッションの開始・終了のモデル。</param>
/// <param name="ConnectionHistory">接続履歴のモデル。</param>
/// <param name="SessionLogAnalysis">保存済みログの解析モデル。</param>
/// <param name="StartVehicleConnection">ホームの接続要求を車両識別へ渡すユースケース。</param>
/// <param name="MakeSettingsPresentationModel">設定画面の表示モデルを作る処理（ログアウトのたびに作り直す）。</param>
public sealed record WindowsApplicationRoot(
    AuthenticationSessionModel Authentication,
    AccountSettingsModel AccountSettings,
    VehicleManagementModel VehicleManagement,
    MaintenanceModel Maintenance,
    LiveTelemetryModel LiveTelemetry,
    ConnectionSessionLifecycleModel ConnectionSessionLifecycle,
    ConnectionHistoryModel ConnectionHistory,
    SessionLogAnalysisModel SessionLogAnalysis,
    StartVehicleConnectionUseCase StartVehicleConnection,
    Func<WindowsSettingsPresentationModel> MakeSettingsPresentationModel);
