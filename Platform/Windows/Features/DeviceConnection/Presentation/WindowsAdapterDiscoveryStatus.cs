namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>アダプター探索の表示状態です（iOS 版の <c>MacOSAdapterDiscoveryStatus</c>）。</summary>
public enum WindowsAdapterDiscoveryStatus
{
    /// <summary>まだ探索していません。</summary>
    Idle,

    /// <summary>探索中です。</summary>
    Searching,

    /// <summary>探索が終わりました。</summary>
    Loaded,

    /// <summary>探索に失敗しました。</summary>
    Failed,
}
