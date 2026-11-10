using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.Ports;

/// <summary>manifest作成時点の取得runtime evidenceです。</summary>
public sealed record ConnectionSessionAcquisitionRuntimeEvidence
{
    /// <summary>取得を生成するアプリとbuildのversionです。</summary>
    public AcquisitionApplicationVersion ApplicationVersion { get; }

    /// <summary>永続化readerが解釈するschema契約versionです。</summary>
    public int SchemaContractVersion { get; }

    /// <summary>端末名を含まない取得platformです。</summary>
    public ConnectionSessionAcquisitionPlatform AcquisitionPlatform { get; }

    /// <summary>runtime authorityから得たmanifest evidenceを固定します。</summary>
    /// <remarks>責務: app/build、schema契約、platformを1回のmanifest作成入力へまとめます。</remarks>
    /// <param name="applicationVersion">取得を生成するアプリとbuildのversion。</param>
    /// <param name="schemaContractVersion">永続化readerが解釈するschema契約version。</param>
    /// <param name="acquisitionPlatform">端末名を含まない取得platform。</param>
    public ConnectionSessionAcquisitionRuntimeEvidence(
        AcquisitionApplicationVersion applicationVersion,
        int schemaContractVersion,
        ConnectionSessionAcquisitionPlatform acquisitionPlatform)
    {
        ApplicationVersion = applicationVersion;
        SchemaContractVersion = schemaContractVersion;
        AcquisitionPlatform = acquisitionPlatform;
    }
}

/// <summary>manifest作成に必要なruntime evidenceを取得する能力です。</summary>
public interface IConnectionSessionAcquisitionEvidencePort
{
    /// <summary>現在実行中のapp/build、schema契約、platformを読み取ります。</summary>
    /// <remarks>責務: Production定数をApplicationで推測せずmanifest用runtime evidenceとして返します。</remarks>
    /// <returns>authorityから読み取った取得runtime evidence。</returns>
    /// <exception cref="Exception">evidence authorityを利用できない場合の実装固有エラー。</exception>
    Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence();
}
