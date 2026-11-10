using ProjectZD8.Data.Configuration;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// ブラウザーからカスタム URL スキーム（<c>revtorque-insight://auth/apple</c>、<c>revtorque-insight://auth/cloudkit</c>）で戻ってきた URL を、
/// 待っているサインインの処理へ渡します。
/// </summary>
/// <remarks>
/// アプリはプロトコルでの起動（またはすでに起動中のインスタンスへのリダイレクト）を受けたら <see cref="TryDeliver"/> を呼びます。
/// 経路（apple / cloudkit）ごとに待てるのは 1 件で、新しく待つと前の待ちは取り消されます。
/// </remarks>
public sealed class AuthorizationCallbackBroker
{
    /// <summary>Sign in with Apple の戻りの経路です。</summary>
    public const string AppleRoute = "apple";

    /// <summary>CloudKit のサインインの戻りの経路です。</summary>
    public const string CloudKitRoute = "cloudkit";

    /// <summary>受け付けるカスタム URL スキームです。</summary>
    private readonly string _scheme;

    /// <summary>状態を守る鍵です。</summary>
    private readonly Lock _lock = new();

    /// <summary>経路ごとの待ちです。</summary>
    private readonly Dictionary<string, TaskCompletionSource<Uri>> _waiters = new(StringComparer.Ordinal);

    /// <summary>
    /// 受け付けるカスタム URL スキームを指定して作ります。
    /// </summary>
    /// <remarks>責務: 1 件のスキームを戻りの受け口に固定します。</remarks>
    /// <param name="scheme">カスタム URL スキーム。<c>null</c> なら <c>revtorque-insight</c>。</param>
    public AuthorizationCallbackBroker(string? scheme = null)
    {
        _scheme = scheme ?? CloudServiceConfiguration.DefaultCallbackScheme;
    }

    /// <summary>
    /// 戻ってきた URL を、待っている処理へ渡します。
    /// </summary>
    /// <remarks>責務: 1 件の URL のスキームと経路を確かめ、対応する待ちを完了させます。</remarks>
    /// <param name="uri">プロトコルでの起動で受け取った URL。</param>
    /// <returns>待っている処理へ渡せたら <c>true</c>。スキームや経路が違うか、誰も待っていなければ <c>false</c>。</returns>
    public bool TryDeliver(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, _scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "auth", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var route = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        TaskCompletionSource<Uri>? waiter;
        lock (_lock)
        {
            if (!_waiters.Remove(route, out waiter))
            {
                return false;
            }
        }
        return waiter.TrySetResult(uri);
    }

    /// <summary>
    /// 指定の経路の戻りを待ちます。
    /// </summary>
    /// <remarks>責務: 1 件の経路について、戻りの URL・時間切れ・取り消しのどれかを待ちます。</remarks>
    /// <param name="route"><see cref="AppleRoute"/> か <see cref="CloudKitRoute"/>。</param>
    /// <param name="timeout">待つ時間の上限。</param>
    /// <param name="cancellationToken">待つのをやめる合図。</param>
    /// <returns>戻ってきた URL。</returns>
    /// <exception cref="TimeoutException">時間内に戻らなかった場合。</exception>
    /// <exception cref="OperationCanceledException">取り消されたか、同じ経路で新しく待ち始めた場合。</exception>
    public async Task<Uri> WaitForCallback(string route, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var waiter = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Uri>? previous;
        lock (_lock)
        {
            _waiters.Remove(route, out previous);
            _waiters[route] = waiter;
        }
        previous?.TrySetCanceled();
        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (_waiters.TryGetValue(route, out var current) && ReferenceEquals(current, waiter))
                {
                    _waiters.Remove(route);
                }
            }
        }
    }

    /// <summary>
    /// 指定の経路の待ちを取り消します。
    /// </summary>
    /// <remarks>責務: 利用者がサインインをやめたときに、待っている処理を終わらせます。</remarks>
    /// <param name="route">経路。</param>
    public void Cancel(string route)
    {
        TaskCompletionSource<Uri>? waiter;
        lock (_lock)
        {
            _waiters.Remove(route, out waiter);
        }
        waiter?.TrySetCanceled();
    }

    /// <summary>
    /// 戻りの URL のクエリーとフラグメントのパラメーターを読みます。
    /// </summary>
    /// <remarks>責務: <c>?a=1&amp;b=2</c> と <c>#a=1&amp;b=2</c> の両方を、URL デコードした辞書へ変換します（<c>+</c> は空白にしません）。</remarks>
    /// <param name="uri">戻りの URL。</param>
    /// <returns>パラメーター（同じ名前はクエリーを優先）。</returns>
    public static IReadOnlyDictionary<string, string> Parameters(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in new[] { uri.Fragment.TrimStart('#'), uri.Query.TrimStart('?') })
        {
            foreach (var pair in part.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = pair.IndexOf('=', StringComparison.Ordinal);
                var name = Uri.UnescapeDataString(index < 0 ? pair : pair[..index]);
                var value = index < 0 ? "" : Uri.UnescapeDataString(pair[(index + 1)..]);
                result[name] = value;
            }
        }
        return result;
    }
}
