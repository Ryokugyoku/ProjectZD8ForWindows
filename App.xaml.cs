using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ProjectZD8.Diagnostics;
using ProjectZD8ForWindows.AppRoot.Composition;
using ProjectZD8ForWindows.AppRoot.Flow;
using ProjectZD8ForWindows.AppRoot.Lifecycle;

namespace ProjectZD8ForWindows;

/// <summary>
/// アプリの入口のオブジェクトです。組み立て、メインウインドウの表示、プロトコル起動の受け付けを行います。
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    /// <summary>プロトコル起動の中継です。</summary>
    private readonly ProtocolActivationRelay _protocolActivation = new();

    /// <summary>メインウインドウです。</summary>
    private MainWindow? _window;

    /// <summary>UI スレッドのディスパッチャーです。</summary>
    private DispatcherQueue? _dispatcherQueue;

    /// <summary>アプリを作ります。</summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// 画面の処理で捕まえられなかった例外を記録します（終了はさせたまま）。
    /// </summary>
    /// <remarks>責務: 直前の出来事と例外を診断ログの <c>crash-*.jsonl</c> へ同期で書きます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">例外の情報。</param>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        DiagnosticLog.ReportCrash(e.Exception, DiagnosticCrashSource.Xaml);

    /// <summary>
    /// UI スレッド以外で捕まえられなかった例外を記録します（プロセスは落ちる）。
    /// </summary>
    /// <remarks>責務: 直前の出来事と例外を診断ログの <c>crash-*.jsonl</c> へ同期で書きます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">例外の情報。</param>
    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            DiagnosticLog.ReportCrash(exception, DiagnosticCrashSource.AppDomain);
        }
    }

    /// <summary>
    /// 待たれなかったタスクの例外を記録します（プロセスは落ちない）。
    /// </summary>
    /// <remarks>責務: 例外を Error として診断ログへ書きます（<c>crash-*.jsonl</c> にも残す）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">例外の情報。</param>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        DiagnosticLog.ReportCrash(e.Exception, DiagnosticCrashSource.UnobservedTask);

    /// <summary>
    /// <c>revtorque-insight://</c> で起動されたときに URI を受け取る処理です（UI スレッドで呼ばれる）。
    /// </summary>
    /// <remarks>
    /// リードが Sign in with Apple・CloudKit のコールバック（<c>AuthorizationCallbackBroker.TryDeliver</c>）につなぎます。
    /// 設定する前に届いた URI はためておき、設定したときに渡します。
    /// </remarks>
    public Action<Uri>? ProtocolActivated
    {
        get => _protocolActivation.Receiver;
        set => _protocolActivation.Receiver = value;
    }

    /// <summary>
    /// 起動されたときの処理です。
    /// </summary>
    /// <remarks>責務: 依存を組み立ててメインウインドウを開き、最初の起動と転送されてくる起動を受け付けます。</remarks>
    /// <param name="args">起動の情報。</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var root = WindowsApplicationComposition.MakeRoot(Environment.GetCommandLineArgs());
        var flow = new WindowsApplicationFlow(root);
        _window = new MainWindow(flow);
        _window.Closed += (_, _) => flow.Shutdown();
        ProtocolActivated = root.DeliverProtocolActivation;

        var current = AppInstance.GetCurrent();
        current.Activated += OnRedirectedActivation;
        HandleActivation(current.GetActivatedEventArgs());
        _window.Activate();
    }

    /// <summary>
    /// 2 つ目の起動から転送されてきた起動の処理です（UI スレッド以外で呼ばれる）。
    /// </summary>
    /// <remarks>責務: 転送された起動を UI スレッドへ移し、ウインドウを前面に出します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">起動の情報。</param>
    private void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        AppLifecycleLog.ActivationRedirected(AppLifecycleLog.Logger, args.Kind.ToString());
        _dispatcherQueue?.TryEnqueue(() =>
        {
            HandleActivation(args);
            _window?.BringToFront();
        });
    }

    /// <summary>
    /// 起動の情報のうち、プロトコル起動の URI を中継へ渡します。
    /// </summary>
    /// <remarks>責務: プロトコル起動を見分けて URI を取り出します。</remarks>
    /// <param name="args">起動の情報。</param>
    private void HandleActivation(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.Protocol
            && args.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol
            && protocol.Uri is { } uri)
        {
            AppLifecycleLog.ProtocolActivation(AppLifecycleLog.Logger, Redact.Url(uri));
            _protocolActivation.Deliver(uri);
        }
    }
}
