using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】既定アダプターの設定をメモリだけに持つ保存先です。
/// </summary>
/// <remarks>デバイス担当の <c>JsonFileDefaultAdapterPreferenceStore</c> ができたら差し替えます。</remarks>
public sealed class InterimDefaultAdapterPreferenceStore : IDefaultAdapterPreferencePort
{
    /// <summary>保存済みの設定です。</summary>
    private DefaultAdapterPreference? _preference;

    /// <summary>保存済みの設定を返します。</summary>
    /// <remarks>責務: メモリ上の既定アダプター設定を返します。</remarks>
    /// <returns>設定。なければ null。</returns>
    public DefaultAdapterPreference? Load() => _preference;

    /// <summary>設定を保存します。</summary>
    /// <remarks>責務: 既定アダプター設定をメモリへ置きます。</remarks>
    /// <param name="preference">保存する設定。</param>
    public void Save(DefaultAdapterPreference preference) => _preference = preference;

    /// <summary>設定を消します。</summary>
    /// <remarks>責務: アカウント削除・ログアウト時に既定アダプター設定を消します。</remarks>
    public void Remove() => _preference = null;
}
