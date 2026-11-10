using System.Globalization;
using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Domain.Policies;

/// <summary>登録車両の識別値をZD8型式へ安全に照合します。</summary>
public sealed class ZD8VehicleModelPolicy
{
    /// <summary>専用PID表示に使用する型式文字列です。</summary>
    public const string ModelCode = "ZD8";

    /// <summary>確認済み海外VIN接頭辞です。</summary>
    private const string OverseasVinPrefix = "JF1ZD";

    /// <summary>国内型式または確認済み海外VIN接頭辞をZD8へ照合します。</summary>
    /// <remarks>責務: 1件の車両識別文字列をZD8型式の適用可否へ変換します。</remarks>
    /// <param name="identifier">VINまたはOBD由来の車両識別文字列。</param>
    /// <returns><c>ZD8</c> または <c>JF1ZD</c> で始まる場合は <see langword="true"/>。</returns>
    public bool Matches(string? identifier)
    {
        // Swift の trimmingCharacters(in: .whitespacesAndNewlines) と string.Trim() はどちらも Unicode White_Space を除去する。
        var normalized = identifier?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized)) { return false; }
        return HasCharacterPrefix(normalized, ModelCode) || HasCharacterPrefix(normalized, OverseasVinPrefix);
    }

    /// <summary>車両プロフィールがZD8専用PIDの適用対象か判定します。</summary>
    /// <remarks>責務: 1台の登録車両が持つVINとOBD識別子をZD8型式へ照合します。</remarks>
    /// <param name="vehicle">判定する登録車両プロフィール。</param>
    /// <returns>いずれかの識別値がZD8を示す場合は <see langword="true"/>。</returns>
    public bool Matches(VehicleProfile vehicle) =>
        Matches(vehicle.Vin) || Matches(vehicle.ObdIdentifier);

    /// <summary>Swift の <c>String.hasPrefix</c> を ASCII 接頭辞について再現します。</summary>
    /// <remarks>
    /// 責務: 書記素クラスタ単位で接頭辞が一致するか（接頭辞の直後に結合文字が続かないか）を判定します。
    /// </remarks>
    /// <param name="value">判定する文字列。</param>
    /// <param name="prefix">ASCII だけからなる接頭辞。</param>
    /// <returns>書記素クラスタ単位で接頭辞が一致する場合は <see langword="true"/>。</returns>
    private static bool HasCharacterPrefix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) { return false; }
        if (value.Length == prefix.Length) { return true; }
        return StringInfo.GetNextTextElementLength(value, prefix.Length - 1) == 1;
    }
}
