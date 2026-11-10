using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】PID 定義をメモリに持つ代用の保存先です。<see cref="InterimStandardOBDPIDSeed"/> で初期化します。
/// </summary>
/// <remarks>iOS 版の <c>GRDBOBDPIDDefinitionRepository</c> の代わり。Data 層の SQLite 実装ができたら差し替えます。</remarks>
public sealed class InterimOBDPIDDefinitionRepository : IOBDPIDDefinitionRepository
{
    /// <summary>状態を守る lock です。</summary>
    private readonly Lock _gate = new();

    /// <summary>(Service, PID, 車種) ごとの定義です。</summary>
    private readonly Dictionary<(byte Service, byte Pid, string? Model), OBDPIDDefinition> _definitions = [];

    /// <summary>初期データを入れた保存先を作ります。</summary>
    public InterimOBDPIDDefinitionRepository()
    {
        foreach (var definition in InterimStandardOBDPIDSeed.Definitions)
        {
            Upsert(definition);
        }
    }

    /// <summary>すべての定義を Service・PID の順に返します。</summary>
    /// <remarks>責務: 保存済みの定義を決まった順で返します。</remarks>
    /// <returns>定義の一覧。</returns>
    public IReadOnlyList<OBDPIDDefinition> Definitions()
    {
        lock (_gate)
        {
            return _definitions.Values.OrderBy(item => item.Service).ThenBy(item => item.Pid).ToList();
        }
    }

    /// <summary>定義を追加または更新します。改訂番号が古いものでは上書きしません。</summary>
    /// <remarks>責務: 1 件の定義を改訂の後退なしで保存します。</remarks>
    /// <param name="definition">保存する定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
        lock (_gate)
        {
            var key = (definition.Service, definition.Pid, definition.VehicleModelCode);
            if (!_definitions.TryGetValue(key, out var existing) || existing.Revision <= definition.Revision)
            {
                _definitions[key] = definition;
            }
        }
    }

    /// <summary>汎用（車種専用でない）定義を 1 件返します。</summary>
    /// <remarks>責務: Service と PID に一致する汎用定義を探します。</remarks>
    /// <param name="service">Service 番号。</param>
    /// <param name="pid">PID 番号。</param>
    /// <returns>定義。なければ null。</returns>
    public OBDPIDDefinition? Definition(byte service, byte pid)
    {
        lock (_gate)
        {
            return _definitions.TryGetValue((service, pid, null), out var definition) ? definition : null;
        }
    }
}
