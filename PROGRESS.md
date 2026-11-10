# ProjectZD8ForWindows 進捗

最終更新: 2026-11-10
README 確認済みのコミット: cb6c663

## 概要
- iOS/macOS アプリ「RevTorque Insight」（GitHub: Ryokugyoku/ProjectZD8、Swift/SwiftUI）を Windows（WinUI 3 / .NET 10）向けに作り直す。

## 現状
- Domain 層（iOS 版の Domain、Swift 65 ファイル）を `src/ProjectZD8.Domain`（net10.0）に移植済み。ビルドは警告 0。
- Domain のテスト 12 ファイルを `tests/ProjectZD8.Domain.Tests`（xUnit）に移植済み。56 件すべて合格。JSON の往復テスト 2 ファイルは Data 層で移す。
- Application 層（iOS 版の Application/Features、9 機能・Swift 約 80 ファイル）を `src/ProjectZD8.Application`（net10.0、Domain だけを参照）に移植済み。テスト 30 ファイル・155 件を `tests/ProjectZD8.Application.Tests` に移し、すべて合格。ビルドは警告 0。
- Data 層を作った（2026-11-10）。`src/ProjectZD8.Data`（net10.0）と `src/ProjectZD8.Data.Windows`（Windows の API）。テスト 283 件（`tests/ProjectZD8.Data.Tests`）すべて合格。iOS の Data のテストはほぼすべて移植（対象外は下の検討事項）。
  - 永続化：マスタ系は SQLite（`Persistence/Sqlite`、`dir/projectzd8.sqlite`）、生ログと取得の証跡（batch・PID 要求）は自作の追記専用ログストア（`Persistence/LogStore`、`dir/logs/<セッション>/`）。iOS 版は全部を GRDB（SQLite）に入れているので、ここが違う。
  - デバイス：ELM327、OBDLink EX の PID 取得、VIN、デモ用アダプター、USB シリアル（COM、115200bps）、Bluetooth Classic（RFCOMM / SPP）、BLE（GATT の UART）、機器の検出、既定アダプターの記憶（JSON ファイル）、到来の監視と接続の確認（アプリ内のイベント）、スリープ抑止（SetThreadExecutionState）。
  - ネットワークと認証：Swift の Codable と同じ JSON、CloudKit Web Services のクライアント（REST）と各リポジトリ、AccountSettings と AccountSessionRevocation のレコード、Web 版の Sign in with Apple（ID トークンを RS256 で検証して sub を取り出す）、ckWebAuthToken の取得、PasswordVault（だめなら DPAPI）への保存、設定ファイル（`appsettings.Local.json` と環境変数 `PROJECTZD8_*`）。設定がなければローカルモード。開発者の手順は `docs/CLOUD_SETUP.md`。
- 生ログストアの性能（Release、24 コア、1 セッションに 100 万件）：書込み 124 万件/秒、Append の遅延 p50 0.6µs・p99 2.0µs・p99.9 20.7µs（最大 14.1ms）。sequence の範囲読取（1,000 件）p50 0.66ms・p99 1.06ms、時間窓（10 秒分）p50 0.65ms・p99 1.09ms、全件読取 377ms。ディスク 26.5MiB（27.8 バイト/件）。異常終了後の開き直し（全走査）204ms。測定は `tools/ProjectZD8.LogStoreBenchmark`。
- 元リポジトリの構成を調べ終えた（Domain / Application / Data / Platform のクリーンアーキテクチャ、テストあり）。
- git は C:\Program Files\Git にあるが PATH に入っていない。GitHub CLI（gh）はない。.NET SDK 10.0.403 あり。
- iOS 版を参照用に ..\ProjectZD8 に clone 済み（8bcda6f）。

