using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Analysis.State;

/// <summary>解析読込の現在段階です。</summary>
/// <remarks>Swift の <c>SessionLogAnalysisState.Phase</c> に対応します。C# では同名のプロパティ <see cref="SessionLogAnalysisState.Phase"/> と入れ子にできないため外へ出しています。</remarks>
public enum SessionLogAnalysisStatePhase
{
    /// <summary>セッション選択前または表示を閉じた状態です。</summary>
    Idle,
    /// <summary>iCloudからRawログを取得するユーザー確認を待っています。</summary>
    AwaitingDownloadConfirmation,
    /// <summary>iCloudからRawログを取得しています。</summary>
    Downloading,
    /// <summary>RawログとPID定義を読込中です。</summary>
    Loading,
    /// <summary>時系列サンプルの生成が完了した状態です。</summary>
    Loaded,
    /// <summary>保存先または定義を読めなかった状態です。</summary>
    Failed,
}

/// <summary>iCloud Rawログ取得前に表示する確認情報です。</summary>
/// <remarks>Swift の <c>SessionLogAnalysisState.DownloadPrompt</c> に対応します。C# では同名のプロパティ <see cref="SessionLogAnalysisState.DownloadPrompt"/> と入れ子にできないため外へ出しています。</remarks>
public sealed record SessionLogAnalysisStateDownloadPrompt
{
    /// <summary>取得対象のセッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>iCloudから取得するRaw Payloadの予定バイト数です。</summary>
    public long ByteCount { get; }

    /// <summary>取得対象と予定容量を固定して確認情報を生成します。</summary>
    /// <remarks>責務: 1件のセッションIDと予定バイト数をRaw取得確認情報へ固定します。</remarks>
    /// <param name="sessionID">取得対象のセッションID。</param>
    /// <param name="byteCount">iCloudから取得するRaw Payloadの予定バイト数。</param>
    public SessionLogAnalysisStateDownloadPrompt(ConnectionSessionID sessionID, long byteCount)
    {
        SessionID = sessionID;
        ByteCount = byteCount;
    }
}

/// <summary>保存済みセッションのPID時系列解析を画面へ公開する状態です。</summary>
public sealed record SessionLogAnalysisState
{
    /// <summary>数値化不能なPIDの理由です。</summary>
    /// <remarks>Swift の <c>SessionLogAnalysisState.TimelineSample.DecodingFailure</c> に対応します。C# では同名のプロパティ <see cref="TimelineSample.DecodingFailure"/> と入れ子にできないため1段外へ出しています。</remarks>
    public enum TimelineSampleDecodingFailure
    {
        /// <summary>保存済み定義がありません。</summary>
        MissingDefinition,
        /// <summary>定義はありますが式が確認されていません。</summary>
        UnavailableFormula,
        /// <summary>Payload長または数式評価が定義を満たしません。</summary>
        InvalidPayload,
    }

    /// <summary>1件の時系列PID表示値です。</summary>
    public sealed record TimelineSample
    {
        /// <summary>セッション内のRawログ順序です。</summary>
        public long Sequence { get; }
        /// <summary>応答を観測した実時間です。</summary>
        public DateTimeOffset ObservedAt { get; }
        /// <summary>OBD Service番号です。</summary>
        public byte Service { get; }
        /// <summary>Service内PID番号です。</summary>
        public byte Pid { get; }
        /// <summary>PID定義が提供する表示名キーです。</summary>
        public string? NameKey { get; }
        /// <summary>数式変換後の数値です。変換できない場合は <see langword="null"/> です。</summary>
        public double? Value { get; }
        /// <summary>数値の単位です。</summary>
        public string? Unit { get; }
        /// <summary>車種専用PIDの場合に表示する型式です。</summary>
        public string? VehicleModelCode { get; }
        /// <summary>数式を適用する前の応答データです。</summary>
        public IReadOnlyList<byte> Payload { get; }
        /// <summary>数値化できなかった理由です。</summary>
        public TimelineSampleDecodingFailure? DecodingFailure { get; }

        /// <summary>画面の行識別子です。</summary>
        public long Id => Sequence;

        /// <summary>解析済みPID値を生成します。</summary>
        /// <remarks>
        /// 責務: 1件のRaw PID応答を表示に必要な変換結果と来歴へ固定します。
        /// C# では省略できる引数を後ろに置くため、<paramref name="vehicleModelCode"/> は Swift と違い最後の引数です。
        /// </remarks>
        /// <param name="sequence">セッション内の記録順序。</param>
        /// <param name="observedAt">応答観測時刻。</param>
        /// <param name="service">OBD Service番号。</param>
        /// <param name="pid">Service内PID番号。</param>
        /// <param name="nameKey">PID表示名キー。</param>
        /// <param name="value">数式変換後の値。</param>
        /// <param name="unit">数値単位。</param>
        /// <param name="payload">元の応答データ。</param>
        /// <param name="decodingFailure">数値化不能理由。</param>
        /// <param name="vehicleModelCode">車種専用PIDの適用型式。</param>
        public TimelineSample(
            long sequence,
            DateTimeOffset observedAt,
            byte service,
            byte pid,
            string? nameKey,
            double? value,
            string? unit,
            IReadOnlyList<byte> payload,
            TimelineSampleDecodingFailure? decodingFailure,
            string? vehicleModelCode = null)
        {
            Sequence = sequence;
            ObservedAt = observedAt;
            Service = service;
            Pid = pid;
            NameKey = nameKey;
            Value = value;
            Unit = unit;
            VehicleModelCode = vehicleModelCode;
            Payload = payload.ToArray();
            DecodingFailure = decodingFailure;
        }

