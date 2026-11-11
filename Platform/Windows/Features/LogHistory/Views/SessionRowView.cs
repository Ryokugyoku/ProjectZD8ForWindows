using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;

/// <summary>セッションの行が、操作を送る先です。</summary>
internal interface ISessionRowHost
{
    /// <summary>今の接続履歴の状態です（アップロード中かどうかなどを読み直すため）。</summary>
    ConnectionHistoryState State { get; }

    /// <summary>セッションの詳細を開きます。</summary>
    /// <param name="sessionID">セッション。</param>
    void OpenDetail(ConnectionSessionID sessionID);

    /// <summary>セッションの解析を開きます。</summary>
    /// <param name="session">セッション。</param>
    void OpenAnalysis(ConnectionSession session);

    /// <summary>手動アップロードを頼みます。</summary>
    /// <param name="sessionID">セッション。</param>
    void RequestUpload(ConnectionSessionID sessionID);

    /// <summary>接続履歴の操作を送ります。</summary>
    /// <param name="action">操作。</param>
    void Send(ConnectionHistoryAction action);
}

/// <summary>
/// 仮想化した一覧の、セッション 1 行です。
/// </summary>
/// <remarks>
/// 要素は 1 回だけ組み、<see cref="Bind"/> と <see cref="Refresh"/> で値だけを差し替えます（行は使い回される）。
/// 右クリックのメニューは、項目の構成が変わったときだけ作り直します（開いている間に消えないように）。
/// 押す操作（詳細を開く）は一覧の ItemClick が受けるので、行の中のボタンはアップロードと Raw の解放だけです。
/// </remarks>
internal sealed partial class SessionRowView : Grid
{
    /// <summary>操作の送り先です。</summary>
    private readonly ISessionRowHost _host;

