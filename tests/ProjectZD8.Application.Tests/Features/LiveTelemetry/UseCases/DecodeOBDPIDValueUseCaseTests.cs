using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.Tests.LiveTelemetry.UseCases;

/// <summary>PID定義取得と数値変換のApplication境界を検証します。</summary>
public sealed class DecodeOBDPIDValueUseCaseTests
{
    /// <summary>登録済み式と単位を変換結果へ反映します。</summary>
    /// <remarks>責務: Service/PIDに対応する式が応答バイトへ一度だけ適用されることを確認します。</remarks>
    [Fact]
    public void TestExecuteDecodesRegisteredDefinition()
    {
        var repository = new OBDPIDDefinitionRepositoryFake(
            new OBDPIDDefinition(
                service: 0x01,
                pid: 0x0C,
                nameKey: "obd.pid.01.0C.name",
                requiredByteCount: 2,
                formula: "(A * 256 + B) / 4",
                unit: "rpm",
                minimumValue: 0,
                maximumValue: 16_383.75,
                sourceURI: "https://www.elmelectronics.com/wp-content/uploads/2016/07/ELM327DSH.pdf",
                revision: 1));
        var useCase = new DecodeOBDPIDValueUseCase(repository);

        var result = useCase.Execute(0x01, 0x0C, [0x1A, 0xF8]);

        Assert.Equal(1_726, result?.Value);
        Assert.Equal("rpm", result?.Unit);
        Assert.Equal(new OBDPIDDefinitionRepositoryFake.Lookup(0x01, 0x0C), repository.LastLookup);
    }

    /// <summary>未登録PIDを推測式で変換しません。</summary>
    /// <remarks>責務: 定義が存在しないService/PIDを未対応として返すことを確認します。</remarks>
    [Fact]
    public void TestExecuteReturnsNilForUnknownPID()
    {
        var repository = new OBDPIDDefinitionRepositoryFake(null);
        var useCase = new DecodeOBDPIDValueUseCase(repository);

        var result = useCase.Execute(0x01, 0xFF, [0x00]);

        Assert.Null(result);
    }
}

/// <summary>PID変換UseCaseへ決定的な定義を返すテスト用リポジトリです。</summary>
file sealed class OBDPIDDefinitionRepositoryFake : IOBDPIDDefinitionRepository
{
    /// <summary>取得時に返す定義です。</summary>
    private readonly OBDPIDDefinition? storedDefinition;

    /// <summary>テストで返す定義を固定して生成します。</summary>
    /// <remarks>責務: 1件の取得シナリオへ返却定義を固定します。</remarks>
    /// <param name="storedDefinition">取得時に返す定義。未登録シナリオでは <c>null</c>。</param>
    public OBDPIDDefinitionRepositoryFake(OBDPIDDefinition? storedDefinition)
    {
        this.storedDefinition = storedDefinition;
    }

    /// <summary>最後に検索された複合識別子です。</summary>
    public Lookup? LastLookup { get; private set; }

    /// <summary>このテストでは使用しないPID定義一覧を返します。</summary>
    /// <remarks>責務: 単一定義読取テストで一覧要求へ空配列を返します。</remarks>
    /// <returns>常に空のPID定義一覧。</returns>
    public IReadOnlyList<OBDPIDDefinition> Definitions() => [];

    /// <summary>保存処理をテスト対象外として受け入れます。</summary>
    /// <remarks>責務: 読取専用UseCaseテストで不要な保存呼出しを明示的に無視します。</remarks>
    /// <param name="definition">使用しないPID定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
    }

    /// <summary>固定定義を返して検索キーを記録します。</summary>
    /// <remarks>責務: 1件のService/PID検索を観測して固定結果を返します。</remarks>
    /// <param name="service">検索されたService番号。</param>
    /// <param name="pid">検索されたPID番号。</param>
    /// <returns>初期化時に指定された定義。</returns>
    public OBDPIDDefinition? Definition(byte service, byte pid)
    {
        LastLookup = new Lookup(service, pid);
        return storedDefinition;
    }

    /// <summary>テストで観測するService/PID複合キーです。</summary>
    /// <param name="Service">OBD Service番号。</param>
    /// <param name="Pid">Service内PID番号。</param>
    public readonly record struct Lookup(byte Service, byte Pid);
}
