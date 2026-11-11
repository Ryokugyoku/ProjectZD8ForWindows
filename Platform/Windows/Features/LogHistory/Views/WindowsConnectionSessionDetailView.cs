using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;

/// <summary>
/// 1 件のセッションの詳細です（iOS 版の <c>MacOSConnectionSessionDetailView</c>）。
/// </summary>
/// <remarks>
/// 停止の確認、Raw の件数、解析への入口、終了の扱い、学習用データの準備状況を出します。
/// 幅は親（一覧と詳細の右側・または全幅）に合わせ、操作のボタンは折り返し、カードは <see cref="AdaptiveGrid"/> で並べます。
/// </remarks>
public sealed partial class WindowsConnectionSessionDetailView : UserControl
{
    /// <summary>セッションと操作の送り先を受け取って詳細を作ります。</summary>
    /// <remarks>一覧へ戻るのは、画面の上のパンくずとシェルの戻る操作で行います。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="state">接続履歴の状態（削除中・アップロード中かどうか）。</param>
    /// <param name="send">接続履歴の操作の送り先。</param>
    /// <param name="openAnalysis">解析を開く処理。</param>
    public WindowsConnectionSessionDetailView(
        ConnectionSession session,
        ConnectionHistoryState state,
        Action<ConnectionHistoryAction> send,
        Action<ConnectionSession> openAnalysis)
    {
        var page = new StackPanel { Spacing = Space.L };
        page.Children.Add(DetailActions(session, state, send, openAnalysis));
        page.Children.Add(Hero(session));
        if (StopReviewCard(session, send) is { } review)
        {
            page.Children.Add(review);
        }

        var cards = AdaptiveGrid.Cards();
        cards.Children.Add(StatusCard(
            "history.training.raw.title",
            NumberText.Integer(session.RawLogSummary.RecordCount),
            ByteCountText.Format(session.RawLogSummary.ByteCount),
            FluentSymbol.Storage,
            Tone.Accent));
        cards.Children.Add(AnalysisCard(session, openAnalysis));
        cards.Children.Add(StatusCard(
            "history.detail.disposition.title",
            DispositionValue(session),
            session.EndReason is { } reason ? LocalizedText.Get(reason.HistoryLocalizationKey()) : "—",
            FluentSymbol.Shield,
            session.Status == ConnectionSessionStatus.Completed ? Tone.Success : Tone.Caution));
        page.Children.Add(cards);
        page.Children.Add(TrainingCard(session));
        page.Children.Add(LineageCard());
        Content = page;
    }

    /// <summary>操作の行（コマンドバー）を作ります。</summary>
    /// <remarks>責務: 解析・アップロード・Raw の解放・終了済みのセッションの削除（削除中は進行表示）のボタンを、幅に合わせて折り返して並べます。狭い幅でも全部届きます。</remarks>
    /// <returns>行。</returns>
    private static FlowPanel DetailActions(ConnectionSession session, ConnectionHistoryState state, Action<ConnectionHistoryAction> send, Action<ConnectionSession> openAnalysis)
    {
        var isDeleting = state.DeletingSessionID == session.Id;
        // ボタンは文字の幅のまま並べ、収まらなければ次の行へ送る（等幅の枠に詰めて文字が切れるのを避ける）。
        var grid = new FlowPanel();
        var analysis = new Button { Content = new GlyphLabel(FluentSymbol.Chart, LocalizedText.Get("analysis.open")), MinHeight = Metrics.ControlHeight };
        analysis.Click += (_, _) => openAnalysis(session);
        grid.Children.Add(analysis);
        if (WindowsConnectionHistoryView.CanUpload(state, session))
        {
            var upload = WindowsConnectionHistoryView.MakeUploadButton(state, session, send);
            upload.MinHeight = Metrics.ControlHeight;
            grid.Children.Add(upload);
        }

        if (WindowsConnectionHistoryView.CanReleaseRawCache(session))
        {
            var release = WindowsConnectionHistoryView.ReleaseRawButton(session, send);
            release.MinHeight = Metrics.ControlHeight;
            grid.Children.Add(release);
        }

        if (session.EndedAt is not null)
        {
            var delete = new Button
            {
                Style = DesignTokens.Style("DangerButtonStyle"),
                IsEnabled = !isDeleting,
                MinHeight = Metrics.ControlHeight,
                Content = isDeleting
                    ? new ProgressRing { IsActive = true, Width = 16, Height = 16 }
                    : new GlyphLabel(FluentSymbol.Delete, LocalizedText.Get("history.delete.button")),
            };
            delete.Click += (_, _) => send(new ConnectionHistoryAction.SessionDeletionRequested(session.Id));
            grid.Children.Add(delete);
        }

        return grid;
    }

