using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>既定アダプターへの接続確認をシステム通知として提示する能力です。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IDefaultAdapterConnectionPrompting
{
    /// <summary>接続確認通知の操作受付と通知許可を準備します。</summary>
    /// <remarks>責務: 接続確認通知を提示できるシステム状態を準備します。副作用: 初回はiOSの通知許可ダイアログを表示できます。</remarks>
    /// <param name="acceptance">ユーザーが接続開始を了承したときの通知先。</param>
    /// <returns>通知提示が許可されている場合は <c>true</c>。</returns>
    Task<bool> Prepare(Action<OBDConnectionEndpoint> acceptance);

    /// <summary>接続可能になった既定アダプターの確認通知を提示します。</summary>
    /// <remarks>責務: 1件の既定アダプター終端をユーザー承認可能な通知へ変換します。副作用: iOSのローカル通知を配信します。</remarks>
    /// <param name="endpoint">了承後に接続する物理終端。</param>
    void Present(OBDConnectionEndpoint endpoint);
}