        /// <summary>応答データを含む全項目が等しいかを返します。</summary>
        /// <remarks>責務: Swift の値型と同じく応答データを要素ごとに比較します。</remarks>
        /// <param name="other">比較対象。</param>
        /// <returns>全項目が等しければ <see langword="true"/>。</returns>
        public bool Equals(TimelineSample? other) =>
            other is not null
            && Sequence == other.Sequence
            && ObservedAt == other.ObservedAt
            && Service == other.Service
            && Pid == other.Pid
            && string.Equals(NameKey, other.NameKey, StringComparison.Ordinal)
            && Value == other.Value
            && string.Equals(Unit, other.Unit, StringComparison.Ordinal)
            && string.Equals(VehicleModelCode, other.VehicleModelCode, StringComparison.Ordinal)
            && Payload.SequenceEqual(other.Payload)
            && DecodingFailure == other.DecodingFailure;

        /// <summary>等価性と整合するハッシュ値を返します。</summary>
        /// <remarks>責務: 応答データを含めてハッシュ値を合成します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Sequence);
            hash.Add(ObservedAt);
            hash.Add(Service);
            hash.Add(Pid);
            hash.Add(NameKey);
            hash.Add(Value);
            hash.Add(Unit);
            hash.Add(VehicleModelCode);
            foreach (var value in Payload)
            {
                hash.Add(value);
            }
            hash.Add(DecodingFailure);
            return hash.ToHashCode();
        }
    }

    /// <summary>現在の解析段階です。</summary>
    public SessionLogAnalysisStatePhase Phase { get; init; } = SessionLogAnalysisStatePhase.Idle;
    /// <summary>現在表示しているセッションIDです。</summary>
    public ConnectionSessionID? SessionID { get; init; }
    /// <summary>ユーザー確認待ちのiCloud取得情報です。</summary>
    public SessionLogAnalysisStateDownloadPrompt? DownloadPrompt { get; init; }
    /// <summary>iCloud Asset取得率です。取得前は <see langword="null"/> です。</summary>
    public double? DownloadProgress { get; init; }
    /// <summary>セッション内の取得順序で昇順にしたPID時系列です。</summary>
    public IReadOnlyList<TimelineSample> Timeline { get; init; } = Array.Empty<TimelineSample>();
    /// <summary>数式変換を完了した時系列サンプル件数です。</summary>
    public int DecodedSampleCount { get; init; }
    /// <summary>読込済みRawログの総件数です。Rawログ読込前は <see langword="null"/> です。</summary>
    /// <remarks>Domain の <c>EntryCount</c> が <see langword="long"/> を返すため <see langword="long"/> で保持します（Swift は <c>Int</c>）。</remarks>
    public long? TotalSampleCount { get; init; }
    /// <summary>数式変換できずRawのまま保持するサンプル件数です。</summary>
    public int RawOnlySampleCount => Timeline.Count - DecodedSampleCount;
    /// <summary>数値化済みサンプルをService/PID単位へまとめた折れ線系列です。</summary>
    public IReadOnlyList<SessionPIDSeries> PidSeries { get; init; } = Array.Empty<SessionPIDSeries>();
    /// <summary>速度・回転数の滞在傾向と車速積算距離をまとめた走行サマリーです。</summary>
    public SessionPerformanceSummary PerformanceSummary { get; init; } = SessionPerformanceSummary.Empty;
    /// <summary>取得できたPIDを車両部品・系統の観察目的へ結び付けた表示状態です。</summary>
    public IReadOnlyList<SessionComponentInsight> ComponentInsights { get; init; } = Array.Empty<SessionComponentInsight>();
    /// <summary>近接時刻で対応付けられる代表PID組の散布図状態です。</summary>
    public IReadOnlyList<SessionPIDRelationship> Relationships { get; init; } = Array.Empty<SessionPIDRelationship>();
    /// <summary>保存元または変換定義の読込失敗を示すキーです。</summary>
    public string? FailureKey { get; init; }

    /// <summary>一覧を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく時系列と各表示一覧を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(SessionLogAnalysisState? other) =>
        other is not null
        && Phase == other.Phase
        && SessionID == other.SessionID
        && DownloadPrompt == other.DownloadPrompt
        && DownloadProgress == other.DownloadProgress
        && Timeline.SequenceEqual(other.Timeline)
        && DecodedSampleCount == other.DecodedSampleCount
        && TotalSampleCount == other.TotalSampleCount
        && PidSeries.SequenceEqual(other.PidSeries)
        && PerformanceSummary == other.PerformanceSummary
        && ComponentInsights.SequenceEqual(other.ComponentInsights)
        && Relationships.SequenceEqual(other.Relationships)
        && string.Equals(FailureKey, other.FailureKey, StringComparison.Ordinal);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 主要項目と一覧の件数からハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Phase, SessionID, DownloadPrompt, DownloadProgress, Timeline.Count, DecodedSampleCount, TotalSampleCount, FailureKey);
}
