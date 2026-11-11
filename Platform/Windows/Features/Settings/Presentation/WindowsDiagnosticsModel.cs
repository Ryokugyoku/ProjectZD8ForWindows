using System;
using System.ComponentModel;
using System.IO;
using ProjectZD8.Diagnostics;
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

/// <summary>
/// 不具合レポートに添える、アプリの状態の情報です。
/// </summary>
/// <param name="DefaultAdapterKind">既定アダプターの種類。選んでいなければ null。</param>
/// <param name="SessionCount">接続セッションの数。分からなければ null。</param>
public sealed record WindowsDiagnosticsEnvironment(string? DefaultAdapterKind, int? SessionCount);

/// <summary>
/// 設定画面の「診断」の行と、シェルの「前回は正常に終了しませんでした」の知らせが使う表示モデルです。
/// </summary>
/// <remarks>
/// 責務: 「詳細ログ」スイッチ（入れた日時を保存し、7 日で自動で切る）、不具合レポートの書き出し、診断ログの削除と合計サイズ、
/// 前回の異常終了の知らせを出すかを扱います。診断ログを始められなかった（<see cref="DiagnosticSession"/> が null の）ときは、すべて何もしません。
/// </remarks>
public sealed class WindowsDiagnosticsModel : INotifyPropertyChanged
{
    /// <summary>前回の異常終了の知らせを、この実行では出さないと決めたかです（サインインし直してシェルを作り直しても出さない）。</summary>
    private static bool s_crashNoticeHandled;

    /// <summary>今の実行の診断ログです。始められなかったら null。</summary>
    private readonly DiagnosticSession? _session;

    /// <summary>「詳細ログ」を入れた日時を保存します。</summary>
    private readonly VerboseLoggingLease? _lease;

    /// <summary>不具合レポートに添える状態を集めます。</summary>
    private readonly Func<WindowsDiagnosticsEnvironment> _environment;

    /// <summary>診断ログを指定して作ります。</summary>
    /// <param name="session">今の実行の診断ログ。始められなかったら null。</param>
    /// <param name="environment">不具合レポートに添える状態を集める処理。</param>
    public WindowsDiagnosticsModel(DiagnosticSession? session, Func<WindowsDiagnosticsEnvironment> environment)
    {
        _session = session;
        _environment = environment;
        if (session is not null)
        {
            _lease = new VerboseLoggingLease(System.IO.Directory.GetParent(session.Directory)?.FullName ?? session.Directory);
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>診断ログが動いているか（動いていなければ「診断」の行は出さない）です。</summary>
    public bool IsAvailable => _session is not null;

    /// <summary>Dev / Beta チャンネルのように、最初から詳細に記録しているか（スイッチは無効）です。</summary>
    public bool IsVerboseByChannel => _session is { Channel: not DiagnosticsChannel.Stable };

    /// <summary>「詳細ログ」が入っているか（最初から詳細な版では常に true）です。</summary>
    public bool VerboseLogging => IsVerboseByChannel || _session?.VerboseLogging == true;

    /// <summary>「詳細ログ」が自動で切れる日時です。入っていない、または最初から詳細な版では null。</summary>
    public DateTimeOffset? VerboseExpiresAt => IsVerboseByChannel || _session?.VerboseLogging != true ? null : _lease?.ExpiresAt;

    /// <summary>前回の実行が正常に終了しなかったことを尋ねる知らせを出すかです。</summary>
    public bool ShowsPreviousRunNotice => _session is { PreviousRunCrashed: true } && !s_crashNoticeHandled;

    /// <summary>
    /// 「詳細ログ」を切り替えます。
    /// </summary>
    /// <remarks>責務: 診断ログの最低レベルを切り替え、入れた日時を保存します（切るときは消す）。Dev / Beta では何もしません。</remarks>
    /// <param name="enabled">入れるなら true。</param>
    public void SetVerboseLogging(bool enabled)
    {
        if (_session is null || _lease is null || IsVerboseByChannel)
        {
            return;
        }

        if (enabled)
        {
            _lease.Enable();
        }
        else
        {
            _lease.Disable();
        }

        _session.SetVerboseLogging(enabled);
        UiLog.VerboseChanged(UiLog.Logger, enabled);
        OnPropertyChanged(nameof(VerboseLogging));
        OnPropertyChanged(nameof(VerboseExpiresAt));
    }

    /// <summary>診断ログの合計サイズ（バイト）を返します。</summary>
    /// <returns>合計サイズ。</returns>
    public long TotalLogBytes() => _session is null ? 0 : DiagnosticLogFolder.TotalBytes(_session.Directory);

    /// <summary>保存ダイアログの既定のファイル名を返します。</summary>
    /// <returns>ファイル名。</returns>
    public string SuggestedReportFileName() =>
        DiagnosticReport.SuggestedFileName(DateTimeOffset.UtcNow, _session?.RunId ?? "0000");

    /// <summary>
    /// 不具合レポートを書き出します。
    /// </summary>
    /// <remarks>責務: 書き出す前に受け取ったログを書き終え、直近 7 日のログと概要を 1 つの zip にします。ファイルの読み書きをするので、UI スレッドの外から呼びます。</remarks>
    /// <param name="zipPath">書き出す zip のパス。</param>
    /// <returns>書き出した結果。診断ログが動いていなければ null。</returns>
    public DiagnosticReportResult? ExportReport(string zipPath)
    {
        if (_session is null)
        {
            return null;
        }

        var environment = _environment();
        return DiagnosticReport.Write(_session, zipPath, environment.DefaultAdapterKind, environment.SessionCount);
    }

    /// <summary>
    /// 診断ログを消します。
    /// </summary>
    /// <remarks>責務: 今の実行が書き込み中のファイルは残し、それ以外の診断ログを消します。</remarks>
    /// <returns>消したファイルの数。</returns>
    public int ClearLogs()
    {
        if (_session is null)
        {
            return 0;
        }

        var deleted = DiagnosticLogFolder.Clear(
            _session.Directory,
            _session.RunId[..DiagnosticFileName.RunPrefixLength]);
        UiLog.LogsCleared(UiLog.Logger, deleted);
        return deleted;
    }

    /// <summary>前回の異常終了の知らせを、この実行ではもう出さないことにします（閉じた・書き出した）。</summary>
    public void HandlePreviousRunNotice()
    {
        if (s_crashNoticeHandled)
        {
            return;
        }

        s_crashNoticeHandled = true;
        OnPropertyChanged(nameof(ShowsPreviousRunNotice));
    }

    /// <summary>プロパティが変わったことを知らせます。</summary>
    /// <param name="name">プロパティの名前。</param>
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
