using System.Text;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 前回の実行の様子です。
/// </summary>
/// <param name="RunPrefix">前回の実行 ID の先頭 6 桁。</param>
/// <param name="Crashed">前回の実行に <c>app.exit</c> がなかったら true。</param>
/// <param name="LastWriteTimeUtc">前回の実行のログを最後に書いた時刻。</param>
public sealed record DiagnosticPreviousRun(string RunPrefix, bool Crashed, DateTime LastWriteTimeUtc);

/// <summary>
/// 起動時のファイルの手入れ（前回の異常終了の検出、古いファイルの掃除）です。
/// </summary>
public static class DiagnosticFileMaintenance
{
    /// <summary>前回の最後のファイルのうち、<c>app.exit</c> を探す末尾の大きさです。</summary>
    private const int TailBytes = 64 * 1024;

    /// <summary><c>app.exit</c> の行に必ず入っている文字列です。</summary>
    private static readonly byte[] ExitMarker = Encoding.UTF8.GetBytes($"\"evt\":\"{DiagnosticEventNames.AppExit}\"");

    /// <summary>
    /// 前回の実行（最も新しく書かれた <c>app-*.jsonl</c> の系列）に <c>app.exit</c> があるかを調べます。
    /// </summary>
    /// <remarks>責務: 前回の系列の最後のファイルの末尾を読み、正常に終わったかを判定します。前回がなければ null。</remarks>
    /// <param name="directory">診断ログのフォルダー。</param>
    /// <param name="currentRunPrefix">今の実行 ID の先頭 6 桁（除く）。</param>
    /// <returns>前回の実行の様子。なければ null。</returns>
    public static DiagnosticPreviousRun? DetectPreviousRun(string directory, string currentRunPrefix)
    {
        var files = List(directory)
            .Where(file => file.Name.Kind == DiagnosticFileKind.App
                && !string.Equals(file.Name.RunPrefix, currentRunPrefix, StringComparison.Ordinal))
            .ToList();
        if (files.Count == 0)
        {
            return null;
        }

        var newest = files.MaxBy(file => file.Info.LastWriteTimeUtc)!;
        var last = files
            .Where(file => file.Name.RunPrefix == newest.Name.RunPrefix && file.Name.Date == newest.Name.Date)
            .MaxBy(file => file.Name.Index)!;
        bool exited;
        try
        {
            exited = TailContains(last.Info.FullName, ExitMarker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            exited = false;
        }

        return new DiagnosticPreviousRun(newest.Name.RunPrefix, !exited, newest.Info.LastWriteTimeUtc);
    }

    /// <summary>
    /// 上限（日数・合計サイズ）を超えた古いファイルを消します。
    /// </summary>
    /// <remarks>
    /// 責務: まず日数を超えたファイルを消し、残りを新しい順に足して合計サイズを超えた分を消します。
    /// 今の実行のファイルは消しません。消せないファイルは飛ばします。
    /// </remarks>
    /// <param name="directory">診断ログのフォルダー。</param>
    /// <param name="retention">上限。</param>
    /// <param name="nowUtc">今の時刻（UTC）。</param>
    /// <param name="currentRunPrefix">今の実行 ID の先頭 6 桁（消さない）。</param>
    /// <returns>消したファイルの数。</returns>
    public static int Clean(string directory, DiagnosticRetention retention, DateTime nowUtc, string currentRunPrefix)
    {
        var deleted = 0;
        var remaining = new List<(DiagnosticFileName Name, FileInfo Info)>();
        foreach (var file in List(directory))
        {
            if (string.Equals(file.Name.RunPrefix, currentRunPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (nowUtc - file.Info.LastWriteTimeUtc > retention.MaxAge)
            {
                deleted += TryDelete(file.Info) ? 1 : 0;
            }
            else
            {
                remaining.Add(file);
            }
        }

        long total = 0;
        foreach (var file in remaining.OrderByDescending(file => file.Info.LastWriteTimeUtc))
        {
            total += file.Info.Length;
            if (total > retention.MaxTotalBytes)
            {
                deleted += TryDelete(file.Info) ? 1 : 0;
            }
        }

        return deleted;
    }

    /// <summary>フォルダーの診断ログのファイルを並べます。</summary>
    /// <param name="directory">フォルダー。</param>
    /// <returns>ファイル名と情報。</returns>
    private static List<(DiagnosticFileName Name, FileInfo Info)> List(string directory)
    {
        var result = new List<(DiagnosticFileName, FileInfo)>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*.jsonl"))
        {
            if (DiagnosticFileName.TryParse(info.Name, out var name))
            {
                result.Add((name, info));
            }
        }

        return result;
    }

    /// <summary>ファイルの末尾に文字列があるかを調べます。</summary>
    /// <param name="path">ファイル。</param>
    /// <param name="marker">探すバイト列。</param>
    /// <returns>あれば true。</returns>
    private static bool TailContains(string path, byte[] marker)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = (int)Math.Min(stream.Length, TailBytes);
        stream.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return buffer.AsSpan().IndexOf(marker) >= 0;
    }

    /// <summary>ファイルを消します（消せなければ飛ばす）。</summary>
    /// <param name="info">ファイル。</param>
    /// <returns>消せたら true。</returns>
    private static bool TryDelete(FileInfo info)
    {
        try
        {
            info.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
