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
| `src/ProjectZD8.Diagnostics` | 診断ログの土台（JSON Lines への書込み、チャンネル Dev / Beta / Stable、リングバッファ、伏せる処理）。net10.0。Domain からは参照しない |
| `tests/ProjectZD8.Domain.Tests`、`tests/ProjectZD8.Application.Tests`、`tests/ProjectZD8.Data.Tests`、`tests/ProjectZD8.Diagnostics.Tests` | 各層の xUnit テスト |
| `tools/ProjectZD8.LogStoreBenchmark` | 生ログストアの性能測定（100 万件の書込みと範囲読取） |
| `scripts/ConvertXcstrings.cs` | iOS 版の `Localizable.xcstrings` と `Strings/WindowsOnly.xcstrings` から `.resw` を作る |
| `scripts/Analyze-Diagnostics.ps1` | 不具合レポート（zip）や診断ログのフォルダーを読み、環境・実行ごとの件数・Error / Fatal の一覧を出す。`-Report 7F3A-1842` で報告番号の前後、`-Event` で絞り込み（PowerShell 5.1 / 7） |
| `docs/PORTING_CONVENTIONS.md` | Swift から C# に移すときの規約 |
| `docs/LOGGING_STRATEGY.md` | 診断ログの戦略（ログの形、版の切り替え、伏せる項目、分析のしかた） |
| `docs/CLOUD_SETUP.md`、`docs/web/` | iCloud 同期と Sign in with Apple の準備（開発者向け）と、サインイン後に戻るページの見本 |
| `docs/USER_GUIDE.html` | お客様向けのご利用ガイド（画像は `docs/store/screenshots/` を参照） |
| `docs/STORE_RELEASE.md`、`.github/workflows/`、`scripts/Set-PackageIdentity.ps1` | Microsoft Store への公開（プライベート対象ユーザーでのベータ配布）と、GitHub Actions のビルド・テスト・自動リリース |
| `Design/AppIcon-1024.png`、`scripts/GenerateAppIcons.cs` | アプリアイコンの元画像（iOS 版と同じ）と、そこから `Assets/` のロゴを全サイズ作るスクリプト（`dotnet run --file scripts/GenerateAppIcons.cs -- Design/AppIcon-1024.png Assets`） |

アプリ本体の csproj はリポジトリのルートにあるので、`src/`・`tests/`・`tools/`・`scripts/` の中のファイルはアプリのビルド対象から外しています。
技術的な識別子（名前空間、プロジェクト名）は、iOS 版と同じく `ProjectZD8` を使います。

## データの保存（iOS 版との違い）

iOS 版はすべてのデータを GRDB（SQLite）に入れています。**Windows 版は 2 つに分けています。**

