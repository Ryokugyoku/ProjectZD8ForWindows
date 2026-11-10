namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 取得開始時に固定する順序付きPID集合です。
/// </summary>
public sealed record OrderedAcquisitionPIDSet
{
    /// <summary>
    /// 要求順を保持する重複のないService/PID一覧です。
    /// </summary>
    public IReadOnlyList<OBDPIDRequest> Requests { get; }

    /// <summary>
    /// 重複を拒否して順序付きPID集合を生成します。
    /// </summary>
    /// <remarks>責務: 取得対象のService/PIDを入力順のまま一意な集合へ固定します。</remarks>
    /// <param name="requests">取得開始時の要求順に並べたService/PID一覧。</param>
    /// <exception cref="OrderedAcquisitionPIDSetException">同じService/PIDが複数含まれる場合は <see cref="OrderedAcquisitionPIDSetError.DuplicateRequest"/>。</exception>
    public OrderedAcquisitionPIDSet(IReadOnlyList<OBDPIDRequest> requests)
    {
        if (new HashSet<OBDPIDRequest>(requests).Count != requests.Count)
        {
            throw new OrderedAcquisitionPIDSetException(OrderedAcquisitionPIDSetError.DuplicateRequest);
        }

        Requests = requests.ToArray();
    }

    /// <summary>
    /// 要求一覧が要素順で一致するかを返します。
    /// </summary>
    /// <remarks>責務: 要求一覧を要素順で比較し、Swift 版の <c>Equatable</c> と同じ等価性を提供します。</remarks>
    /// <param name="other">比較対象のPID集合。</param>
    /// <returns>要求一覧が要素順で一致する場合は <c>true</c>。</returns>
    public bool Equals(OrderedAcquisitionPIDSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && Requests.SequenceEqual(other.Requests);
    }

    /// <summary>
    /// 等価性と整合するハッシュ値を返します。
    /// </summary>
    /// <remarks>責務: 要求一覧の要素から <see cref="Equals(OrderedAcquisitionPIDSet?)"/> と整合するハッシュ値を計算します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var request in Requests)
        {
            hash.Add(request);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// 順序付きPID集合を確定できない理由です。
/// </summary>
public enum OrderedAcquisitionPIDSetError
{
    /// <summary>
    /// 同じService/PIDが複数指定されています。
    /// </summary>
    DuplicateRequest,
}

/// <summary>
/// 順序付きPID集合を確定できないことを表す例外です。
/// </summary>
public sealed class OrderedAcquisitionPIDSetException : Exception
{
    /// <summary>
    /// 順序付きPID集合を確定できない理由です。
    /// </summary>
    public OrderedAcquisitionPIDSetError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件の順序付きPID集合失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">順序付きPID集合を確定できない理由。</param>
    public OrderedAcquisitionPIDSetException(OrderedAcquisitionPIDSetError reason)
        : base($"{nameof(OrderedAcquisitionPIDSetError)}.{reason}")
    {
        Reason = reason;
    }
}
