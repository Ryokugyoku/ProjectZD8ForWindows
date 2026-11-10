using ProjectZD8.Application.DeviceConnection.State;

namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>デフォルトアダプター設定をプロセス間で保持する保存境界です。</summary>
public interface IDefaultAdapterPreferencePort
{
    /// <summary>現在保存されているデフォルトアダプター設定を読み込みます。</summary>
    /// <remarks>責務: 保存済みのデフォルトアダプター設定を1件だけ返します。</remarks>
    /// <returns>復元できた設定。未保存または不完全な場合は <c>null</c>。</returns>
    DefaultAdapterPreference? Load();

    /// <summary>指定されたデフォルトアダプター設定を保存します。</summary>
    /// <remarks>責務: 1件のデフォルトアダプター設定を次回起動用に置き換えて保存します。</remarks>
    /// <param name="preference">保存するデフォルトアダプターの識別情報。</param>
    void Save(DefaultAdapterPreference preference);
}
