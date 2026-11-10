# ProjectZD8ForWindows 進捗

最終更新: 2026-11-10
README 確認済みのコミット: 5709f29

## 概要
- iOS/macOS アプリ「RevTorque Insight」（GitHub: Ryokugyoku/ProjectZD8、Swift/SwiftUI）を Windows（WinUI 3 / .NET 10）向けに作り直す。

## 現状
- Domain 層（iOS 版の Domain、Swift 65 ファイル）を `src/ProjectZD8.Domain`（net10.0）に移植済み。ビルドは警告 0。
- Domain のテスト 12 ファイルを `tests/ProjectZD8.Domain.Tests`（xUnit）に移植済み。56 件すべて合格。JSON の往復テスト 2 ファイルは Data 層で移す。
- WinUI 3 のアプリ本体はテンプレートのまま（Domain を参照しているだけ）。
- 元リポジトリの構成を調べ終えた（Domain / Application / Data / Platform のクリーンアーキテクチャ、テストあり）。
- git は C:\Program Files\Git にあるが PATH に入っていない。GitHub CLI（gh）はない。.NET SDK 10.0.403 あり。
- iOS 版を参照用に ..\ProjectZD8 に clone 済み（8bcda6f）。

## 直近の作業
- 2026-11-10: 全プロジェクトを .NET 10 に上げた。テスト 56 件合格、アプリ（x64）のビルドも成功。アプリの起動は未確認。
- 2026-11-10: Domain 層とそのテストを C# に移植した（テスト 56 件合格、移植のバグは見つからず）。移植の規約を docs/PORTING_CONVENTIONS.md に、README を新しく書いた。git init して最初のコミット（5709f29）を作った。
- 2026-11-10: iOS 版の CloudKit と認証の実装を調べ、Windows から同期する方式を設計した（下の「同期の設計メモ」）。
- 2026-11-10: 元リポジトリの構成を調べ、移植のしやすさを評価した。

## 次にやること
- iOS 版の設定とログアウト通知を CloudKit 化する（ProjectZD8 リポジトリ側の作業。下の設計案）
- 開発者側の準備：Apple Developer で Sign in with Apple 用の Services ID とリダイレクト先ドメインを用意する。CloudKit Console で API トークンを作る
- Application 層（iOS 版の Application/Features）を C# に移す（次の作業）
- Data 層：JSON の互換性（下の「JSON 互換の注意」）→ SQLite → CloudKit Web Services のクライアント → ELM327 と BLE の通信
- 画面（WinUI 3）

## 同期の設計メモ（iOS 版の実装から確認したこと）
- コンテナは iCloud.Ryokugyoku.ProjectZD8 の private database（default zone）。
- レコード名は「接頭辞 + SHA-256(Sign in with Apple の user ID、小文字 16 進)」。例：vehicle-catalog-<fp>、session-metadata-<fp>-<uuid>、raw-session-<fp>-<uuid>、mac-receipt-<fp>-<uuid>-<sha256(deviceID)>、deleted-session-<fp>-<uuid>。
- レコードの種類：VehicleCatalog と MaintenanceCatalog（どちらも JSON を catalogAsset に入れる）、ConnectionSessionMetadata、ConnectionSessionRawLog（payloadAsset、形式バージョン 2）、ConnectionSessionMacReceipt、ConnectionSessionDeletion。
- JSON の日付の形式に注意：車両カタログは Swift の既定（2001-01-01 からの秒数）。セッション転送は 1970 年からのミリ秒。
- Windows 側は CloudKit Web Services（REST）を使う。サインインは 2 回必要：
  1) Sign in with Apple の Web フロー：同じ Team の Services ID なら、iOS と同じ user ID（sub）が取れる。リダイレクト先は https のドメインが必須。
  2) CloudKit の Web 認証：iPhone と同じ Apple ID で iCloud にサインインし、ckWebAuthToken を取る。private DB にアクセスするのに必要。
- iCloud KV ストア（設定の同期、ほかの端末のログアウト通知）には Web API がない。→ iOS 版を直して CloudKit にも書く（下の設計案）。

