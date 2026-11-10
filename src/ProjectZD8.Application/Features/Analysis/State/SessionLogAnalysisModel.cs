using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.Analysis.Actions;
using ProjectZD8.Application.Analysis.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Analysis.State;

/// <summary>セッションRawログ解析の状態遷移を保持します。</summary>
/// <remarks>
/// UI スレッド（単一スレッド）から呼ぶ前提です（Swift 版の <c>@MainActor</c>）。
/// Swift 版の <c>@Observable</c> に対応し、<see cref="State"/> の変更を <see cref="PropertyChanged"/> で通知します。
/// 解析処理は Swift と同じく最初に <see cref="Task.Yield"/> で一度呼び出し元へ戻ってから始まるため、
/// <see cref="Send"/> の直後は読込開始時の状態（<see cref="SessionLogAnalysisStatePhase.Downloading"/> など）のままです。
/// </remarks>
public sealed class SessionLogAnalysisModel : INotifyPropertyChanged
{
    /// <summary>画面へ公開する解析状態です。</summary>
    private SessionLogAnalysisState _state;
    /// <summary>保存済みログを時系列表示へ変換するユースケースです。</summary>
    private readonly DecodeSessionLogTimelineUseCase _decodeTimeline;
    /// <summary>解析前にRawログをオンデマンドで利用可能にするユースケースです。</summary>
    private readonly PrepareConnectionSessionRawLogUseCase? _prepareRawLog;
    /// <summary>現在実行中の逐次解析処理です。</summary>
    private Task? _loadTask;
    /// <summary>現在実行中の逐次解析処理を取り消すための発行元です。</summary>
    private CancellationTokenSource? _loadCancellation;
    /// <summary>ユーザー確認後にiCloudから取得するセッションです。</summary>
    private ConnectionSession? _pendingDownloadSession;

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>画面へ公開する解析状態です。</summary>
    public SessionLogAnalysisState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>現在実行中の逐次解析処理です。テストから完了を待つために公開します。</summary>
    internal Task? LoadTask => _loadTask;

    /// <summary>初期状態と時系列変換ユースケースを注入して生成します。</summary>
    /// <remarks>責務: セッション解析状態と読取専用変換ワークフローを固定します。</remarks>
    /// <param name="state">初期表示状態。</param>
    /// <param name="decodeTimeline">保存済みRawログを変換するユースケース。</param>
    /// <param name="prepareRawLog">必要時にCloudKitからRawログを復元するユースケース。</param>
    public SessionLogAnalysisModel(
        SessionLogAnalysisState state,
        DecodeSessionLogTimelineUseCase decodeTimeline,
        PrepareConnectionSessionRawLogUseCase? prepareRawLog = null)
    {
        _state = state;
        _decodeTimeline = decodeTimeline;
        _prepareRawLog = prepareRawLog;
    }

    /// <summary>画面操作を解析状態遷移へ適用します。</summary>
    /// <remarks>責務: 1件の解析操作を対応する読込または表示終了状態へ変換します。</remarks>
    /// <param name="action">適用する型付き解析操作。</param>
    public void Send(SessionLogAnalysisAction action)
    {
        switch (action)
        {
            case SessionLogAnalysisAction.SessionSelected selected:
                Select(selected.Session);
                break;
            case SessionLogAnalysisAction.DownloadConfirmed:
                ConfirmDownload();
                break;
            case SessionLogAnalysisAction.DownloadCancelled:
                CancelDownload();
                break;
            case SessionLogAnalysisAction.Dismissed:
                Dismiss();
                break;
        }
    }

    /// <summary>指定セッションを直接解析またはiCloud取得確認へ振り分けます。</summary>
    /// <remarks>責務: 1件の解析対象をローカル読込またはRaw取得確認状態へ変換します。</remarks>
    /// <param name="session">解析する保存済みセッション概要。</param>
    private void Select(ConnectionSession session)
    {
        if (State.SessionID == session.Id && State.Phase != SessionLogAnalysisStatePhase.Failed)
        {
            return;
        }
        if (session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            _loadCancellation?.Cancel();
            _pendingDownloadSession = session;
            State = new SessionLogAnalysisState
            {
                Phase = SessionLogAnalysisStatePhase.AwaitingDownloadConfirmation,
                SessionID = session.Id,
                DownloadPrompt = new SessionLogAnalysisStateDownloadPrompt(
                    sessionID: session.Id,
                    byteCount: session.RawLogSummary.ByteCount),
            };
        }
        else
        {
            BeginLoading(session, downloadsRaw: false);
        }
    }

    /// <summary>ユーザーが確認したセッションのiCloud取得と解析を開始します。</summary>
    /// <remarks>責務: 保留中のRaw取得確認を進捗付き解析処理へ変換します。</remarks>
    private void ConfirmDownload()
    {
        if (_pendingDownloadSession is not { } session
            || State.DownloadPrompt?.SessionID != session.Id)
        {
            return;
        }
        _pendingDownloadSession = null;
        BeginLoading(session, downloadsRaw: true);
    }

