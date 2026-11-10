using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.Ports;

/// <summary>1件のPID要求についてTransport境界で直接確認できた結果です。</summary>
public abstract record OBDPIDRequestTransportOutcome
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: ケースをこのファイルの入れ子レコードだけに限定します。</remarks>
    private OBDPIDRequestTransportOutcome()
    {
    }

    /// <summary>要求と一致する正応答payloadを受信しました。</summary>
    public sealed record Responded : OBDPIDRequestTransportOutcome
    {
        /// <summary>受信した正応答payloadです。</summary>
        public IReadOnlyList<byte> Payload { get; }

        /// <summary>受信payloadを固定します。</summary>
        /// <remarks>責務: 正応答payloadを変更されない複製として保持します。</remarks>
        /// <param name="payload">受信した正応答payload。</param>
        public Responded(IReadOnlyList<byte> payload)
        {
            Payload = payload.ToArray();
        }

        /// <summary>payloadの内容で等価性を判定します。</summary>
        /// <remarks>責務: Swift の Equatable と同じくバイト列の内容で比較します。</remarks>
        /// <param name="other">比較対象。</param>
        /// <returns>payloadが同じ内容なら <c>true</c>。</returns>
        public bool Equals(Responded? other) =>
            other is not null && Payload.SequenceEqual(other.Payload);

        /// <summary>payloadの内容からハッシュ値を計算します。</summary>
        /// <remarks>責務: 内容比較の等価性と矛盾しないハッシュ値を返します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in Payload)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>protocol上の明示証拠により要求PID非対応を確認しました。</summary>
    public sealed record Unsupported : OBDPIDRequestTransportOutcome;

    /// <summary>要求送信後に応答期限が切れました。</summary>
    public sealed record TimedOut : OBDPIDRequestTransportOutcome;

    /// <summary>要求送信後に呼出元の取消を確認しました。</summary>
    public sealed record Cancelled : OBDPIDRequestTransportOutcome;

    /// <summary>要求送信後にTransport接続を失いました。</summary>
    public sealed record TransportFailure : OBDPIDRequestTransportOutcome;

    /// <summary>応答文字列は得ましたが非対応または形式不正を安全に分類できません。</summary>
    public sealed record UnclassifiedResponse : OBDPIDRequestTransportOutcome;
}

/// <summary>1件のPID要求とTransport観測結果を対応付けます。</summary>
public sealed record OBDPIDRequestTransportObservation
{
    /// <summary>観測対象のService/PID要求です。</summary>
    public OBDPIDRequest Request { get; }

    /// <summary>Transport境界で直接確認した結果です。</summary>
    public OBDPIDRequestTransportOutcome Outcome { get; }

    /// <summary>PID要求と確認済みTransport結果を固定します。</summary>
    /// <remarks>責務: 1件の要求を推測を含まないTransport観測結果へ対応付けます。</remarks>
    /// <param name="request">観測対象のService/PID要求。</param>
    /// <param name="outcome">Transport境界で直接確認した結果。</param>
    public OBDPIDRequestTransportObservation(OBDPIDRequest request, OBDPIDRequestTransportOutcome outcome)
    {
        Request = request;
        Outcome = outcome;
    }
}
