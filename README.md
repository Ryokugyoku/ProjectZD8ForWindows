# RevTorque Insight for Windows

iOS / macOS アプリ **RevTorque Insight**（[Ryokugyoku/ProjectZD8](https://github.com/Ryokugyoku/ProjectZD8)）の Windows 版です。
WinUI 3（Windows App SDK）と .NET 10 で作り直し、iOS 版と同じ機能（OBD2 アダプターでのライブ取得、走行ログ、分析、整備記録、ガレージ）を持ちます。
CloudKit Web Services を通じて iOS 版とデータを同期します。

> 今動いている範囲、確かめていないこと、未決定の事項は [PROGRESS.md](PROGRESS.md) を見てください。

## 構成

| パス | 内容 |
|---|---|
| `ProjectZD8ForWindows.csproj`（ルート） | WinUI 3 のアプリ本体（パッケージ化した MSIX アプリ）。`App/`（組み立て・起動・画面の流れ）、`Platform/Windows/`（画面と表示モデル）、`Strings/`（翻訳） |
| `src/ProjectZD8.Domain` | 業務ルール層（iOS 版の `ProjectZD8/Domain` に対応）。net10.0、OS に依存しない |
| `src/ProjectZD8.Application` | ユースケース層（iOS 版の `ProjectZD8/Application` に対応）。net10.0、Domain だけを参照する。機能ごとに `Features/<機能>/{Actions,Ports,State,UseCases}` |
| `src/ProjectZD8.Data` | 実装層のうち OS に依存しない部分（iOS 版の `ProjectZD8/Data`）。net10.0。保存（SQLite とログストア）、JSON 互換、CloudKit Web Services、Sign in with Apple（Web）、ELM327 と OBD のアダプター |
| `src/ProjectZD8.Data.Windows` | Windows の API が要る実装。USB シリアル（COM）、Bluetooth Classic（RFCOMM）、BLE（GATT の UART）、機器の検出と到来の監視、資格情報の保存（PasswordVault / DPAPI）、ブラウザーの起動、スリープ抑止 |
| `tests/ProjectZD8.Domain.Tests`、`tests/ProjectZD8.Application.Tests`、`tests/ProjectZD8.Data.Tests` | 各層の xUnit テスト |
| `tools/ProjectZD8.LogStoreBenchmark` | 生ログストアの性能測定（100 万件の書込みと範囲読取） |
| `scripts/ConvertXcstrings.cs` | iOS 版の `Localizable.xcstrings` と `Strings/WindowsOnly.xcstrings` から `.resw` を作る |
| `docs/PORTING_CONVENTIONS.md` | Swift から C# に移すときの規約 |
| `docs/CLOUD_SETUP.md`、`docs/web/` | iCloud 同期と Sign in with Apple の準備（開発者向け）と、サインイン後に戻るページの見本 |

アプリ本体の csproj はリポジトリのルートにあるので、`src/`・`tests/`・`tools/`・`scripts/` の中のファイルはアプリのビルド対象から外しています。
技術的な識別子（名前空間、プロジェクト名）は、iOS 版と同じく `ProjectZD8` を使います。

## データの保存（iOS 版との違い）

iOS 版はすべてのデータを GRDB（SQLite）に入れています。**Windows 版は 2 つに分けています。**

- **マスタ系は SQLite**（Microsoft.Data.Sqlite、`<データのフォルダー>\projectzd8.sqlite`）：車両、PID の定義（標準と ZD8 の初期データ）、車両ごとの PID の対応、整備記録、接続セッションの概要、取得マニフェスト。
- **トランザクション系は追記専用のログストア**（`<データのフォルダー>\logs\<セッション>\`）：生ログ、取得バッチ、PID 要求の証跡。
  セッションごとのセグメントファイルに「長さ＋CRC32」で枠付けして追記し、疎な索引で sequence と時刻の範囲を速く読み、確定したセグメントは圧縮します。
  書込みは背景スレッドでまとめて行うので計測を止めず、途中で落ちても書きかけの末尾を切り詰めて開き直します。
  Domain のリポジトリのインターフェースの裏に隠しているので、保存方式はあとから替えられます。

データのフォルダーは、パッケージアプリならパッケージの `LocalState`（`%LOCALAPPDATA%\Packages\<パッケージファミリー名>\LocalState`）です。
iOS 版とやりとりする JSON（CloudKit のレコード）の形式は、保存方式とは関係なく iOS 版と同じです。

## OBD2 アダプター

- USB：ELM327 系や OBDLink EX の COM ポート（115200bps、8N1）
- Bluetooth Classic：SPP / RFCOMM（OBDLink MX+ など。ペアリング済みの機器）
- BLE：GATT の UART（既知の UART サービスのプロファイル）
- デモ用アダプター（実機なしで全画面を試せる）

設定画面でプライマリーアダプターを選ぶと既定として記憶し、BLE の既定アダプターが近くに来るとシェルの InfoBar で接続を尋ねます。

## アカウントと同期

- Apple の Services ID と CloudKit の API トークンを設定すると、Web 版の Sign in with Apple でサインインし、設定画面から iCloud（CloudKit）にサインインして iOS 版と同期します。手順は [docs/CLOUD_SETUP.md](docs/CLOUD_SETUP.md)。
- 設定は `appsettings.Local.json`（コミットしない。見本は `appsettings.Local.example.json`）か環境変数 `PROJECTZD8_*` から読みます。
- **設定がなければ、アカウントなしのローカルモード**で動きます（データはこの PC の中だけ）。

## 必要なもの

- Windows 10 1809（17763）以降
- .NET SDK 10
- アプリ本体をビルドするには、Visual Studio 2022 以降と「WinUI アプリケーション開発」ワークロード（コマンドラインの `dotnet build` でもビルドできます）

## ビルドとテスト

```bash
dotnet build ProjectZD8ForWindows.slnx -p:Platform=x64
dotnet test ProjectZD8ForWindows.slnx -p:Platform=x64
```

アプリ本体は Visual Studio で `ProjectZD8ForWindows.slnx` を開き、プラットフォーム（x64 など）を選んで実行します。
コマンドラインでは、ビルドしたあと `Add-AppxPackage -Register bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AppxManifest.xml` で登録してスタートメニューから起動できます。

翻訳を作り直すとき（iOS 版を `..\ProjectZD8` に clone してあるとき）：

```bash
dotnet run --file scripts/ConvertXcstrings.cs -- ../ProjectZD8/ProjectZD8/Resources/Localization/Localizable.xcstrings Strings Strings/WindowsOnly.xcstrings
```

生ログストアの性能測定：

```bash
dotnet run -c Release --project tools/ProjectZD8.LogStoreBenchmark
```

## ライセンス

元の iOS 版は PolyForm Noncommercial License 1.0.0 で公開されています。Windows 版のライセンスはまだ決めていません（PROGRESS.md の検討事項を参照）。