## 直近の作業
- 2026-11-10: Application 層とそのテストを C# に移植した（9 機能、テスト 155 件合格、Domain の修正なし）。サブエージェント 5 つで機能ごとに並行作業。アプリ（x64）のビルドも成功。README の構成表に追加。
- 2026-11-10: 全プロジェクトを .NET 10 に上げた。テスト 56 件合格、アプリ（x64）のビルドも成功。アプリの起動は未確認。
- 2026-11-10: Domain 層とそのテストを C# に移植した（テスト 56 件合格、移植のバグは見つからず）。移植の規約を docs/PORTING_CONVENTIONS.md に、README を新しく書いた。git init して最初のコミット（5709f29）を作った。
- 2026-11-10: iOS 版の CloudKit と認証の実装を調べ、Windows から同期する方式を設計した（下の「同期の設計メモ」）。
- 2026-11-10: 元リポジトリの構成を調べ、移植のしやすさを評価した。

## 次にやること
- iOS 版の設定とログアウト通知を CloudKit 化する（ProjectZD8 リポジトリ側の作業。下の設計案）
- 開発者側の準備：Apple Developer で Sign in with Apple 用の Services ID とリダイレクト先ドメインを用意する。CloudKit Console で API トークンを作る
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
- アカウント削除で AccountSessionRevocation のレコードを消すかどうか。今の実装は消さない（ほかの端末へログアウトを知らせるのに要るため）。「iOS 版の CloudKit 化の設計案」の「消す」と食い違っている。→ 開発者に確認。
- CloudKit Web Services の確かめていない点：新しい ckWebAuthToken が応答のどこに入るか（JSON の ckWebAuthToken / ckSession と同名のヘッダーを見ている）、CloudKit Console の Sign In Callback にカスタム URL スキームを直接入れられるか（入れられなければ https のページ経由。docs/web/cloudkit-callback.html）、update / forceReplace の実際の動き、asset の receipt の形。
- Web の Sign in with Apple で得た sub が iOS の credential.user と一致するか（同じ Team・同じ主 App ID なら一致するはず）。実機で確かめる。
- Swift の sortedKeys の並び順と、iOS が実際に書いた JSON とのバイト単位の比較はしていない（読み込みは順番に依存しない）。
- 生ログストアと SQLite にまたがる操作（セッション終了・回復・生ログの置換）は、SQLite のトランザクション中に LogStore を fsync してから SQLite を確定する。fsync のあとに SQLite の確定だけが失敗すると不整合が残る（まれ）。
- 生ログの除去と復元で、Windows は取得証拠を残したまま生ログだけを消す。iOS は Raw を参照する PID 要求があると外部キーで失敗するので、iOS 側の不具合かもしれない。→ iOS 側で確認。
- Domain の取得 platform に Windows の値がない。manifest の CHECK 制約を緩めたが、Windows で取ったセッションを何として記録するか（今は macOS 相当）は未決定。Domain に Windows を足すと iOS 版との JSON 互換に影響する。
- 計測中のセッションは batch の証拠をすべてメモリに持つ。2 時間を超える長いセッションのメモリ量は未測定。
- 同じデータフォルダを複数のプロセスで開くことは想定していない（単一インスタンスにする）。
- 実機で見る必要がある点（デバイス）：FTDI ドライバーが COM ポートのインターフェイスを登録するか、BusReportedDeviceDesc と Manufacturer が値を返すか、GetGattServicesAsync(Uncached) で接続が始まるか、SPP をキャッシュから取れない機器での 10 秒の照会。
- iOS のテストで移植しなかったもの：旧 DB（v8）からの移行（Windows に旧 DB はない）、日時が無限大のテスト（C# にない）、取得性能の計測マトリクス（ベンチマークで代用）。Application.Tests の中のメモリ上の代用リポジトリや PID 定義の写し（PROGRESS の上の項目）は、Data の実装に置き換えられる（Application.Tests から Data を参照する必要がある）。
- Application 移植で Swift と完全には同じにならない点：
  - Swift の `Task {}` は後で走るが、C# の async は最初の await まで同期で走る。状態遷移の順番は同じにしたが、同じ同期区間で続けて操作を送ると（例：VehicleSaved の直後に EditCancelled）見える状態が変わることがある。LiveTelemetryModel だけは Task.Yield で Swift に近づけた。モデルは UI スレッド（単一スレッド）から呼ぶ前提。
  - Swift の actor 3 つ（取得コントローラー、マニフェスト作成、バッチ保存）は lock 付きのクラスにした。ポートに CancellationToken がないので、実行中の呼び出しは止められず、世代番号の確認で古い結果を捨てる（Swift と同じ結果）。
  - 並べ替え：localizedStandardCompare は現在のカルチャ＋IgnoreCase＋NumericOrdering で近似。caseInsensitiveCompare は OrdinalIgnoreCase。LINQ の安定ソートなので同値の並びは Swift と違うことがある。
  - Swift でクラッシュする入力（PID 定義の重複、範囲の min>max）は C# ではクラッシュしない（エラー表示・false になる）。
  - SessionLogAnalysisModel は表示用の配列をバッチごとにコピーする（とても長いログで遅くなる可能性）。
