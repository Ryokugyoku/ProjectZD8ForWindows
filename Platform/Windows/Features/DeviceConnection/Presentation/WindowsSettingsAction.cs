using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>Windows 設定画面のアダプター選択の操作です（iOS 版の <c>MacOSSettingsAction</c>）。</summary>
public abstract record WindowsSettingsAction
{
    /// <summary>役割のアダプター選択を開きます。</summary>
    /// <param name="Role">選択する役割。</param>
    public sealed record AdapterSelectionRequested(AdapterConnectionRole Role) : WindowsSettingsAction;

    /// <summary>探索する通信方式を選びました。</summary>
    /// <param name="Mode">通信方式。</param>
    public sealed record AdapterTransportModeSelected(AdapterTransportMode Mode) : WindowsSettingsAction;

    /// <summary>探索をやり直します。</summary>
    public sealed record AdapterRefreshRequested : WindowsSettingsAction;

    /// <summary>候補を選び、詳細を確認します。</summary>
    /// <param name="Adapter">候補。</param>
    public sealed record AdapterCandidateSelected(DiscoveredAdapter Adapter) : WindowsSettingsAction;

    /// <summary>確認中のアダプターで決定します。</summary>
    public sealed record InspectedAdapterConfirmed : WindowsSettingsAction;

    /// <summary>確認中のアダプターを使いません。</summary>
    public sealed record InspectedAdapterDeclined : WindowsSettingsAction;

    /// <summary>詳細を閉じます。</summary>
    public sealed record AdapterDetailsDismissed : WindowsSettingsAction;

    /// <summary>選択をやめます。</summary>
    public sealed record AdapterSelectionCancelled : WindowsSettingsAction;

    /// <summary>アダプター設定へ注意を向けます（ホームの「アダプターを設定」）。</summary>
    public sealed record AdapterAttentionRequested : WindowsSettingsAction;

    /// <summary>画面が注意要求を処理しました。</summary>
    /// <param name="Sequence">処理した通し番号。</param>
    public sealed record AdapterAttentionConsumed(uint Sequence) : WindowsSettingsAction;
}
