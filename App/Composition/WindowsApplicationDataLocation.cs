using System;
using System.IO;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// アプリのデータと開発者の設定ファイルの置き場所を決めます。
/// </summary>
public static class WindowsApplicationDataLocation
{
    /// <summary>開発者ごとの秘密の設定ファイル名です（リポジトリにはコミットしない）。</summary>
    public const string LocalSettingsFileName = "appsettings.Local.json";

    /// <summary>
    /// アプリのデータ（SQLite・生ログ・設定）を置くフォルダを返します。
    /// </summary>
    /// <remarks>
    /// 責務: パッケージアプリならパッケージのローカルフォルダ、パッケージなしで動かしたときは
    /// %LOCALAPPDATA%\RevTorqueInsight を返します。
    /// </remarks>
    /// <returns>存在するフォルダの完全パス。</returns>
    public static string DataDirectory()
    {
        string directory;
        try
        {
            directory = global::Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevTorqueInsight");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 開発者の設定ファイル（Apple の Services ID、CloudKit の API トークンなど）のパスを返します。
    /// </summary>
    /// <remarks>
    /// 責務: データのフォルダ、アプリの実行フォルダの順に <see cref="LocalSettingsFileName"/> を探します。
    /// 見つからなければ null（環境変数だけで設定するか、ローカルモードで動く）。
    /// </remarks>
    /// <param name="dataDirectory">データのフォルダ。</param>
    /// <returns>見つかった設定ファイルのパス。なければ null。</returns>
    public static string? LocalSettingsPath(string dataDirectory)
    {
        foreach (var directory in new[] { dataDirectory, AppContext.BaseDirectory })
        {
            var path = Path.Combine(directory, LocalSettingsFileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