- **マスタ系は SQLite**（Microsoft.Data.Sqlite、`<データのフォルダー>\projectzd8.sqlite`）：車両、PID の定義（標準と ZD8 の初期データ）、車両ごとの PID の対応、整備記録、接続セッションの概要、取得マニフェスト。
- **トランザクション系は追記専用のログストア**（`<データのフォルダー>\logs\<セッション>\`）：生ログ、取得バッチ、PID 要求の証跡。
  セッションごとのセグメントファイルに「長さ＋CRC32」で枠付けして追記し、疎な索引で sequence と時刻の範囲を速く読み、確定したセグメントは圧縮します。
  書込みは背景スレッドでまとめて行うので計測を止めず、途中で落ちても書きかけの末尾を切り詰めて開き直します。
  Domain のリポジトリのインターフェースの裏に隠しているので、保存方式はあとから替えられます。
- **診断ログ**（`<データのフォルダー>\diagnostics\`、JSON Lines）：アプリの動きと不具合の記録。生ログの `logs\` とは別のフォルダーです。端末の中だけに置き、自動では送信しません（[PRIVACY.md](PRIVACY.md)）。

データのフォルダーは、パッケージアプリならパッケージの `LocalState`（`%LOCALAPPDATA%\Packages\<パッケージファミリー名>\LocalState`）です。
iOS 版とやりとりする JSON（CloudKit のレコード）の形式は、保存方式とは関係なく iOS 版と同じです。

## OBD2 アダプター

- USB：ELM327 系や OBDLink EX の COM ポート（115200bps、8N1）
- Bluetooth Classic：SPP / RFCOMM（OBDLink MX+ など。ペアリング済みの機器）
- BLE：GATT の UART（既知の UART サービスのプロファイル）
- デモ用アダプター（実機なしで全画面を試せる）

設定画面でプライマリーアダプターを選ぶと既定として記憶し、BLE の既定アダプターが近くに来るとシェルの InfoBar で接続を尋ねます。

## アカウントと同期

- **Sign in with Apple と iCloud 同期は、今は「近日対応（Coming Soon）」です。** ログイン画面と設定画面には押せないボタンに「近日対応」の印を付けて出し、使える入口は「この PC で続ける」（アカウントなしのローカルモード。データはこの PC の中だけ）です。
- 機能フラグ `AppleSignIn:Enabled`（既定は `false`）を `true` にし、Apple の Services ID・https のリダイレクト先・CloudKit の API トークンをそろえると有効になります。Web 版の Sign in with Apple でサインインし、設定画面から iCloud（CloudKit）にサインインして iOS 版と同期します。手順とチェックリストは [docs/CLOUD_SETUP.md](docs/CLOUD_SETUP.md)。
- 設定は `appsettings.Local.json`（コミットしない。見本は `appsettings.Local.example.json`）か環境変数 `PROJECTZD8_*` から読みます。
- 有効にしたあと、この PC で初めて Apple でサインインしたとき、ローカルモードで貯めた車両・走行ログ・整備記録・設定を、そのアカウントへ 1 回だけ引き継ぎます。

## 画面と操作（Windows 向けの工夫）

- ウインドウの幅で 4 段階に配置が変わります（幅クラス：〜759 / 760〜1199 / 1200〜1599 / 1600〜。最小 640×420）。ナビゲーションは広いと題つきのペイン（設定は下）、中くらいだと記号だけのレール、狭いと画面の下のタブ帯になります。`Ctrl+1`〜`Ctrl+6` で各画面、`Ctrl+,` で設定。
- タイトルバーに**接続コンソール**（アダプター名・状態・接続／切断・「ライブを表示」）があり、どの画面からでも接続・切断できます（ホームでは状態だけ。操作はホームの大きなボタン）。
- 接続履歴はパンくずで階層を移動でき、タイトルバーの戻るボタン・`Alt+←`・マウスの戻るボタンで 1 段戻れます。ウインドウが広いと、セッション一覧と詳細（整備も一覧と記録）を左右に並べ、狭いと片方だけを出します。
- 設定は「全般・接続・データと同期・診断・アカウント」のグループに分かれ、広い幅では左の目次から各グループへ移れます。
- セッション・整備記録・車両のカードは右クリックで操作メニューが開きます。整備記録と車両の編集は `Ctrl+S` で保存、ガレージと整備は `F5` で更新。
- 画面は要素を作り直さずに値だけを差し替えます（ライブ値は 100ms ごとにまとめて反映）。ライブ取得中も分類のボタンやメニューのクリックが取りこぼされません。作りの決まりは `docs/UI_GUIDELINES.md`。

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

診断ログの版（Dev / Beta / Stable）は MSBuild プロパティ `DiagnosticsChannel` で決まります。Debug 構成は既定で Dev、それ以外は Stable です。ベータの版で作るには次のようにします。

```bash
dotnet build ProjectZD8ForWindows.slnx -p:Platform=x64 -c Release -p:DiagnosticsChannel=Beta
```

Release ビルドはトリミングしません（トリミングすると起動直後に落ちるため）。

## リリース

タグ `v1.2.3` を push すると、GitHub Actions がテストし、Store 用のパッケージ（x64 の `.msixupload`）を作り、Microsoft Store に提出します。
今はプライベート対象ユーザー（指定した Microsoft アカウントだけ）に向けたベータ配布です。最初の 1 回の手作業と必要な設定は [docs/STORE_RELEASE.md](docs/STORE_RELEASE.md) を見てください。

## 翻訳と性能測定

翻訳を作り直すとき（iOS 版を `..\ProjectZD8` に clone してあるとき）：

```bash
dotnet run --file scripts/ConvertXcstrings.cs -- ../ProjectZD8/ProjectZD8/Resources/Localization/Localizable.xcstrings Strings Strings/WindowsOnly.xcstrings
```

生ログストアの性能測定：

```bash
dotnet run -c Release --project tools/ProjectZD8.LogStoreBenchmark
```

## ライセンス

This project is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE).

本プロジェクトは、iOS 版と同じく [PolyForm Noncommercial License 1.0.0](LICENSE) に基づきソースを公開しています。著作権者本人を除き、商用目的での利用・改変・再配布は許可されません。
