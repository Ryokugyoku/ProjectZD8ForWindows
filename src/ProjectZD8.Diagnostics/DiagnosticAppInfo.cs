namespace ProjectZD8.Diagnostics;

/// <summary>
/// <c>app.start</c> に書くアプリと環境の情報です。
/// </summary>
/// <param name="Version">アプリのバージョン。</param>
/// <param name="OsBuild">OS のビルド（<c>10.0.26200.0</c> など）。</param>
/// <param name="Cpu">CPU（アーキテクチャと論理コア数など）。</param>
/// <param name="UiLanguage">表示言語（<c>ja-JP</c> など）。</param>
/// <param name="IsLocalMode">アカウントなしのローカルモードか。</param>
/// <param name="IsPackaged">MSIX のパッケージとして動いているか。</param>
public sealed record DiagnosticAppInfo(
    string Version,
    string OsBuild,
    string Cpu,
    string UiLanguage,
    bool IsLocalMode,
    bool IsPackaged);
