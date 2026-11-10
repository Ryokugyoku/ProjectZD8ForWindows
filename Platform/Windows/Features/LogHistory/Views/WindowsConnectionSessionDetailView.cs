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
/// <remarks>停止の確認、Raw の件数、解析への入口、終了の扱い、学習用データの準備状況を出します。</remarks>
public sealed partial class WindowsConnectionSessionDetailView : UserControl
{
    /// <summary>セッションと操作の送り先を受け取って詳細を作ります。</summary>
    /// <param name="session">セッション。</param>
    /// <param name="isDeleting">削除中なら true。</param>
    /// <param name="send">接続履歴の操作の送り先。</param>
    /// <param name="openAnalysis">解析を開く処理。</param>
    /// <param name="back">一覧へ戻る処理。</param>
    public WindowsConnectionSessionDetailView(
        ConnectionSession session,
        bool isDeleting,
        Action<ConnectionHistoryAction> send,
        Action<ConnectionSession> openAnalysis,
        Action back)
    {
        var page = new StackPanel { Spacing = 20 };
        page.Children.Add(DetailActions(session, isDeleting, send, back));
        page.Children.Add(Hero(session));
        if (StopReviewCard(session, send) is { } review)
        {
            page.Children.Add(review);
        }

        var cards = new AdaptiveGrid { MinItemWidth = 220, Spacing = 16 };
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

    /// <summary>戻る・削除の操作の行を作ります。</summary>
    /// <remarks>責務: 戻るボタンと、終了済みのセッションの削除ボタン（削除中は進行表示）を並べます。</remarks>
    /// <returns>行。</returns>
    private static Grid DetailActions(ConnectionSession session, bool isDeleting, Action<ConnectionHistoryAction> send, Action back)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var backButton = new Button { Content = new GlyphLabel(FluentSymbol.Back, LocalizedText.Get("history.detail.back")), MinHeight = 36 };
        backButton.Click += (_, _) => back();
        grid.Children.Add(backButton);
        if (session.EndedAt is not null)
        {
            var delete = new Button
            {
                Style = DesignTokens.Style("DangerButtonStyle"),
                IsEnabled = !isDeleting,
                MinHeight = 36,
                MinWidth = 128,
                Content = isDeleting
                    ? new ProgressRing { IsActive = true, Width = 16, Height = 16 }
                    : new GlyphLabel(FluentSymbol.Delete, LocalizedText.Get("history.delete.button")),
            };
            delete.Click += (_, _) => send(new ConnectionHistoryAction.SessionDeletionRequested(session.Id));
            Grid.SetColumn(delete, 1);
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
        var trailing = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Right };
        if (session.DistanceSourceModelCode is { } modelCode)
        {
            var badge = new VehicleModelBadge(modelCode) { HorizontalAlignment = HorizontalAlignment.Right };
            trailing.Children.Add(badge);
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
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        trailing.Children.Add(pill);
        trailing.Children.Add(Typography.Text(session.Id.RawValue.ToString().ToUpperInvariant(), "MonospaceCaptionTextStyle", wraps: false));

        var subtitle = DateText.DateAndMinute(session.StartedAt) + "  ·  " + WindowsConnectionHistoryView.AcquisitionDeviceText(session);
        return SurfaceCard.Create(new ScreenHeader(
            FluentSymbol.Pulse,
            LocalizedText.Get("history.training.badge"),
            session.Vehicle?.Name ?? LocalizedText.Get("history.vehicle.pending"),
            subtitle,
            trailing), padding: 25);
    }

    /// <summary>停止の確認のカードを作ります。</summary>
    /// <remarks>責務: 確認が必要なら確認のボタン付きのカード、利用者の停止と確認済みなら確認済みの表示を作ります。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="send">操作の送り先。</param>
    /// <returns>表示の要素。なければ null。</returns>
    private static UIElement? StopReviewCard(ConnectionSession session, Action<ConnectionHistoryAction> send)
    {
        if (session.NeedsStopReview)
        {
            var grid = new Grid { ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new WarningTriangleIcon(20));
            var texts = new StackPanel { Spacing = 4 };
            texts.Children.Add(Typography.Text(LocalizedText.Get("history.stop_review.card.title"), "BodyStrongTextBlockStyle"));
            texts.Children.Add(Typography.Text(LocalizedText.Get(session.EndReason == ConnectionSessionEndReason.VehicleNoResponse
                ? "history.stop_review.card.no_response"
                : "history.stop_review.card.connection_lost"), "SecondaryCaptionTextStyle"));
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            var action = new Button { Content = LocalizedText.Get("history.stop_review.card.action"), VerticalAlignment = VerticalAlignment.Center };
            action.Click += (_, _) => send(new ConnectionHistoryAction.StopReviewRequested(session.Id));
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
            return SurfaceCard.Create(grid, Tone.Caution, 17);
        }

        if (session.StopReviewDecision == ConnectionSessionStopReviewDecision.UserInitiated)
        {
            return new Border
            {
                Background = DesignTokens.SubtleBackground(Tone.Success),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(15),
                Child = new GlyphLabel(FluentSymbol.Completed, LocalizedText.Get("history.stop_review.accepted"), "BodyStrongTextBlockStyle", DesignTokens.Foreground(Tone.Success)),
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
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(StatusContent(
            "analysis.open",
            LocalizedText.Get("analysis.card.value"),
            LocalizedText.Format("analysis.card.caption", session.RawLogSummary.RecordCount),
            FluentSymbol.Chart,
            Tone.Indigo));
        var hint = new Grid();
        hint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hint.Children.Add(Typography.Strong(LocalizedText.Get("analysis.open.hint"), "CaptionTextBlockStyle", DesignTokens.Foreground(Tone.Indigo)));
        var chevron = new FontIcon { Glyph = FluentSymbol.ChevronRight, FontSize = 11, Foreground = DesignTokens.Foreground(Tone.Indigo) };
        Grid.SetColumn(chevron, 1);
        hint.Children.Add(chevron);
        stack.Children.Add(hint);
        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
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
        var card = SurfaceCard.Create(StatusContent(titleKey, value, caption, glyph, tone), padding: 18);
        card.MinHeight = 132;
        return card;
    }

    /// <summary>状態のカードの中身を作ります。</summary>
    /// <remarks>責務: 記号と題の行、値、補足を縦に並べます。</remarks>
    /// <returns>中身。</returns>
    private static StackPanel StatusContent(string titleKey, string value, string caption, string glyph, Tone tone)
    {
        var stack = new StackPanel { Spacing = 12 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = DesignTokens.Foreground(tone) });
        head.Children.Add(Typography.Strong(LocalizedText.Get(titleKey), "SecondaryCaptionTextStyle"));
        stack.Children.Add(head);
        stack.Children.Add(Typography.Text(value, "SubtitleTextBlockStyle", wraps: false));
        stack.Children.Add(Typography.Text(caption, "MonospaceCaptionTextStyle", wraps: false));
        return stack;
    }

    /// <summary>学習用データの準備状況のカードを作ります。</summary>
    /// <remarks>責務: 車両・端末の Raw・Mac の受領がそろったときだけ「準備完了」と出します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>カード。</returns>
    private static Border TrainingCard(ConnectionSession session)
    {
        var ready = session.Vehicle is not null
            && session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Available
            && session.RawLogSummary.RecordCount > 0
            && session.RawLogSummary.IsDurablyImportedByMac;
        var tone = ready ? Tone.Success : Tone.Caution;
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(FluentSymbol.Brain, tone, 64));
        var texts = new StackPanel { Spacing = 7 };
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.title"), "CardTitleTextStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Get(ready ? "history.training.ready.body" : "history.training.pending.body"), "SecondaryBodyTextStyle"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var scope = new MetricBlock(
            session.Vehicle is null ? "—" : "VEHICLE / SESSION / SEQUENCE",
            LocalizedText.Get("history.training.scope"),
            HorizontalAlignment.Right,
            valueStyleKey: "MonospaceCaptionTextStyle");
        Grid.SetColumn(scope, 2);
        grid.Children.Add(scope);
        return SurfaceCard.Create(grid, tone, 22);
    }

    /// <summary>データの系譜の説明のカードを作ります。</summary>
    /// <remarks>責務: 学習データの系譜の題と説明を出します。</remarks>
    /// <returns>カード。</returns>
    private static Border LineageCard()
    {
        var grid = new Grid { ColumnSpacing = 13 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = FluentSymbol.Branch, FontSize = 17, Foreground = DesignTokens.Foreground(Tone.Indigo) });
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.lineage.title"), "BodyStrongTextBlockStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Get("history.training.lineage.body"), "SecondaryCaptionTextStyle"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return new Border { Background = DesignTokens.SubtleBackground(Tone.Indigo), CornerRadius = new CornerRadius(14), Padding = new Thickness(17), Child = grid };
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
