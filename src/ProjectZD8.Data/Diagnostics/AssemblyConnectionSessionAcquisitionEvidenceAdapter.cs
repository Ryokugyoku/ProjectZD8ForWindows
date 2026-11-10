using System.Reflection;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Diagnostics;

/// <summary>アプリのバージョンと注入済みschema authorityから取得runtime evidenceを生成します。</summary>
/// <remarks>
/// iOS版 <c>BundleConnectionSessionAcquisitionEvidenceAdapter</c>（Data/Mapping）の移植です。iOS版はBundleの
/// <c>CFBundleShortVersionString</c> と <c>CFBundleVersion</c> を読みます。Windows版は組み立てがパッケージの版
/// （<c>Package.Current.Id.Version</c> など）を文字列で渡すか、<see cref="FromAssembly"/> でアセンブリ属性から読みます。
/// 取得platformはDomainにWindowsの値がまだないため、組み立てが注入します。
/// </remarks>
public sealed class AssemblyConnectionSessionAcquisitionEvidenceAdapter : IConnectionSessionAcquisitionEvidencePort
{
    /// <summary>ユーザー向けのアプリversionです（読めない場合は <see langword="null"/>）。</summary>
    private readonly string? _marketingVersion;
    /// <summary>配布build versionです（読めない場合は <see langword="null"/>）。</summary>
    private readonly string? _buildVersion;
    /// <summary>保存readerが解釈するschema契約versionです。</summary>
    private readonly int _schemaContractVersion;
    /// <summary>端末名を含まない現在platformです。</summary>
    private readonly ConnectionSessionAcquisitionPlatform _acquisitionPlatform;

    /// <summary>version文字列、schema version、platformを固定します。</summary>
    /// <remarks>責務: Production runtime evidenceの3つのauthorityを1件のadapterへ注入します。</remarks>
    /// <param name="marketingVersion">ユーザー向けのアプリversion。</param>
    /// <param name="buildVersion">配布build version。</param>
    /// <param name="schemaContractVersion">保存readerが解釈するschema契約version。</param>
    /// <param name="acquisitionPlatform">端末名を含まない現在platform。</param>
    public AssemblyConnectionSessionAcquisitionEvidenceAdapter(
        string? marketingVersion,
        string? buildVersion,
        int schemaContractVersion,
        ConnectionSessionAcquisitionPlatform acquisitionPlatform)
    {
        _marketingVersion = marketingVersion;
        _buildVersion = buildVersion;
        _schemaContractVersion = schemaContractVersion;
        _acquisitionPlatform = acquisitionPlatform;
    }

    /// <summary>アセンブリ属性からversionを読んでadapterを生成します。</summary>
    /// <remarks>
    /// 責務: <see cref="AssemblyInformationalVersionAttribute"/>（<c>+</c>以降のビルドメタデータを除く）をmarketing version、
    /// <see cref="AssemblyFileVersionAttribute"/> をbuild versionとして読みます。
    /// </remarks>
    /// <param name="assembly">versionを読むアセンブリ。</param>
    /// <param name="schemaContractVersion">保存readerが解釈するschema契約version。</param>
    /// <param name="acquisitionPlatform">端末名を含まない現在platform。</param>
    /// <returns>adapter。</returns>
    public static AssemblyConnectionSessionAcquisitionEvidenceAdapter FromAssembly(
        Assembly assembly,
        int schemaContractVersion,
        ConnectionSessionAcquisitionPlatform acquisitionPlatform)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var marketing = informational?.Split('+')[0];
        var build = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        return new AssemblyConnectionSessionAcquisitionEvidenceAdapter(marketing, build, schemaContractVersion, acquisitionPlatform);
    }

    /// <summary>versionと注入済みschema/platformを返します。</summary>
    /// <remarks>責務: 現在processのversion情報をmanifest用runtime evidenceへ変換します。</remarks>
    /// <returns>app/build、schema契約、platformを持つruntime evidence。</returns>
    /// <exception cref="AssemblyConnectionSessionAcquisitionEvidenceException">versionが欠落している場合は <c>VersionUnavailable</c>。</exception>
    public Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence()
    {
        if (string.IsNullOrEmpty(_marketingVersion) || string.IsNullOrEmpty(_buildVersion))
        {
            return Task.FromException<ConnectionSessionAcquisitionRuntimeEvidence>(
                new AssemblyConnectionSessionAcquisitionEvidenceException(AssemblyConnectionSessionAcquisitionEvidenceError.VersionUnavailable));
        }
        try
        {
            return Task.FromResult(new ConnectionSessionAcquisitionRuntimeEvidence(
                new AcquisitionApplicationVersion(_marketingVersion, _buildVersion),
                _schemaContractVersion,
                _acquisitionPlatform));
        }
        catch (Exception error)
        {
            return Task.FromException<ConnectionSessionAcquisitionRuntimeEvidence>(error);
        }
    }
}
