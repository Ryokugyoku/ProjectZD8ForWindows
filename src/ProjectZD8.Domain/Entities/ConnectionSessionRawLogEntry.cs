namespace ProjectZD8.Domain.Entities;

/// <summary>1件のOBD要求に対応する未デコード応答ログです。</summary>
public sealed record ConnectionSessionRawLogEntry
{
    /// <summary>セッション内で単調増加する記録順序です。</summary>
    public long Sequence { get; }
    /// <summary>応答を観測した実時間です。</summary>
    public DateTimeOffset ObservedAt { get; }
    /// <summary>要求バッチ開始から応答群を受け取るまでの単調時間です。</summary>
    public ulong BatchElapsedNanoseconds { get; }
    /// <summary>OBD Service番号です。</summary>
    public byte Service { get; }
    /// <summary>Service内PID番号です。</summary>
    public byte Pid { get; }
    /// <summary>数値化や単位変換を適用していない応答データバイトです。</summary>
    public IReadOnlyList<byte> Payload { get; }

    /// <summary>未デコード応答をセッション内の記録順序へ固定して生成します。</summary>
    /// <remarks>責務: 1件のRaw OBD応答を学習元として再現可能なセッションログへ変換します。</remarks>
    /// <param name="sequence">セッション内で重複しない単調増加番号。</param>
    /// <param name="observedAt">応答を観測した実時間。</param>
    /// <param name="batchElapsedNanoseconds">要求バッチ開始から応答群受信までの単調時間。</param>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <param name="payload">未デコード応答データバイト。</param>
    public ConnectionSessionRawLogEntry(
        long sequence,
        DateTimeOffset observedAt,
        ulong batchElapsedNanoseconds,
        byte service,
        byte pid,
        IReadOnlyList<byte> payload)
    {
        Sequence = sequence;
        ObservedAt = observedAt;
        BatchElapsedNanoseconds = batchElapsedNanoseconds;
        Service = service;
        Pid = pid;
        Payload = payload.ToArray();
    }

    /// <summary>応答データバイトを含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく応答データバイトを要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(ConnectionSessionRawLogEntry? other) =>
        other is not null
        && Sequence == other.Sequence
        && ObservedAt == other.ObservedAt
        && BatchElapsedNanoseconds == other.BatchElapsedNanoseconds
        && Service == other.Service
        && Pid == other.Pid
        && Payload.SequenceEqual(other.Payload);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 応答データバイトを含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Sequence);
        hash.Add(ObservedAt);
        hash.Add(BatchElapsedNanoseconds);
        hash.Add(Service);
        hash.Add(Pid);
        foreach (var value in Payload)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}
