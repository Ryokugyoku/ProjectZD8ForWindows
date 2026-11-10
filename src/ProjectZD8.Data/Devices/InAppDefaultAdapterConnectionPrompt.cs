using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>既定アダプターの到来をアプリ内の確認表示（InfoBar やダイアログ）として提示します。</summary>
/// <remarks>
/// iOS 版の <c>IOSLocalDefaultAdapterConnectionPrompt</c> はローカル通知を配信し、通知の操作を了承として受け取ります。
/// Windows 版では OS の通知に依存せず、<see cref="PromptRequested"/> イベントで画面へ知らせ、
/// 画面が <see cref="Accept"/> または <see cref="Decline"/> を呼びます（トースト通知は画面側で任意に追加できます）。
/// UI スレッドから呼ぶ前提です。
/// </remarks>
public sealed class InAppDefaultAdapterConnectionPrompt : IDefaultAdapterConnectionPrompting
{
    /// <summary>接続開始を了承した終端の通知先です。</summary>
    private Action<OBDConnectionEndpoint>? acceptance;

    /// <summary>了承が準備より先に届いた場合の一時終端です。</summary>
    private OBDConnectionEndpoint? pendingAcceptedEndpoint;

    /// <summary>画面へ確認表示を求めたときに発生します。</summary>
    public event EventHandler<OBDConnectionEndpoint>? PromptRequested;

    /// <summary>画面の確認表示を閉じるべきときに発生します。</summary>
    public event EventHandler? PromptDismissed;

    /// <summary>現在表示を求めている終端です。画面が後から購読したときに使います。</summary>
    public OBDConnectionEndpoint? PendingPrompt { get; private set; }

    /// <summary>接続確認の了承通知先を登録します。</summary>
    /// <remarks>責務: 了承の通知先をアプリ内確認表示の準備状態へ変換し、準備前に届いた了承を引き渡します。</remarks>
    /// <param name="acceptance">ユーザーが接続を了承した終端の通知先。</param>
    /// <returns>アプリ内表示は許可が要らないため常に <c>true</c>。</returns>
    public Task<bool> Prepare(Action<OBDConnectionEndpoint> acceptance)
    {
        this.acceptance = acceptance;
        if (pendingAcceptedEndpoint is { } endpoint)
        {
            pendingAcceptedEndpoint = null;
            acceptance(endpoint);
        }

        return Task.FromResult(true);
    }

    /// <summary>既定アダプターを開いて接続するか確認する表示を求めます。</summary>
    /// <remarks>責務: 1件の終端を画面の確認表示要求へ変換し、前の要求を置き換えます。</remarks>
    /// <param name="endpoint">ユーザー了承後に接続する終端。</param>
    public void Present(OBDConnectionEndpoint endpoint)
    {
        PendingPrompt = endpoint;
        PromptRequested?.Invoke(this, endpoint);
    }

    /// <summary>画面で「接続」が選ばれたことを受け取ります。</summary>
    /// <remarks>責務: 1件の了承を登録済み通知先または準備前バッファーへ引き渡します。</remarks>
    /// <param name="endpoint">表示していた終端。</param>
    public void Accept(OBDConnectionEndpoint endpoint)
    {
        ClearPendingPrompt();
        if (acceptance is not null)
        {
            acceptance(endpoint);
        }
        else
        {
            pendingAcceptedEndpoint = endpoint;
        }
    }

    /// <summary>画面で「今はしない」が選ばれたこと、または表示が閉じられたことを受け取ります。</summary>
    /// <remarks>責務: 表示中の要求を了承せずに破棄します。</remarks>
    public void Decline() => ClearPendingPrompt();

    /// <summary>表示中の要求を消して画面へ閉じるよう知らせます。</summary>
    /// <remarks>責務: 確認表示の要求状態を空にします。</remarks>
    private void ClearPendingPrompt()
    {
        if (PendingPrompt is null)
        {
            return;
        }

        PendingPrompt = null;
        PromptDismissed?.Invoke(this, EventArgs.Empty);
    }
}