    /// <summary>状態の記号です（完了は緑のチェック、中断は注意の三角）。</summary>
    private readonly FontIcon _statusIcon = new() { FontSize = 18, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>開始日時です。</summary>
    private readonly TextBlock _date = Typography.Strong(string.Empty);

    /// <summary>取得した端末の行です（広い一覧だけ）。</summary>
    private readonly StackPanel _deviceRow = new() { Orientation = Orientation.Horizontal, Spacing = Space.S };

    /// <summary>取得した端末の文字です。</summary>
    private readonly TextBlock _device = Typography.Text(string.Empty, TextStyles.SecondaryCaption, wraps: false);

    /// <summary>iCloud の状態の印です。</summary>
    private readonly Border _cloudPill = StatusPill.Create(null, string.Empty, Tone.Neutral);

    /// <summary>端末の Raw の状態の印です。</summary>
    private readonly Border _localPill = StatusPill.Create(null, string.Empty, Tone.Neutral);

    /// <summary>終わった理由です。</summary>
    private readonly TextBlock _reason = Typography.Strong(string.Empty, TextStyles.Caption);

    /// <summary>時間の文字です。</summary>
    private readonly TextBlock _duration = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

    /// <summary>距離の文字です。</summary>
    private readonly TextBlock _distance = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

    /// <summary>短い ID です（広い一覧だけ）。</summary>
    private readonly TextBlock _id = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

    /// <summary>手動アップロードのボタンです（広い一覧だけ）。</summary>
    private readonly Button _upload = new() { Style = DesignTokens.Style("SubtleButtonStyle"), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>手動アップロードの文字の面です。</summary>
    private readonly StackPanel _uploadLabel = new() { Orientation = Orientation.Horizontal, Spacing = Space.S };

    /// <summary>手動アップロードの進み具合の面です。</summary>
    private readonly StackPanel _uploadProgress = new() { Orientation = Orientation.Horizontal, Spacing = Space.S, MinWidth = 112 };

    /// <summary>手動アップロードの進み具合の棒です。</summary>
    private readonly ProgressBar _uploadBar = new() { Width = 72, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>手動アップロードの進み具合の文字です。</summary>
    private readonly TextBlock _uploadPercent = Typography.Text(string.Empty, TextStyles.Caption, wraps: false);

    /// <summary>端末の Raw を解放するボタンです（広い一覧だけ）。</summary>
    private readonly Button _release = new()
    {
        Style = DesignTokens.Style("SubtleButtonStyle"),
        VerticalAlignment = VerticalAlignment.Center,
        Content = new GlyphLabel(FluentSymbol.Storage, LocalizedText.Get("history.raw.remove.button"), TextStyles.Body, DesignTokens.Foreground(Tone.Caution)),
    };

    /// <summary>ボタンを並べる面です。</summary>
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = Space.S, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>右クリックのメニューを作ったときの構成です。</summary>
    private (bool Upload, bool Release, bool Delete)? _menuShape;

    /// <summary>操作の送り先を指定して、行を組みます。</summary>
    /// <param name="host">操作の送り先。</param>
    public SessionRowView(ISessionRowHost host)
    {
        _host = host;
        CornerRadius = new CornerRadius(Radius.M);
        Padding = new Thickness(Space.M, Space.S, Space.M, Space.S);
        MinHeight = Metrics.RowMinHeight;
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;

        ColumnSpacing = Space.M;
        var grid = this;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_statusIcon);

        _deviceRow.Children.Add(new FontIcon { Glyph = FluentSymbol.Person, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        _deviceRow.Children.Add(_device);
        // 印は細い一覧で切れないよう、収まらなければ次の行へ送る。
        var badges = new FlowPanel();
        badges.Children.Add(_cloudPill);
        badges.Children.Add(_localPill);
        var texts = new StackPanel { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_date);
        texts.Children.Add(_deviceRow);
        texts.Children.Add(badges);
        texts.Children.Add(_reason);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        // 時間・距離の列は幅をそろえ、行ごとに位置がずれないようにする。
        var measures = new StackPanel { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Center, MinWidth = 84 };
        measures.Children.Add(Labeled(FluentSymbol.Clock, _duration));
        measures.Children.Add(Labeled(FluentSymbol.Road, _distance));
        Grid.SetColumn(measures, 2);
        grid.Children.Add(measures);

        _id.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_id, 3);
        grid.Children.Add(_id);

        _uploadLabel.Children.Add(new FontIcon { Glyph = FluentSymbol.Upload, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        _uploadLabel.Children.Add(Typography.Text(LocalizedText.Get("history.upload.button"), wraps: false));
        _uploadProgress.Children.Add(_uploadBar);
        _uploadProgress.Children.Add(_uploadPercent);
        _upload.Content = new Grid { Children = { _uploadLabel, _uploadProgress } };
        _upload.Click += (_, _) =>
        {
            if (Session is { } session)
            {
                _host.Send(new ConnectionHistoryAction.SessionUploadRequested(session.Id));
            }
        };
        _release.Click += (_, _) =>
        {
            if (Session is { } session)
            {
                _host.Send(new ConnectionHistoryAction.LocalRawRemovalRequested(session.Id));
            }
        };
        _buttons.Children.Add(_upload);
        _buttons.Children.Add(_release);
        Grid.SetColumn(_buttons, 4);
        grid.Children.Add(_buttons);
    }

    /// <summary>今、この行が出しているセッションです。</summary>
    public ConnectionSession? Session { get; private set; }

    /// <summary>
    /// セッションを入れて、行を合わせます。
    /// </summary>
    /// <remarks>責務: 行の中の文字・印・記号を、セッションの値に差し替えます。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="selected">詳細を出しているなら true。</param>
    /// <param name="wide">広い一覧（端末・ID・ボタンを出す）なら true。</param>
    public void Bind(ConnectionSession session, bool selected, bool wide)
    {
        Session = session;
        Retained.SetText(_date, DateText.DateAndMinute(session.StartedAt));
        Retained.SetText(_device, WindowsConnectionHistoryView.AcquisitionDeviceText(session));
        Retained.SetText(_duration, DurationText.HoursAndMinutes(SessionDuration(session)));
        Retained.SetText(_distance, DistanceText.Format(session.RecordedDistanceKilometers));
        Retained.SetText(_id, session.Id.RawValue.ToString("N")[..8].ToUpperInvariant());
        var completed = session.Status == ConnectionSessionStatus.Completed;
        Retained.SetGlyph(_statusIcon, completed ? FluentSymbol.Completed : FluentSymbol.Warning);
        _statusIcon.Foreground = DesignTokens.Foreground(completed ? Tone.Success : Tone.Caution);
        Retained.SetText(_reason, LocalizedText.Get(session.EndReason?.HistoryLocalizationKey() ?? ConnectionHistoryEndReasonFilter.All.HistoryLocalizationKey()));
        _reason.Foreground = completed ? DesignTokens.SystemBrush("TextFillColorPrimaryBrush") : DesignTokens.Foreground(Tone.Caution);
        var (cloudGlyph, cloudText, cloudTone) = CloudStatus(session.RawLogSummary.CloudState);
        StatusPill.Set(_cloudPill, cloudGlyph, cloudText, cloudTone);
        var (localGlyph, localText, localTone) = LocalStatus(session.RawLogSummary.LocalState);
        StatusPill.Set(_localPill, localGlyph, localText, localTone);
        AutomationProperties.SetName(this, $"{DateText.DateAndMinute(session.StartedAt)} {_reason.Text}");
        Refresh(selected, wide);
    }

    /// <summary>
    /// 状態に左右される部分（選択・ボタン・メニュー）を合わせます。
    /// </summary>
    /// <remarks>責務: 選択の強調、広い一覧のボタン（アップロードの進み具合を含む）、右クリックのメニューを、今の状態に合わせます。</remarks>
    /// <param name="selected">詳細を出しているなら true。</param>
    /// <param name="wide">広い一覧なら true。</param>
    public void Refresh(bool selected, bool wide)
    {
        if (Session is not { } session)
        {
            return;
        }

        var state = _host.State;
        Background = selected ? DesignTokens.SubtleBackground(Tone.Accent) : null;
        BorderBrush = selected ? DesignTokens.Stroke(Tone.Accent) : null;
        Retained.SetVisible(_deviceRow, wide);
        Retained.SetVisible(_id, wide);
        var canUpload = WindowsConnectionHistoryView.CanUpload(state, session);
        var canRelease = WindowsConnectionHistoryView.CanReleaseRawCache(session);
        Retained.SetVisible(_upload, wide && canUpload);
        Retained.SetVisible(_release, wide && canRelease);
        Retained.SetVisible(_buttons, wide && (canUpload || canRelease));
        if (wide && canUpload)
        {
            var uploading = state.UploadingSessionID == session.Id;
            Retained.SetEnabled(_upload, state.UploadingSessionID is null);
            Retained.SetVisible(_uploadLabel, !uploading);
            Retained.SetVisible(_uploadProgress, uploading);
            if (uploading)
            {
                var percent = (state.UploadProgress ?? 0) * 100;
                if (Math.Abs(_uploadBar.Value - percent) > 0.05)
                {
                    _uploadBar.Value = percent;
                }

                Retained.SetText(_uploadPercent, NumberText.Percent(state.UploadProgress ?? 0));
            }
        }

        var shape = (canUpload && state.UploadingSessionID is null, canRelease, session.EndedAt is not null && state.DeletingSessionID != session.Id);
        if (_menuShape != shape)
        {
            _menuShape = shape;
            ContextFlyout = BuildMenu(shape);
        }
    }

    /// <summary>
    /// 右クリックのメニューを作ります。
    /// </summary>
    /// <remarks>責務: 詳細・解析、使えるときだけアップロード・Raw の解放・削除を並べます。押したときの対象は、その時点の行のセッションです。</remarks>
    /// <param name="shape">メニューの構成（アップロード・解放・削除を出すか）。</param>
    /// <returns>メニュー。</returns>
    private MenuFlyout BuildMenu((bool Upload, bool Release, bool Delete) shape)
    {
        var menu = new ContextMenu()
            .Item(FluentSymbol.Document, LocalizedText.Get("windows.history.open_detail"), () => WithSession(session => _host.OpenDetail(session.Id)))
            .Item(FluentSymbol.Chart, LocalizedText.Get("analysis.open"), () => WithSession(_host.OpenAnalysis));
        if (shape.Upload)
        {
            menu.Item(FluentSymbol.Upload, LocalizedText.Get("history.upload.button"), () => WithSession(session => _host.RequestUpload(session.Id)));
        }

        if (shape.Release)
        {
            menu.Item(
                FluentSymbol.Storage,
                LocalizedText.Get("history.raw.remove.button"),
                () => WithSession(session => _host.Send(new ConnectionHistoryAction.LocalRawRemovalRequested(session.Id))),
                Tone.Caution);
        }

        if (shape.Delete)
        {
            menu.Separator().Item(
                FluentSymbol.Delete,
                LocalizedText.Get("history.delete.button"),
                () => WithSession(session => _host.Send(new ConnectionHistoryAction.SessionDeletionRequested(session.Id))),
                Tone.Danger);
        }

        return menu.Build();
    }

    /// <summary>行が今出しているセッションに対して処理を行います（行は使い回されるので、押した時点のセッションを読み直す）。</summary>
    /// <param name="action">処理。</param>
    private void WithSession(Action<ConnectionSession> action)
    {
        if (Session is { } session)
        {
            action(session);
        }
    }

    /// <summary>記号と文字を横に並べます。</summary>
    /// <param name="glyph">記号。</param>
    /// <param name="text">文字。</param>
    /// <returns>並べた面。</returns>
    private static StackPanel Labeled(string glyph, TextBlock text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    /// <summary>iCloud の状態の印の中身です。</summary>
    /// <param name="state">iCloud の状態。</param>
    /// <returns>記号・文字・色の役割。</returns>
    private static (string? Glyph, string Text, Tone Tone) CloudStatus(ConnectionSessionCloudSyncState state) => state switch
    {
        ConnectionSessionCloudSyncState.Pending => (FluentSymbol.Upload, LocalizedText.Get("history.raw.cloud.pending"), Tone.Caution),
        ConnectionSessionCloudSyncState.Uploaded => (FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.uploaded"), Tone.Success),
        ConnectionSessionCloudSyncState.Failed => (FluentSymbol.Error, LocalizedText.Get("history.raw.cloud.failed"), Tone.Danger),
        _ => (FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.not_uploaded"), Tone.Neutral),
    };

    /// <summary>端末の Raw の状態の印の中身です。</summary>
    /// <param name="state">端末の Raw の状態。</param>
    /// <returns>記号・文字・色の役割。</returns>
    private static (string? Glyph, string Text, Tone Tone) LocalStatus(ConnectionSessionLocalRawState state) => state switch
    {
        ConnectionSessionLocalRawState.Empty => (null, LocalizedText.Get("history.raw.local.empty"), Tone.Neutral),
        ConnectionSessionLocalRawState.Available => (FluentSymbol.Completed, LocalizedText.Get("history.raw.local.available"), Tone.Accent),
        _ => (FluentSymbol.Download, LocalizedText.Get("history.raw.local.removed"), Tone.Neutral),
    };

    /// <summary>セッションの長さ（秒）です。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>秒。</returns>
    private static double SessionDuration(ConnectionSession session) =>
        Math.Max(0, ((session.EndedAt ?? session.StartedAt) - session.StartedAt).TotalSeconds);
}
