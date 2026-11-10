using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.UseCases;

/// <summary>デフォルトアダプター設定の保存、復元、検出候補との照合を提供します。</summary>
public sealed class DefaultAdapterPreferenceUseCase
{
    /// <summary>デフォルト設定を保持する保存境界です。</summary>
    private readonly IDefaultAdapterPreferencePort preferencePort;

    /// <summary>保存境界を注入してデフォルト設定ユースケースを生成します。</summary>
    /// <remarks>責務: デフォルトアダプター設定のライフサイクルを1件の保存境界へ結び付けます。</remarks>
    /// <param name="preferencePort">デフォルト設定を保持する保存境界。</param>
    public DefaultAdapterPreferenceUseCase(IDefaultAdapterPreferencePort preferencePort)
    {
        this.preferencePort = preferencePort;
    }

    /// <summary>現在保存されているデフォルトアダプター設定を返します。</summary>
    /// <remarks>責務: デフォルト設定の読込を保存境界へ委譲します。</remarks>
    /// <returns>復元できた設定。未保存または不完全な場合は <c>null</c>。</returns>
    public DefaultAdapterPreference? Load() => preferencePort.Load();

    /// <summary>検出済み候補を次回起動用のデフォルト設定として保存します。</summary>
    /// <remarks>責務: 1件の検出済み候補をデフォルト設定へ変換して保存境界へ渡します。</remarks>
    /// <param name="adapter">ユーザーがデフォルトとして確定した候補。</param>
    /// <returns>保存境界へ渡したデフォルト設定。</returns>
    public DefaultAdapterPreference Save(DiscoveredAdapter adapter)
    {
        var preference = new DefaultAdapterPreference(adapter);
        preferencePort.Save(preference);
        return preference;
    }

    /// <summary>保存済みデフォルトと一致する検出候補を返します。</summary>
    /// <remarks>責務: 1件のデフォルト設定を最新の検出候補一覧と照合します。</remarks>
    /// <param name="preference">照合する保存済みデフォルト設定。</param>
    /// <param name="adapters">最新探索で検出した候補一覧。</param>
    /// <returns>保存済み識別情報と一致する候補。存在しない場合は <c>null</c>。</returns>
    public DiscoveredAdapter? DetectedAdapter(
        DefaultAdapterPreference preference,
        IReadOnlyList<DiscoveredAdapter> adapters) =>
        adapters.FirstOrDefault(preference.Matches);
}
