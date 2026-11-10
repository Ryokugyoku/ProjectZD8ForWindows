using System.Reflection;
using System.Threading.Tasks;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】取得証拠に記録するアプリの版と取得端末の種類を返す境界です。
/// </summary>
/// <remarks>
/// iOS 版の <c>BundleConnectionSessionAcquisitionEvidenceAdapter</c> の代わり（ネットワーク担当の Data/Mapping が本物）。
/// 版はアセンブリの版を使います。取得端末の種類は、Domain に Windows がないため macOS 構成と同じ値にしています（検討事項）。
/// </remarks>
public sealed class InterimConnectionSessionAcquisitionEvidence : IConnectionSessionAcquisitionEvidencePort
{
    /// <summary>schema 契約の版です。</summary>
    private readonly int _schemaContractVersion;

    /// <summary>取得端末の種類です。</summary>
    private readonly ConnectionSessionAcquisitionPlatform _platform;

    /// <summary>版と端末の種類を指定して作ります。</summary>
    /// <param name="schemaContractVersion">schema 契約の版。</param>
    /// <param name="platform">取得端末の種類。</param>
    public InterimConnectionSessionAcquisitionEvidence(int schemaContractVersion, ConnectionSessionAcquisitionPlatform platform)
    {
        _schemaContractVersion = schemaContractVersion;
        _platform = platform;
    }

    /// <summary>取得証拠に使う実行時の情報を返します。</summary>
    /// <remarks>責務: アセンブリの版と構成値を、取得証拠の実行時情報へ変換します。</remarks>
    /// <returns>実行時の情報。</returns>
    public Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var marketing = version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        var build = version is null ? "1" : $"{version.Build}";
        return Task.FromResult(new ConnectionSessionAcquisitionRuntimeEvidence(
            new AcquisitionApplicationVersion(marketing, build),
            _schemaContractVersion,
            _platform));
    }
}
