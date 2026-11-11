namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログのフォルダーの大きさを調べ、ログを消します（設定画面の「診断ログを消す」）。
/// </summary>
/// <remarks>診断ログのファイル名（<see cref="DiagnosticFileName"/>）に合うものだけを数え、消します。ほかのファイルには触れません。</remarks>
public static class DiagnosticLogFolder
{
    /// <summary>
    /// 診断ログのファイルの合計サイズを返します。
    /// </summary>
    /// <param name="directory">診断ログのフォルダー。</param>
    /// <returns>合計バイト数。フォルダーがなければ 0。</returns>
    public static long TotalBytes(string directory)
    {
        long total = 0;
        foreach (var file in Enumerate(directory))
        {
            try
            {
                total += file.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 読めないファイルは数えない。
            }
        }

        return total;
    }

    /// <summary>
    /// 診断ログを消します。
    /// </summary>
    /// <remarks>責務: 今の実行（実行 ID の先頭 6 桁が同じ）のファイルは書き込み中なので残し、それ以外の診断ログを消します。消せないファイルは飛ばします。</remarks>
    /// <param name="directory">診断ログのフォルダー。</param>
    /// <param name="currentRunPrefix">今の実行 ID の先頭 6 桁（消さない）。</param>
    /// <returns>消したファイルの数。</returns>
    public static int Clear(string directory, string currentRunPrefix)
    {
        var deleted = 0;
        foreach (var file in Enumerate(directory))
        {
            if (DiagnosticFileName.TryParse(file.Name, out var name)
                && string.Equals(name.RunPrefix, currentRunPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 使用中などで消せないものは飛ばす。
            }
        }

        return deleted;
    }

    /// <summary>
    /// フォルダーの診断ログのファイルを並べます。
    /// </summary>
    /// <param name="directory">診断ログのフォルダー。</param>
    /// <returns>診断ログのファイル。</returns>
    internal static List<FileInfo> Enumerate(string directory)
    {
        var result = new List<FileInfo>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*.jsonl"))
        {
            if (DiagnosticFileName.TryParse(info.Name, out _))
            {
                result.Add(info);
            }
        }

        return result;
    }
}
