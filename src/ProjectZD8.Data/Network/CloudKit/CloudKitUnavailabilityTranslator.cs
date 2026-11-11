namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit を使えない状態（サインインしていない・サインインが切れた）を、Domain の「利用できない」エラーへ置き換えます。
/// </summary>
internal static class CloudKitUnavailabilityTranslator
{
    /// <summary>
    /// 処理を実行し、使えない状態の例外を指定のエラーへ置き換えます。
    /// </summary>
    /// <remarks>責務: <see cref="CloudKitUnavailableException"/> と 421 を、Application が判定できるエラーへ変換します。</remarks>
    /// <typeparam name="T">結果の型。</typeparam>
    /// <param name="action">CloudKit を使う処理。</param>
    /// <param name="unavailable">置き換えるエラーを作る処理。</param>
    /// <returns>処理の結果。</returns>
    public static async Task<T> Run<T>(Func<Task<T>> action, Func<Exception> unavailable)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (CloudKitUnavailableException)
        {
            SyncLog.UnavailableTranslated(SyncLog.Logger, "unavailable");
            throw unavailable();
        }
        catch (CloudKitAuthenticationRequiredException)
        {
            SyncLog.UnavailableTranslated(SyncLog.Logger, "authentication-required");
            throw unavailable();
        }
    }

    /// <summary>
    /// 結果のない処理を実行し、使えない状態の例外を指定のエラーへ置き換えます。
    /// </summary>
    /// <remarks>責務: 結果のない処理について <see cref="Run{T}"/> と同じ変換をします。</remarks>
    /// <param name="action">CloudKit を使う処理。</param>
    /// <param name="unavailable">置き換えるエラーを作る処理。</param>
    /// <returns>処理。</returns>
    public static Task Run(Func<Task> action, Func<Exception> unavailable) =>
        Run(
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            unavailable);
}