    /// <summary>セッションの見出しを作ります。</summary>
    /// <remarks>責務: 車両名・開始日時・取得元と、状態・ID・距離の取得元の車種を並べます。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>カード。</returns>
    private static Border Hero(ConnectionSession session)
    {
        // 詳細は一覧の横の細い欄にも出るので、状態・車種・ID は見出しの右ではなく下の行に並べる（題が何行にも折れないように）。
        var trailing = new FlowPanel();
        if (session.DistanceSourceModelCode is { } modelCode)
        {
            trailing.Children.Add(new VehicleModelBadge(modelCode));
        }

        var statusKey = session.Status switch
        {
            ConnectionSessionStatus.Connected => "history.status.connected",
            ConnectionSessionStatus.Completed => "history.status.completed",
            _ => "history.status.interrupted",
        };
        var pill = StatusPill.Create(
            session.Status == ConnectionSessionStatus.Completed ? FluentSymbol.Completed : FluentSymbol.Clock,
            LocalizedText.Get(statusKey),
            session.Status == ConnectionSessionStatus.Completed ? Tone.Success : Tone.Caution);
        trailing.Children.Add(pill);
        var id = Typography.Text(session.Id.RawValue.ToString().ToUpperInvariant(), TextStyles.Monospace, wraps: false);
        id.VerticalAlignment = VerticalAlignment.Center;
        id.TextTrimming = TextTrimming.CharacterEllipsis;
        trailing.Children.Add(id);

        var subtitle = DateText.DateAndMinute(session.StartedAt) + "  ·  " + WindowsConnectionHistoryView.AcquisitionDeviceText(session);
        var stack = new StackPanel { Spacing = Space.M };
        stack.Children.Add(new ScreenHeader(
            FluentSymbol.Pulse,
            LocalizedText.Get("history.training.badge"),
            session.Vehicle?.Name ?? LocalizedText.Get("history.vehicle.pending"),
            subtitle));
        stack.Children.Add(trailing);
        return SurfaceCard.Create(stack);
    }

    /// <summary>停止の確認のカードを作ります。</summary>
    /// <remarks>責務: 確認が必要なら確認のボタン付きのカード（Compact ではボタンを下の段へ）、利用者の停止と確認済みなら確認済みの表示を作ります。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="send">操作の送り先。</param>
    /// <returns>表示の要素。なければ null。</returns>
    private static UIElement? StopReviewCard(ConnectionSession session, Action<ConnectionHistoryAction> send)
    {
        if (session.NeedsStopReview)
        {
            var grid = new Grid { ColumnSpacing = Space.M };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new WarningTriangleIcon(20);
            icon.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            var texts = new StackPanel { Spacing = Space.XS };
            texts.Children.Add(Typography.Text(LocalizedText.Get("history.stop_review.card.title"), TextStyles.BodyStrong));
            texts.Children.Add(Typography.Text(LocalizedText.Get(session.EndReason == ConnectionSessionEndReason.VehicleNoResponse
                ? "history.stop_review.card.no_response"
                : "history.stop_review.card.connection_lost"), TextStyles.SecondaryCaption));
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            var action = new Button { Content = LocalizedText.Get("history.stop_review.card.action"), VerticalAlignment = VerticalAlignment.Center };
            action.Click += (_, _) => send(new ConnectionHistoryAction.StopReviewRequested(session.Id));
            grid.Children.Add(action);
            ResponsiveLayout.Bind(grid, widthClass =>
            {
                var compact = widthClass == WidthClass.Compact;
                grid.RowDefinitions.Clear();
                grid.RowSpacing = compact ? Space.M : 0;
                if (compact)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                Grid.SetRow(action, compact ? 1 : 0);
                Grid.SetColumn(action, compact ? 1 : 2);
                action.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            });
            return SurfaceCard.Create(grid, Tone.Caution, Space.L, tinted: true);
        }

        if (session.StopReviewDecision == ConnectionSessionStopReviewDecision.UserInitiated)
        {
            return new Border
            {
                Background = DesignTokens.SubtleBackground(Tone.Success),
                CornerRadius = new CornerRadius(Radius.L),
                Padding = new Thickness(Space.L),
                Child = new GlyphLabel(FluentSymbol.Completed, LocalizedText.Get("history.stop_review.accepted"), TextStyles.BodyStrong, DesignTokens.Foreground(Tone.Success)),
            };
        }

        return null;
    }

    /// <summary>解析への入口のカードを作ります。</summary>
    /// <remarks>責務: 時系列の件数を出し、押すと解析を開くボタンを作ります。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="openAnalysis">解析を開く処理。</param>
    /// <returns>ボタン。</returns>
    private static Button AnalysisCard(ConnectionSession session, Action<ConnectionSession> openAnalysis)
    {
        var stack = new StackPanel { Spacing = Space.M };
        stack.Children.Add(StatusContent(
            "analysis.open",
            LocalizedText.Get("analysis.card.value"),
            LocalizedText.Format("analysis.card.caption", session.RawLogSummary.RecordCount),
            FluentSymbol.Chart,
            Tone.Accent));
        var hint = new Grid();
        hint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hint.Children.Add(Typography.Strong(LocalizedText.Get("analysis.open.hint"), TextStyles.Caption, DesignTokens.Foreground(Tone.Accent)));
        var chevron = new FontIcon { Glyph = FluentSymbol.ChevronRight, FontSize = 11, Foreground = DesignTokens.Foreground(Tone.Accent) };
        Grid.SetColumn(chevron, 1);
        hint.Children.Add(chevron);
        stack.Children.Add(hint);
        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(Space.L),
            CornerRadius = new CornerRadius(Radius.L),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-session-analysis-open");
        button.Click += (_, _) => openAnalysis(session);
        return button;
    }