## JSON 互換の注意（Data 層で再現する）
- Swift の既定 JSONEncoder：日付は 2001-01-01 UTC からの秒数（数値）、キーは camelCase、UUID は大文字の文字列、Data は base64。
- VehicleID などの ID は `{"rawValue": "..."}` になるはず（RawRepresentable ではないため）。実データで確認すること。
- 走行ログの転送（CloudKit）の JSON だけは、日付が 1970 年からのミリ秒で、キーをソートして出力する。
- ConnectionSessionTransferPackage のキーは accountIdentifier、entries、sessionID の 3 つだけ。
- 古いデータとの互換：ConnectionSession は stopReviewDecision と acquisitionDevice がなくても読めること。VehicleProfile は obdIdentifier がなくても読めること。

## iOS 版の CloudKit 化の設計案（未着手）
- 対象：UbiquitousKeyValueStoreAccountSettingsStore と UbiquitousKeyValueStoreAccountSessionRevocationStore（Data 層）、IOS/MacOSApplicationComposition、ProjectZD8AccountDataEraser。Port（AccountSettingsStorePort、AccountSessionRevocationPort）は変えない。
- Port は同期的に呼ばれる作りなので、KV ストアはそのまま残し、CloudKit にも書く層を重ねる。保存時は KV と CloudKit の両方に書く。監視を始めたときとアプリが前面に来たときに CloudKit を読み、新しければ反映する。プッシュ通知は後回し。
- レコード案：
  - AccountSettings / account-settings-<fp>：settingsData（Bytes。JSON、キーをソート）、updatedAt
  - AccountSessionRevocation / session-revocation-<fp>：marker（String、UUID）、revokedAt
- しばらくは KV にも書き続ける（古いバージョンのアプリと共存するため）。アカウント削除のときは、この 2 つのレコードも消す。
- CloudKit Console で、スキーマを本番環境（Production）に反映する必要がある（開発者の作業）。

## 検討事項・未決定
- Windows を「Mac 受領証」（ConnectionSessionMacReceipt）を出す端末として扱うか。出すと、iOS 側が生ログのキャッシュを消す判断に影響する。
- Sign in with Apple のリダイレクト先ドメインをどれにするか。そのドメインからカスタム URL スキームでアプリに戻す。
- git の user.name / user.email が未設定。最初のコミットは -c で作成者 Ryokugyoku を指定した。リモート（GitHub）にはまだ push していない。
- 移植で Swift と完全には同じにならない点（実害は小さい見込み）：計算式の中の漢数字（一 など）を Swift は数字として扱うが .NET は扱わない（エラーの種類が変わるだけ）。double の NaN 同士の比較が等しくなる。日時の精度が 100ns 単位。
- 対応する OBD アダプター：BLE の UART（Windows の BLE API で可能）と、Bluetooth Classic の SPP / COM ポート（OBDLink MX+ など）。iOS の ExternalAccessory（MFi）は Windows では使えない。
- 文字列の翻訳（Localizable.xcstrings、約520KB）を .resw に変換する方法。
- ライセンスが PolyForm Noncommercial なので、Windows 版も同じライセンスにするか。

## 決定事項
- 2026-11-10: 移植は Windows 版から始め、Domain → Application → Data → 画面の順に進める（開発者の指示）。
- 2026-11-10: 名前空間とプロジェクト名は iOS 版に合わせて ProjectZD8 にする。Domain は OS に依存しないクラスライブラリにして、JSON の属性は付けない。
- 2026-11-10: 全プロジェクトを .NET 10（LTS）に上げた（開発者の指示）。アプリは net10.0-windows10.0.19041.0、Domain とテストは net10.0。
- 2026-11-10: iCloud KV ストアの代わりに、iOS 版も設定とログアウト通知を CloudKit に書くよう直す（Windows から同期するため）。
- 2026-11-10: Windows 版は iOS 版とデータを同期する（開発者の希望）。CloudKit Web Services と Sign in with Apple の Web 版を使う。
