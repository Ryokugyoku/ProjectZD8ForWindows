using System;
using System.Threading.Tasks;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.Logging.State;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Application.Maintenance.State;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

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
/// <param name="IsLocalMode">Apple・CloudKit の設定がなく、アカウントなしのローカルモードで動いているなら true。</param>
/// <param name="CloudSync">Windows 版だけの iCloud サインインの表示モデル。</param>
/// <param name="PromptForDefaultAdapterConnection">既定アダプターの到来を監視して接続を尋ねるユースケース（iOS 版と同じ）。</param>
/// <param name="PromptedVehicleConnectionRequest">尋ねて受け入れられた接続要求をサインイン後まで預かる入れ物。</param>
/// <param name="AdapterArrivalPrompt">到来したアダプターについて尋ねる InfoBar の表示モデル。</param>
/// <param name="DeliverProtocolActivation">プロトコル起動の URI をサインインの戻り受け口へ渡す処理。</param>
/// <param name="SynchronizeAccountState">CloudKit の AccountSettings・AccountSessionRevocation を読み直す処理（前面に来たとき）。</param>
/// <param name="Shutdown">終了時に保存先を閉じる処理（生ログの確定と索引の書出し）。</param>
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
    Func<WindowsSettingsPresentationModel> MakeSettingsPresentationModel,
    bool IsLocalMode,
    WindowsCloudSyncModel CloudSync,
    PromptForDefaultAdapterConnectionUseCase PromptForDefaultAdapterConnection,
    PromptedVehicleConnectionRequestBuffer PromptedVehicleConnectionRequest,
    WindowsAdapterArrivalPromptModel AdapterArrivalPrompt,
    Action<Uri> DeliverProtocolActivation,
    Func<Task> SynchronizeAccountState,
    Action Shutdown);