- Data 層ができたら直すテスト：同期の往復テスト 1 件（testRoundTripBetweenIPhoneAndMacPreservesSharedArchiveMetadata）は GRDB が必要なので未移植。取得コントローラーの結合テストと生ログ準備のテストの一部は、テストファイル内のメモリ上の代用リポジトリで動かしている。Demo アダプターや OBD PID の初期データもテスト内に写した。→ Data 層の実装に差し替えて確かめ直す。
- Windows を「Mac 受領証」（ConnectionSessionMacReceipt）を出す端末として扱うか。出すと、iOS 側が生ログのキャッシュを消す判断に影響する。
- Sign in with Apple のリダイレクト先ドメインをどれにするか。そのドメインからカスタム URL スキームでアプリに戻す。
- git の user.name / user.email が未設定。最初のコミットは -c で作成者 Ryokugyoku を指定した。リモート（GitHub）にはまだ push していない。
- 移植で Swift と完全には同じにならない点（実害は小さい見込み）：計算式の中の漢数字（一 など）を Swift は数字として扱うが .NET は扱わない（エラーの種類が変わるだけ）。double の NaN 同士の比較が等しくなる。日時の精度が 100ns 単位。
- 対応する OBD アダプター：BLE の UART（Windows の BLE API で可能）と、Bluetooth Classic の SPP / COM ポート（OBDLink MX+ など）。iOS の ExternalAccessory（MFi）は Windows では使えない。
- 文字列の翻訳（Localizable.xcstrings、約520KB）を .resw に変換する方法。
- ライセンスが PolyForm Noncommercial なので、Windows 版も同じライセンスにするか。

