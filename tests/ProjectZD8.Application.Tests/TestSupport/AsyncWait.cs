namespace ProjectZD8.Application.Tests.TestSupport;

/// <summary>
/// 非同期に進む状態遷移をテストから待つための補助です。
/// </summary>
internal static class AsyncWait
{
    /// <summary>
    /// 条件が成り立つまで短い間隔で確認を繰り返します。
    /// </summary>
    /// <remarks>責務: 起動済みの非同期処理が状態へ反映されるのを、決められた時間だけ待ちます。</remarks>
    /// <param name="condition">待つ条件。</param>
    /// <param name="timeoutMilliseconds">あきらめるまでの時間（ミリ秒）。</param>
    /// <returns>時間内に条件が成り立てば <c>true</c>。</returns>
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(5);
        }

        return true;
    }
}
