using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.State;

/// <summary>通知で承認された接続要求を認証完了まで一時保持します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class PromptedVehicleConnectionRequestBuffer : INotifyPropertyChanged
{
    /// <summary>未処理の承認済み接続終端の保存先です。</summary>
    private OBDConnectionEndpoint? pendingEndpoint;

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>未処理の承認済み接続終端です。</summary>
    public OBDConnectionEndpoint? PendingEndpoint
    {
        get => pendingEndpoint;
        private set => SetProperty(ref pendingEndpoint, value);
    }

    /// <summary>承認済み接続終端を未処理要求として保持します。</summary>
    /// <remarks>責務: 最新の承認済み接続要求を1件だけ保持します。</remarks>
    /// <param name="endpoint">ユーザーが通知で接続を了承した終端。</param>
    public void Store(OBDConnectionEndpoint endpoint)
    {
        PendingEndpoint = endpoint;
    }

    /// <summary>未処理の接続終端を取り出してバッファーを空にします。</summary>
    /// <remarks>責務: 現在の承認済み接続要求を高々1件だけ消費します。</remarks>
    /// <returns>保持されていた接続終端。要求がなければ <c>null</c>。</returns>
    public OBDConnectionEndpoint? Consume()
    {
        var endpoint = PendingEndpoint;
        PendingEndpoint = null;
        return endpoint;
    }

    /// <summary>値が変わったときだけ保存して変更を通知します。</summary>
    /// <remarks>責務: 観測対象プロパティの変更通知を1か所にまとめます。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">保存先のフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
