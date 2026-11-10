using System;
using System.ComponentModel;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>
/// 既定のアダプターが近くに来たときに出す「接続しますか」の表示状態と操作を持ちます。
/// </summary>
/// <remarks>
/// iOS 版はローカル通知で尋ねます。Windows 版はシェルの InfoBar で尋ねます。
/// 到来の検出と接続の要求は Application の <c>PromptForDefaultAdapterConnectionUseCase</c> が行い、
/// このモデルは「いま尋ねている終端」だけを持ちます。UI スレッド（単一スレッド）から呼ぶ前提です。
/// </remarks>
public sealed class WindowsAdapterArrivalPromptModel : INotifyPropertyChanged
{
    /// <summary>「接続」を選んだときの処理です。</summary>
    private readonly Action<OBDConnectionEndpoint> _accept;

    /// <summary>「あとで」を選んだときの処理です。</summary>
    private readonly Action _decline;

    /// <summary>いま尋ねている終端です。</summary>
    private OBDConnectionEndpoint? _pendingEndpoint;

    /// <summary>
    /// 選んだ結果を返す処理を受け取ります。
    /// </summary>
    /// <remarks>責務: 組み立てが渡す 2 つの処理を保持します。</remarks>
    /// <param name="accept">「接続」を選んだときの処理。</param>
    /// <param name="decline">「あとで」を選んだときの処理。</param>
    public WindowsAdapterArrivalPromptModel(Action<OBDConnectionEndpoint> accept, Action decline)
    {
        _accept = accept;
        _decline = decline;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>いま尋ねている終端。尋ねていなければ null。</summary>
    public OBDConnectionEndpoint? PendingEndpoint
    {
        get => _pendingEndpoint;
        private set
        {
            if (Equals(_pendingEndpoint, value))
            {
                return;
            }

            _pendingEndpoint = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingEndpoint)));
        }
    }

    /// <summary>
    /// 終端について尋ね始めます。
    /// </summary>
    /// <remarks>責務: 尋ねる終端を表示状態にします。</remarks>
    /// <param name="endpoint">近くに来た既定のアダプター。</param>
    public void Present(OBDConnectionEndpoint endpoint) => PendingEndpoint = endpoint;

    /// <summary>
    /// 尋ねるのをやめます（ほかの経路で片付いたとき）。
    /// </summary>
    /// <remarks>責務: 表示状態を空にします。</remarks>
    public void Dismiss() => PendingEndpoint = null;

    /// <summary>
    /// 「接続」を選びます。
    /// </summary>
    /// <remarks>責務: 尋ねている終端を接続の要求として返し、表示を閉じます。</remarks>
    public void Accept()
    {
        if (PendingEndpoint is not { } endpoint)
        {
            return;
        }

        PendingEndpoint = null;
        _accept(endpoint);
    }

    /// <summary>
    /// 「あとで」を選びます。
    /// </summary>
    /// <remarks>責務: 断ったことを返し、表示を閉じます。</remarks>
    public void Decline()
    {
        if (PendingEndpoint is null)
        {
            return;
        }

        PendingEndpoint = null;
        _decline();
    }
}