    /// <summary>状態のカードを作ります。</summary>
    /// <remarks>責務: 記号・題・値・補足の 4 つを持つカードを作ります。</remarks>
    /// <returns>カード。</returns>
    private static Border StatusCard(string titleKey, string value, string caption, string glyph, Tone tone)
    {
        var card = SurfaceCard.Create(StatusContent(titleKey, value, caption, glyph, tone), padding: Space.L);
        card.MinHeight = 132;
        return card;
    }

    /// <summary>状態のカードの中身を作ります。</summary>
    /// <remarks>責務: 記号と題の行、値、補足を縦に並べます。</remarks>
    /// <returns>中身。</returns>
    private static StackPanel StatusContent(string titleKey, string value, string caption, string glyph, Tone tone)
    {
        var stack = new StackPanel { Spacing = Space.M };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
        head.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = DesignTokens.Foreground(tone) });
        head.Children.Add(Typography.Strong(LocalizedText.Get(titleKey), TextStyles.SecondaryCaption));
        stack.Children.Add(head);
        stack.Children.Add(Typography.Metric(value, TextStyles.MetricValue));
        stack.Children.Add(Typography.Text(caption, TextStyles.Monospace, wraps: false));
        return stack;
    }

    /// <summary>学習用データの準備状況のカードを作ります。</summary>
    /// <remarks>責務: 車両・端末の Raw・Mac の受領がそろったときだけ「準備完了」と出します。Compact では範囲の表示を下の段へ回します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>カード。</returns>
    private static Border TrainingCard(ConnectionSession session)
    {
        var ready = session.Vehicle is not null
            && session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Available
            && session.RawLogSummary.RecordCount > 0
            && session.RawLogSummary.IsDurablyImportedByMac;
        var tone = ready ? Tone.Success : Tone.Caution;
        var grid = new Grid { ColumnSpacing = Space.XL };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(FluentSymbol.Brain, tone, Metrics.IconTileLarge));
        var texts = new StackPanel { Spacing = Space.S };
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.title"), TextStyles.CardTitle));
        texts.Children.Add(Typography.Text(LocalizedText.Get(ready ? "history.training.ready.body" : "history.training.pending.body"), TextStyles.SecondaryBody));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var scope = new MetricBlock(
            session.Vehicle is null ? "—" : "VEHICLE / SESSION / SEQUENCE",
            LocalizedText.Get("history.training.scope"),
            HorizontalAlignment.Left,
            valueStyleKey: TextStyles.Monospace);
        grid.Children.Add(scope);
        ResponsiveLayout.Bind(grid, widthClass =>
        {
            var compact = widthClass == WidthClass.Compact;
            grid.RowDefinitions.Clear();
            grid.RowSpacing = compact ? Space.M : 0;
            if (compact)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Grid.SetRow(scope, compact ? 1 : 0);
            Grid.SetColumn(scope, compact ? 0 : 2);
            Grid.SetColumnSpan(scope, compact ? 3 : 1);
            scope.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        });
        return SurfaceCard.Create(grid, tone, Space.XL);
    }

    /// <summary>データの系譜の説明のカードを作ります。</summary>
    /// <remarks>責務: 学習データの系譜の題と説明を出します。</remarks>
    /// <returns>カード。</returns>
    private static Border LineageCard()
    {
        var grid = new Grid { ColumnSpacing = Space.M };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = FluentSymbol.Branch, FontSize = 17, Foreground = DesignTokens.Foreground(Tone.Accent), VerticalAlignment = VerticalAlignment.Top });
        var texts = new StackPanel { Spacing = Space.XS };
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.lineage.title"), TextStyles.BodyStrong));
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.lineage.body"), TextStyles.SecondaryCaption));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = grid };
    }

    /// <summary>終了の扱いの文言を返します（iOS 版の <c>dispositionValue</c>）。</summary>
    /// <remarks>責務: 接続中・確認待ち・利用者の停止・通常・中断を文言へ変換します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>文言。</returns>
    private static string DispositionValue(ConnectionSession session)
    {
        if (session.Status == ConnectionSessionStatus.Connected)
        {
            return LocalizedText.Get("history.status.connected");
        }

        if (session.NeedsStopReview)
        {
            return LocalizedText.Get("history.detail.disposition.review");
        }

        if (session.StopReviewDecision == ConnectionSessionStopReviewDecision.UserInitiated)
        {
            return LocalizedText.Get("history.detail.disposition.user_accepted");
        }

        return LocalizedText.Get(session.Status == ConnectionSessionStatus.Completed
            ? "history.detail.disposition.regular"
            : "history.status.interrupted");
    }
}