## 決定事項
- 2026-11-10: トランザクション系の保存は、セッションごとの追記専用ジャーナル（1 レコード＝長さ＋CRC32、256 件ごとの疎な索引に sequence と日時の範囲、確定したセグメントは 64KB ブロックごとに Deflate 圧縮）にした。書込みは背景スレッド 1 本と上限付きキューで group commit（計測を止めない）、開くときに書きかけの末尾を切り詰める（クラッシュ耐性）。NuGet を増やさず、Domain のリポジトリのインターフェースの裏に隠したので、あとで LMDB などに替えられる。
- 2026-11-10: 計測中の書込みは fsync を待たない。落ちると直前の数 ms を失うことがあるが、残るのは必ず途中までの正しい列。fsync の方針は LogStoreFlushPolicy で 3 種類から選べる。
- 2026-11-10: 車両は SQLite が正。CloudKit の VehicleCatalog とは、前回同期した車両 ID の一覧を端末に残して 3 者で突き合わせる（両方にあれば更新日時の新しい方、同じならこの端末）。VehicleCatalog に削除の印がないため。同期の失敗は次の読み込みに回し、端末内の一覧を返す（iOS はキャッシュが空なら例外）。
- 2026-11-10: SQLite の日時は UTC の Ticks（100ns）で保存する（iOS の GRDB はミリ秒）。C# の日時を損なわずに往復させるため。取得境界は iOS と同じく µs に丸めて比べる。
- 2026-11-10: Bluetooth の識別子は、Classic も BLE もアドレス（00-04-3E-12-34-56 の形）。Windows には iOS のペリフェラル UUID に当たる値がないため。SPP 機器は Bluetooth 側にだけ出し、USB の一覧から Bluetooth の仮想 COM（BTHENUM）を除く（同じ機器を二重に出さないため）。
- 2026-11-10: Bluetooth Classic の候補は、MX+ に加えて、既知の BLE 名の接頭辞か名前に「OBD」「ELM327」を含む SPP 機器（macOS は MX+ だけ）。ペアリングしていない機器は列挙しない。
- 2026-11-10: 到来の監視は USB シリアル（DeviceWatcher）と BLE（アドバタイズ）。Classic は監視しない（iOS も BLE だけ）。アプリが動いている間だけ。接続の確認はトーストでなくアプリ内のイベント（InAppDefaultAdapterConnectionPrompt）で出す。Data.Windows を WindowsAppSDK に依存させないため。
- 2026-11-10: 設定がないとき（ローカルモード）は、LocalAccountAuthorizationAdapter が端末内で固定のアカウント ID を作る。CloudKit 系は取得が空・削除は何もしない。
- 2026-11-10: Web の Sign in with Apple では資格情報の状態を問い合わせる手段がないので、CredentialState は常に Authorized を返す。
- 2026-11-10: 取得性能のイベントは os_signpost の代わりに ETW（EventSource）に出す。
- 2026-11-10: 翻訳は scripts/ConvertXcstrings.cs（.NET 10 のファイル実行）で xcstrings から Strings/{ja-JP,en-US,es-ES}/Resources.resw を生成する。.resw の Name は英数字以外を _ にし、記号を含むキーには SHA-256 の先頭 8 桁を付ける（LocalizedText.ResourceName と同じ規則）。訳がない言語は日本語（元の言語）で補う。printf 形式（%@ など）は値に残し、アプリの LocalizedText.Format で埋める。
- 2026-11-10: カスタム URL スキームは revtorque-insight（戻り先は revtorque-insight://auth/apple と revtorque-insight://auth/cloudkit）。製品名と合わせ、iOS の識別子とぶつからないため。
- 2026-11-10: Data 層は 2 つに分ける。OS に依存しない部分は src/ProjectZD8.Data（net10.0、テストしやすい）、Windows の API が要る部分（シリアル、Bluetooth、資格情報、ブラウザー起動）は src/ProjectZD8.Data.Windows。
- 2026-11-10: Microsoft.Data.Sqlite が既定で引く SQLitePCLRaw.lib.e_sqlite3 2.1.11 に既知の脆弱性（GHSA-2m69-gcr7-jv3q）があるので、2.1.14 を明示して参照する。
- 2026-11-10: マニフェストから、テンプレートに入っていた systemAIModels を外した（使わないため）。
- 2026-11-10: Application 層の名前空間は ProjectZD8.Application.<機能>.<サブフォルダ>（フォルダは Features/ 付きで iOS 版と同じ）。Port は I 付きインターフェースで、ファイル名にも I を付ける。async メソッドに Async の接尾辞は付けない（Domain に合わせる）。State は INotifyPropertyChanged を実装した UI 非依存のクラス。
- 2026-11-10: 移植は Windows 版から始め、Domain → Application → Data → 画面の順に進める（開発者の指示）。
- 2026-11-10: 名前空間とプロジェクト名は iOS 版に合わせて ProjectZD8 にする。Domain は OS に依存しないクラスライブラリにして、JSON の属性は付けない。
- 2026-11-10: 全プロジェクトを .NET 10（LTS）に上げた（開発者の指示）。アプリは net10.0-windows10.0.19041.0、Domain とテストは net10.0。
- 2026-11-10: iCloud KV ストアの代わりに、iOS 版も設定とログアウト通知を CloudKit に書くよう直す（Windows から同期するため）。
- 2026-11-10: Windows 版は iOS 版とデータを同期する（開発者の希望）。CloudKit Web Services と Sign in with Apple の Web 版を使う。
