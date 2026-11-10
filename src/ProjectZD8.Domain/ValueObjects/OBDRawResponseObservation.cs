namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// セッション割当て前にApplicationが受け取る未デコードOBD応答です。
/// </summary>
public sealed record OBDRawResponseObservation
{
    /// <summary>
    /// 応答を観測した実時間です。
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// 要求バッチ開始から応答群受信までの単調時間です。
    /// </summary>
    public ulong BatchElapsedNanoseconds { get; }

    /// <summary>
    /// 応答対象のOBD要求です。
    /// </summary>
    public OBDPIDRequest Request { get; }

    /// <summary>
    /// 数値化前の応答データバイトです。
    /// </summary>
    public IReadOnlyList<byte> Payload { get; }

    /// <summary>
    /// 取得境界のRaw応答をセッション未割当て観測として生成します。
    /// </summary>
    /// <remarks>責務: 1件のOBD応答と観測時刻をLoggingへ渡せる不変値へまとめます。</remarks>
    /// <param name="observedAt">応答を観測した実時間。</param>
    /// <param name="batchElapsedNanoseconds">要求バッチ開始から応答群受信までの単調時間。</param>
    /// <param name="request">応答対象のService/PID要求。</param>
    /// <param name="payload">未デコード応答データバイト。</param>
    public OBDRawResponseObservation(
        DateTimeOffset observedAt,
        ulong batchElapsedNanoseconds,
        OBDPIDRequest request,
        IReadOnlyList<byte> payload)
    {
        ObservedAt = observedAt;
        BatchElapsedNanoseconds = batchElapsedNanoseconds;
        Request = request;
        Payload = payload.ToArray();
    }

    /// <summary>
    /// 全項目が一致するかを返します。
    /// </summary>
    /// <remarks>責務: 応答データバイトを要素順で比較し、Swift 版の <c>Equatable</c> と同じ等価性を提供します。</remarks>
    /// <param name="other">比較対象の観測。</param>
    /// <returns>全項目が一致する場合は <c>true</c>。</returns>
    public bool Equals(OBDRawResponseObservation? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && ObservedAt.Equals(other.ObservedAt)
            && BatchElapsedNanoseconds == other.BatchElapsedNanoseconds
            && Request == other.Request
            && Payload.SequenceEqual(other.Payload);
    }

    /// <summary>
    /// 等価性と整合するハッシュ値を返します。
    /// </summary>
    /// <remarks>責務: 応答データバイトを含めて <see cref="Equals(OBDRawResponseObservation?)"/> と整合するハッシュ値を計算します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ObservedAt);
        hash.Add(BatchElapsedNanoseconds);
        hash.Add(Request);
        foreach (var value in Payload)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