    /// <summary>保留中のiCloud取得を副作用なしで取り消します。</summary>
    /// <remarks>責務: Raw取得確認待ち状態を空の解析状態へ戻します。</remarks>
    private void CancelDownload()
    {
        if (State.Phase != SessionLogAnalysisStatePhase.AwaitingDownloadConfirmation)
        {
            return;
        }
        _pendingDownloadSession = null;
        State = new SessionLogAnalysisState();
    }

    /// <summary>指定セッションの逐次解析を開始します。</summary>
    /// <remarks>責務: 1件のセッションIDを画面遷移後に開始するキャンセル可能な解析処理へ変換します。</remarks>
    /// <param name="session">解析する保存済みセッション概要。</param>
    /// <param name="downloadsRaw">解析前にiCloud取得進捗を表示する場合は <see langword="true"/>。</param>
    private void BeginLoading(ConnectionSession session, bool downloadsRaw)
    {
        var sessionID = session.Id;
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        State = new SessionLogAnalysisState
        {
            Phase = downloadsRaw ? SessionLogAnalysisStatePhase.Downloading : SessionLogAnalysisStatePhase.Loading,
            SessionID = sessionID,
            DownloadProgress = downloadsRaw ? 0 : null,
        };
        _loadTask = RunLoading(session, sessionID, cancellation.Token);
    }

    /// <summary>Raw取得、時系列変換、グラフ集計を順に実行して状態へ反映します。</summary>
    /// <remarks>責務: 1件のセッション解析を取り消しと対象切替を考慮しながら表示状態へ変換します。</remarks>
    /// <param name="session">解析する保存済みセッション概要。</param>
    /// <param name="sessionID">解析対象のセッションID。</param>
    /// <param name="cancellationToken">この解析処理の取り消しを伝えるトークン。</param>
    /// <returns>解析処理の完了を表すタスク。</returns>
    private async Task RunLoading(ConnectionSession session, ConnectionSessionID sessionID, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            if (_prepareRawLog is { } prepareRawLog)
            {
                await prepareRawLog.Execute(
                    session,
                    downloadProgress: progress =>
                    {
                        if (State.SessionID != sessionID || State.Phase != SessionLogAnalysisStatePhase.Downloading)
                        {
                            return;
                        }
                        State = State with { DownloadProgress = Math.Min(Math.Max(progress, 0), 1) };
                    });
            }
            if (State.SessionID != sessionID)
            {
                return;
            }
            State = State with
            {
                Phase = SessionLogAnalysisStatePhase.Loading,
                DownloadPrompt = null,
                DownloadProgress = null,
            };
            await _decodeTimeline.Execute(
                sessionID,
                prepared: count =>
                {
                    if (State.SessionID != sessionID)
                    {
                        return;
                    }
                    State = State with { TotalSampleCount = count };
                },
                batchDecoded: batch =>
                {
                    if (State.SessionID != sessionID)
                    {
                        return;
                    }
                    State = State with
                    {
                        Timeline = [.. State.Timeline, .. batch],
                        DecodedSampleCount = State.DecodedSampleCount + batch.Count(sample => sample.Value is not null),
                    };
                },
                cancellationToken);
            if (State.SessionID != sessionID)
            {
                return;
            }
            var timeline = State.Timeline;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var visualization = SessionLogVisualizationBuilder.Build(timeline);
            if (cancellationToken.IsCancellationRequested || State.SessionID != sessionID)
            {
                return;
            }
            State = State with
            {
                PidSeries = visualization.Series,
                PerformanceSummary = visualization.PerformanceSummary,
                ComponentInsights = visualization.ComponentInsights,
                Relationships = visualization.Relationships,
                Phase = SessionLogAnalysisStatePhase.Loaded,
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (State.SessionID != sessionID)
            {
                return;
            }
            State = State with
            {
                Phase = SessionLogAnalysisStatePhase.Failed,
                FailureKey = "analysis.timeline.error.storage",
            };
        }
    }

    /// <summary>現在の解析を中止して表示状態を初期化します。</summary>
    /// <remarks>責務: 表示終了操作を解析キャンセルと空の解析状態へ変換します。</remarks>
    private void Dismiss()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _loadTask = null;
        _pendingDownloadSession = null;
        State = new SessionLogAnalysisState();
    }

    /// <summary>別の状態値が設定された場合にフィールドを更新して変更を通知します。</summary>
    /// <remarks>
    /// 責務: 1件の観測対象プロパティの更新を変更通知へ変換します。
    /// 時系列が大きくなるため、値の比較（全サンプルの比較）はせず、同じインスタンスの再設定だけを省きます。
    /// </remarks>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">変更したプロパティ名。</param>
    private void SetProperty(ref SessionLogAnalysisState field, SessionLogAnalysisState value, [CallerMemberName] string? propertyName = null)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
