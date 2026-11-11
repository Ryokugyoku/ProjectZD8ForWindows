using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using ProjectZD8.Diagnostics;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// Windows アプリの診断ログを始めます（docs/LOGGING_STRATEGY.md）。
/// </summary>
/// <remarks>
/// チャンネルはこのアセンブリに埋め込んだ <c>DiagnosticsChannel</c>（csproj）、最低レベルの上書きは
/// 環境変数 <c>PROJECTZD8_DIAGNOSTICS_LEVEL</c> と <c>appsettings.Local.json</c> の <c>Diagnostics:Level</c> から読みます。
/// </remarks>
internal static class WindowsDiagnosticsBootstrap
{
    /// <summary>
    /// 診断ログを始め、出力先を差し込みます。
    /// </summary>
    /// <remarks>責務: アプリと環境の情報を集めて <see cref="DiagnosticSession.Start"/> に渡します。始められなくてもアプリは止めません。</remarks>
    /// <param name="dataDirectory">データのフォルダー。</param>
    /// <param name="settingsPath"><c>appsettings.Local.json</c> のパス。なければ null。</param>
    /// <param name="isLocalMode">アカウントなしのローカルモードか。</param>
    /// <returns>始めた診断ログ。始められなければ null。</returns>
    public static DiagnosticSession? Start(string dataDirectory, string? settingsPath, bool isLocalMode)
    {
        try
        {
            var assembly = typeof(WindowsDiagnosticsBootstrap).Assembly;
            var packageVersion = PackageVersion();
            return DiagnosticSession.Start(new DiagnosticSessionOptions
            {
                DataDirectory = dataDirectory,
                Channel = DiagnosticsChannelRawValue.FromAssembly(assembly),
                EnvironmentLevel = Environment.GetEnvironmentVariable(DiagnosticLevelResolver.EnvironmentVariable),
                SettingsFilePath = settingsPath,
                App = new DiagnosticAppInfo(
                    Version: packageVersion ?? AssemblyVersion(assembly),
                    OsBuild: Environment.OSVersion.Version.ToString(),
                    Cpu: $"{RuntimeInformation.ProcessArchitecture}/{RuntimeInformation.OSArchitecture} x{Environment.ProcessorCount}",
                    UiLanguage: CultureInfo.CurrentUICulture.Name,
                    IsLocalMode: isLocalMode,
                    IsPackaged: packageVersion is not null),
            });
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"診断ログを始められませんでした: {exception}");
            return null;
        }
    }

    /// <summary>パッケージのバージョンを返します。</summary>
    /// <returns>パッケージとして動いていればバージョン。そうでなければ null。</returns>
    private static string? PackageVersion()
    {
        try
        {
            var version = global::Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>アセンブリのバージョンを返します（パッケージなしで動かしたとき）。</summary>
    /// <param name="assembly">アプリのアセンブリ。</param>
    /// <returns>バージョン。</returns>
    private static string AssemblyVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}
