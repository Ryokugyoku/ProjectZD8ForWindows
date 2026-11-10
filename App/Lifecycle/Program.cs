using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ProjectZD8ForWindows.AppRoot.Lifecycle;

/// <summary>
/// プロセスの入口です。2 つ目の起動（プロトコル起動を含む）を最初のインスタンスへ渡して、アプリを 1 つだけにします。
/// </summary>
/// <remarks>
/// XAML の自動生成の Main は csproj の <c>DISABLE_XAML_GENERATED_MAIN</c> で止めています。
/// 振り分けの待ち方は Windows App SDK の単一インスタンスのサンプルと同じです（STA のメッセージを止めないよう CoWaitForMultipleObjects で待つ）。
/// </remarks>
public static class Program
{
    /// <summary>単一インスタンスを登録するキーです。</summary>
    public const string MainInstanceKey = "RevTorqueInsight.Main";

    /// <summary>
    /// アプリを起動します。
    /// </summary>
    /// <remarks>責務: 最初のインスタンスなら XAML アプリを開始し、そうでなければ起動要求を最初のインスタンスへ渡して終わります。</remarks>
    /// <param name="args">コマンドライン引数。</param>
    /// <returns>終了コード。</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToMainInstanceIfNeeded())
        {
            return 0;
        }

        Application.Start(_parameters =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// ほかにインスタンスがあれば、起動要求をそちらへ渡します。
    /// </summary>
    /// <remarks>責務: 単一インスタンスのキーを登録し、自分が最初でなければ起動引数を転送します。</remarks>
    /// <returns>転送したら true（このプロセスは終わる）。</returns>
    private static bool RedirectToMainInstanceIfNeeded()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        if (mainInstance.IsCurrent)
        {
            return false;
        }

        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var redirectEvent = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try
            {
                mainInstance.RedirectActivationToAsync(activationArguments).AsTask().Wait();
            }
            finally
            {
                SetEvent(redirectEvent);
            }
        });

        const uint CwmoDefault = 0;
        const uint Infinite = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CwmoDefault, Infinite, 1, [redirectEvent], out _);
        return true;
    }

    /// <summary>Win32 のイベントを作ります。</summary>
    /// <param name="lpEventAttributes">セキュリティ属性。</param>
    /// <param name="bManualReset">手動リセットなら true。</param>
    /// <param name="bInitialState">初期状態。</param>
    /// <param name="lpName">名前。</param>
    /// <returns>イベントのハンドル。</returns>
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    /// <summary>Win32 のイベントをシグナル状態にします。</summary>
    /// <param name="hEvent">イベントのハンドル。</param>
    /// <returns>成功したら true。</returns>
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    /// <summary>STA のメッセージを処理しながらハンドルを待ちます。</summary>
    /// <param name="dwFlags">待ち方のフラグ。</param>
    /// <param name="dwTimeout">待つ時間（ミリ秒）。</param>
    /// <param name="cHandles">ハンドルの数。</param>
    /// <param name="pHandles">ハンドル。</param>
    /// <param name="lpdwindex">シグナルになったハンドルの番号。</param>
    /// <returns>HRESULT。</returns>
    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint dwFlags,
        uint dwTimeout,
        ulong cHandles,
        IntPtr[] pHandles,
        out uint lpdwindex);
}
