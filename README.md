# RevTorque Insight for Windows

iOS / macOS アプリ **RevTorque Insight**（[Ryokugyoku/ProjectZD8](https://github.com/Ryokugyoku/ProjectZD8)）の Windows 版です。
WinUI 3（Windows App SDK）と .NET 10 で作り直しています。CloudKit を通じて iOS 版とデータを同期することを目標にしています。

> 移植作業の途中です。今動いている範囲と今後の予定は [PROGRESS.md](PROGRESS.md) を見てください。

## 構成

| パス | 内容 |
|---|---|
| `ProjectZD8ForWindows.csproj`（ルート） | WinUI 3 のアプリ本体（パッケージ化した MSIX アプリ） |
| `src/ProjectZD8.Domain` | 業務ルール層（iOS 版の `ProjectZD8/Domain` に対応）。net10.0、OS に依存しない |
| `tests/ProjectZD8.Domain.Tests` | Domain 層の xUnit テスト |
| `docs/PORTING_CONVENTIONS.md` | Swift から C# に移すときの規約 |

アプリ本体の csproj はリポジトリのルートにあるので、`src/` と `tests/` の中のファイルはアプリのビルド対象から外しています。

技術的な識別子（名前空間、プロジェクト名）は、iOS 版と同じく `ProjectZD8` を使います。

## 必要なもの

- Windows 10 1809（17763）以降
- .NET SDK 10
- アプリ本体をビルドするには、Visual Studio 2022 以降と「WinUI アプリケーション開発」ワークロード

## ビルドとテスト

```bash
dotnet test tests/ProjectZD8.Domain.Tests
```

アプリ本体は Visual Studio で `ProjectZD8ForWindows.slnx` を開き、プラットフォーム（x64 など）を選んで実行します。

## ライセンス

元の iOS 版は PolyForm Noncommercial License 1.0.0 で公開されています。Windows 版のライセンスはまだ決めていません（PROGRESS.md の検討事項を参照）。
