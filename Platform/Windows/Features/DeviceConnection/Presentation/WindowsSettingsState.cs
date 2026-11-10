using System.Collections.Generic;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>
/// Windows 設定画面のアダプター選択と既定アダプターの表示状態です（iOS 版の <c>MacOSSettingsState</c>）。
/// </summary>
public sealed record WindowsSettingsState
{
    /// <summary>接続の役割ごとに選ばれたアダプターです。</summary>
    public IReadOnlyDictionary<AdapterConnectionRole, DiscoveredAdapter> SelectedAdapters { get; init; } =
        new Dictionary<AdapterConnectionRole, DiscoveredAdapter>();

    /// <summary>保存済みの既定アダプターです。</summary>
    public DefaultAdapterPreference? DefaultAdapterPreference { get; init; }

    /// <summary>探索する通信方式です。</summary>
    public AdapterTransportMode AdapterTransportMode { get; init; } = AdapterTransportMode.Usb;

    /// <summary>最新の探索で見つかったアダプターです。</summary>
    public IReadOnlyList<DiscoveredAdapter> DiscoveredAdapters { get; init; } = [];

    /// <summary>探索の状態です。</summary>
    public WindowsAdapterDiscoveryStatus AdapterDiscoveryStatus { get; init; } = WindowsAdapterDiscoveryStatus.Idle;

    /// <summary>選択ダイアログを開いている役割です。閉じていれば null。</summary>
    public AdapterConnectionRole? PresentedAdapterSlot { get; init; }

    /// <summary>詳細を確認中のアダプターです。</summary>
    public DiscoveredAdapter? InspectedAdapter { get; init; }

    /// <summary>ほかの役割と同じアダプターを選ぼうとしたかどうかです。</summary>
    public bool HasAdapterAssignmentConflict { get; init; }

    /// <summary>アダプター設定へ注意を向ける要求の通し番号です。</summary>
    public uint AdapterAttentionSequence { get; init; }

    /// <summary>画面が処理済みの注意要求の通し番号です。</summary>
    public uint ConsumedAdapterAttentionSequence { get; init; }

    /// <summary>まだ画面が処理していない注意要求があるかどうかです。</summary>
    public bool HasPendingAdapterAttention => AdapterAttentionSequence > ConsumedAdapterAttentionSequence;
}
