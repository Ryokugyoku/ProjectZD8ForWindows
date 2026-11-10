using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>取得開始時点で確認されたPID対応状態です。</summary>
public enum AcquisitionPIDCapabilitySupport
{
    /// <summary>応答などの証拠により対応が確認されています。</summary>
    Supported,
    /// <summary>protocol上の明示的証拠により非対応が確認されています。</summary>
    Unsupported,
    /// <summary>未探索、曖昧な応答、または探索失敗により判定できません。</summary>
    Indeterminate,
}

/// <summary><see cref="AcquisitionPIDCapabilitySupport"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class AcquisitionPIDCapabilitySupportRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this AcquisitionPIDCapabilitySupport value) => value switch
    {
        AcquisitionPIDCapabilitySupport.Supported => "supported",
        AcquisitionPIDCapabilitySupport.Unsupported => "unsupported",
        AcquisitionPIDCapabilitySupport.Indeterminate => "indeterminate",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionPIDCapabilitySupport value)
    {
        switch (rawValue)
        {
            case "supported": value = AcquisitionPIDCapabilitySupport.Supported; return true;
            case "unsupported": value = AcquisitionPIDCapabilitySupport.Unsupported; return true;
            case "indeterminate": value = AcquisitionPIDCapabilitySupport.Indeterminate; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>1件のPIDについて取得時点の変換意味と能力選択を固定します。</summary>
public sealed record AcquisitionPIDDefinitionSnapshot
{
    /// <summary>定義を結び付けるService/PIDです。</summary>
    public OBDPIDRequest Request { get; }
    /// <summary>取得時に確認された対応状態です。<see langword="null"/> はlegacy欠落を表します。</summary>
    public AcquisitionPIDCapabilitySupport? CapabilitySupport { get; }
    /// <summary>今回の継続収集対象かどうかです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public bool? IsCollectionEnabled { get; }
    /// <summary>取得時PID定義のrevisionです。<see langword="null"/> は推測禁止のlegacy欠落を表します。</summary>
    public int? DefinitionRevision { get; }
    /// <summary>式評価に必要なbyte数です。<see langword="null"/> は取得時証拠の欠落を表します。</summary>
    public int? RequiredByteCount { get; }
    /// <summary>再構築可能な取得時式identityです。<see langword="null"/> は取得時証拠の欠落を表します。</summary>
    public AcquisitionPIDDefinitionIdentity? DefinitionIdentity { get; }
    /// <summary>取得時の単位です。<see langword="null"/> は取得時証拠の欠落を表します。</summary>
    public string? Unit { get; }
    /// <summary>取得時の有効範囲です。<see langword="null"/> は範囲metadata自体の欠落を表します。</summary>
    public AcquisitionPIDValidityRange? ValidityRange { get; }

    /// <summary>取得時PID定義snapshotを生成します。</summary>
    /// <remarks>責務: 1件のService/PIDに対する取得時の能力、選択、変換契約を不変値へ固定します。</remarks>
    /// <param name="request">定義を結び付けるService/PID。</param>
    /// <param name="capabilitySupport">証拠に基づく対応状態、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="isCollectionEnabled">今回の収集選択、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="definitionRevision">正の取得時revision、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="requiredByteCount">正の必要byte数、または取得時証拠欠落を示す <see langword="null"/>。</param>
    /// <param name="definitionIdentity">再構築可能な取得時式identity、または取得時証拠欠落を示す <see langword="null"/>。</param>
    /// <param name="unit">取得時の単位、または取得時証拠欠落を示す <see langword="null"/>。</param>
    /// <param name="validityRange">取得時の明示範囲、範囲なし、またはmetadata欠落を示す <see langword="null"/>。</param>
    /// <exception cref="AcquisitionPIDDefinitionSnapshotException">revisionまたは必要byte数が正でない場合（<see cref="AcquisitionPIDDefinitionSnapshotError.InvalidNumericContract"/>）。</exception>
    public AcquisitionPIDDefinitionSnapshot(
        OBDPIDRequest request,
        AcquisitionPIDCapabilitySupport? capabilitySupport,
        bool? isCollectionEnabled,
        int? definitionRevision,
        int? requiredByteCount,
        AcquisitionPIDDefinitionIdentity? definitionIdentity,
        string? unit,
        AcquisitionPIDValidityRange? validityRange)
    {
        if (definitionRevision is <= 0 || requiredByteCount is <= 0)
        {
            throw new AcquisitionPIDDefinitionSnapshotException(AcquisitionPIDDefinitionSnapshotError.InvalidNumericContract);
        }
        Request = request;
        CapabilitySupport = capabilitySupport;
        IsCollectionEnabled = isCollectionEnabled;
        DefinitionRevision = definitionRevision;
        RequiredByteCount = requiredByteCount;
        DefinitionIdentity = definitionIdentity;
        Unit = unit;
        ValidityRange = validityRange;
    }
}

/// <summary>PID定義snapshotを確定できない理由です。</summary>
public enum AcquisitionPIDDefinitionSnapshotError
{
    /// <summary>revisionまたは必要byte数が正ではありません。</summary>
    InvalidNumericContract,
}

/// <summary>PID定義snapshotを確定できないことを表す例外です。</summary>
public sealed class AcquisitionPIDDefinitionSnapshotException : Exception
{
    /// <summary>確定できない理由です。</summary>
    public AcquisitionPIDDefinitionSnapshotError Reason { get; }

    /// <summary>理由を指定して例外を生成します。</summary>
    /// <remarks>責務: Swift の <c>AcquisitionPIDDefinitionSnapshotError</c> のケースを例外として運びます。</remarks>
    /// <param name="reason">確定できない理由。</param>
    public AcquisitionPIDDefinitionSnapshotException(AcquisitionPIDDefinitionSnapshotError reason)
        : base(reason.ToString())
    {
        Reason = reason;
    }
}
