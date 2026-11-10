# ProjectZD8ForWindows 進捗

最終更新: 2026-11-10
README 確認済みのコミット: ae15a35（＋未コミットの画面の作り直しを反映済み）

## 概要
- iOS/macOS アプリ「RevTorque Insight」（GitHub: Ryokugyoku/ProjectZD8、Swift/SwiftUI）を Windows（WinUI 3 / .NET 10）向けに作り直す。iOS 版と CloudKit で同期する。

## 現状
- iOS 版と同じ機能のアプリとして一通りできた。ローカルモード（アカウントなし）とデモ用アダプターで、全画面を実際に操作して確かめた（下の「確かめたこと・確かめていないこと」）。
- **Sign in with Apple と iCloud 同期は「近日対応（Coming Soon）」**（2026-11-10、開発者の指示）。機能フラグ `AppleSignIn:Enabled`（既定 false）で止めている。ログイン画面の「Apple アカウントで続ける」と設定画面の「iCloud にサインイン」は押せないボタンに「近日対応」の印。使える入口は「この PC で続ける」（ローカルモード）だけ。Services ID が出たら、docs/CLOUD_SETUP.md の「0. 有効にする手順（チェックリスト）」のとおりフラグを true にするだけで動く（コードとテストは残してある）。
- 層：Domain（`src/ProjectZD8.Domain`）、Application（`src/ProjectZD8.Application`）、Data（`src/ProjectZD8.Data` と、Windows の API を使う `src/ProjectZD8.Data.Windows`）、画面と組み立て（ルートのアプリ。`App/`、`Platform/Windows/`）。
- テスト：Domain 56 件、Application 155 件、Data 311 件、計 522 件すべて合格。全プロジェクトのビルド（`ProjectZD8ForWindows.slnx`、x64）は警告 0・エラー 0。
- 保存：マスタ系は SQLite（`<データ>\projectzd8.sqlite`）、生ログと取得の証跡は自作の追記専用ログストア（`<データ>\logs\<セッション>\`）。iOS 版は全部を GRDB（SQLite）に入れているので、ここが違う（README にも書いた）。データのフォルダはパッケージの LocalState。
- 生ログストアの性能（Release、24 コア、1 セッションに 100 万件、`tools/ProjectZD8.LogStoreBenchmark`）：書込み 124 万件/秒、Append の遅延 p50 0.6µs・p99 2.0µs・p99.9 20.7µs（最大 14.1ms）。sequence の範囲読取（1,000 件）p50 0.66ms・p99 1.06ms、時間窓（10 秒分）p50 0.65ms・p99 1.09ms、全件読取 377ms。ディスク 26.5MiB（27.8 バイト/件）。開き直し（索引あり）1.2ms、異常終了後の開き直し（全走査）204ms。
- OBD：USB シリアル（COM、115200bps）、Bluetooth Classic（RFCOMM / SPP）、BLE（GATT の UART）、デモ用アダプター。機器の検出、既定アダプターの記憶、BLE の既定アダプターの到来を監視してシェルの InfoBar で接続を尋ねる処理（iOS と同じユースケース）。
- 同期と認証：Swift の Codable と同じ JSON、CloudKit Web Services のクライアントと各リポジトリ（車両・整備・接続セッション・AccountSettings・AccountSessionRevocation）、Web 版の Sign in with Apple（ID トークンの検証と sub の取り出し）、CloudKit の Web 認証（設定画面の「iCloud 同期」カード）、PasswordVault（だめなら DPAPI）への保存。機能フラグが有効で設定（`appsettings.Local.json` か環境変数 `PROJECTZD8_*`）もそろったときだけ使う。それ以外はローカルモード。
- 翻訳：iOS 版の xcstrings（904 キー）と Windows 版だけの文言（`Strings/WindowsOnly.xcstrings`、30 キー）から、日本語・英語・スペイン語の .resw を生成して取り込んだ。
- git は C:\Program Files\Git にあるが PATH に入っていない（Git Bash の git は使える）。GitHub CLI（gh）はない。.NET SDK 10.0.403 あり。iOS 版を参照用に ..\ProjectZD8 に clone 済み（8bcda6f）。

## 確かめたこと・確かめていないこと
- 確かめた（2026-11-10、x64 Debug をパッケージ登録して起動。スクリーンショットは共有メモのフォルダの screenshots/final/）：ローカルモードのログイン → 免責事項 → ホーム → 設定でアダプターを探索（実機なし。デモの USB 3 件だけが出て、Windows の探索はエラーなし）→ デモ USB を既定に → 接続 → VIN の識別と車両の登録（SQLite）→ ライブ PID（87 件）→ 生ログがログストアに書かれることをファイルで確認 → 切断 → 履歴・セッション詳細（2,407 件、正常終了）・分析（折れ線）→ 整備記録の作成 → 終了（索引ファイルが書かれる）と再起動（サインインの復元、履歴が残る）→ PID 収集設定 → 英語への切替 → アカウント削除（ログと既定アダプターが消え、ログイン画面に戻る）。2 つ目の起動（revtorque-insight:// のプロトコル起動）は起動中のインスタンスへ転送された。
- 確かめていない：実機の OBD アダプター（USB シリアル・RFCOMM・BLE の接続と送受信、探索、到来の監視）、実際の CloudKit と Apple のサインイン（テストは偽の HTTP 応答だけ）、本物の車、電源断での fsync の効き目、ARM64 と x86 のビルド、狭いウインドウ（640×420）、写真の取り込み、停止確認・Raw の解放・セッション削除の各ダイアログ、Release ビルド（トリミングあり）での起動。

## 直近の作業
- 2026-11-10: 画面を Windows 向けに作り直した（開発者の依頼「ボタンが若干不便」）。原因：多くの画面がモデルの変化（ライブ取得・ログ読込み・アダプター探索で 1 秒に何度も）のたびにボタンを作り直し、押してから離すまでにボタンが消えてクリックが取りこぼされていた。対策：RenderGate（押している最中・メニューやドロップダウンを開いている間は作り直しを待たせる）と、描く内容の鍵が変わったときだけ作り直す方式を全画面に入れた。あわせて、ナビを 1 行・設定を下に、全画面共通の接続バー（接続・切断・ライブ表示）、戻る（ボタン・Alt+←・マウスの戻る）、履歴のパンくずと広い幅での一覧・詳細の 2 列、右クリックメニュー（セッション・整備記録・車両）、整備の「新しい記録」メニューと ListView、Ctrl+S・F5、設定を Windows 11 の設定アプリ風の行に。不具合も直した：車両の編集で写真を選ぶと入力が消えていた、PID 収集設定でスイッチを切り替えるたびに一覧が先頭に戻っていた。整備記録の削除に確認ダイアログを足した（モデルは確認なしで消すため）。ビルド警告 0、テスト 522 件合格、起動して応答することまで確認（画面の目視は未）。
- 2026-11-10: Sign in with Apple と iCloud 同期を「近日対応（Coming Soon）」にした（開発者の指示）。機能フラグ AppleSignIn:Enabled（既定 false）を足し、ログイン画面と設定画面に押せないボタンと「近日対応」の印を出した。有効にしたときに、ローカルモードのデータを最初の Apple アカウントへ引き継ぐ処理（SqliteAccountDataReassignment と LocalAccountDataAdoptingAuthorizationPort）を作った。切り替えと引き継ぎのテスト 28 件を追加。docs/CLOUD_SETUP.md に有効にする手順のチェックリストを書いた。起動してログイン画面と設定画面（日本語・英語）を目で確かめた（screenshots/coming-soon/）。
- 2026-11-10: Data 層・画面・組み立て・翻訳・マニフェストを作り、アプリを完成させた。サブエージェント 4 つ（永続化、デバイス、ネットワークと認証、画面）を別の作業ツリーで並行させ、リードがまとめて組み立てを本物の Data 層に差し替えた。ローカルモードとデモ用アダプターで全画面を操作して確かめた。
- 2026-11-10: Application 層とそのテストを C# に移植した（9 機能、テスト 155 件合格）。
- 2026-11-10: 全プロジェクトを .NET 10 に上げた。Domain 層とそのテストを C# に移植した（56 件合格）。移植の規約を docs/PORTING_CONVENTIONS.md に書いた。
- 2026-11-10: iOS 版の CloudKit と認証の実装を調べ、Windows から同期する方式を設計した（下の「同期の設計メモ」）。

## 次にやること
- 画面の作り直し（2026-11-10）を目で確かめる：接続バーの接続・切断、履歴の 2 列（幅 1100 以上）とパンくず・戻る、右クリックメニュー、整備の「新しい記録」と ↑↓ の選択、設定の行、ライブ取得中に分類ボタンを連打してもクリックが届くこと、暗い外観・英語・狭い幅（640）。
- Services ID が発行されたら、docs/CLOUD_SETUP.md の「0. 有効にする手順（チェックリスト）」を順に進め、最後に AppleSignIn:Enabled を true にする。
- 開発者の準備（docs/CLOUD_SETUP.md）：Apple Developer で Services ID とリダイレクト先の https ドメインを用意し、docs/web/ のページを置く。CloudKit Console で API トークン（Sign In Callback）を作り、AccountSettings と AccountSessionRevocation のスキーマを Production に反映する。appsettings.Local.json を置く。
- 実際の Apple・CloudKit につないで確かめる（下の検討事項の「CloudKit Web Services の確かめていない点」と「sub の一致」）。
- 実機の OBD アダプター（OBDLink EX の USB、OBDLink MX+ の Bluetooth、BLE の ELM327）で接続・識別・ライブ取得を確かめる。
- iOS 版の設定とログアウト通知を CloudKit 化する（ProjectZD8 リポジトリ側の作業。下の設計案）。それまで設定とログアウト通知は iOS と同期しない。
- 画面の細部：ローカルモードでも Garage の副題（「iPhone と同期します」）と整備の最終同期時刻（雲の記号つき）がクラウドに保存したように見える。ローカルモードでは出し分ける。
- Application.Tests の中のメモリ上の代用リポジトリと PID 定義の写しを、Data の実装に置き換える（Application.Tests から Data を参照する）。未移植の往復テスト（testRoundTripBetweenIPhoneAndMacPreservesSharedArchiveMetadata）を移す。
- ARM64 と x86 のビルド、Release（トリミングあり）での起動を確かめる。

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
- 画面の作り直しで、iOS 版と違う所：ホームの補足（ナビの 2 行目）はツールチップへ、詳細・解析の「戻る」ボタンはパンくずとシェルの戻るへ、設定の言語・外観はボタンの並びからドロップダウンへ、整備記録の削除は確認ダイアログを挟む。Windows の慣習に合わせたが、iOS と揃えたい所があるか → 開発者に確認。
- 車両の編集で写真を選んだとき、iOS 版は下書きを置き換える（入力が消える）実装に見える。Windows 版は入力を残すようにした。iOS 側も直すか → 開発者に確認。
- ローカルモードのデータの引き継ぎの注意点：①確認なしで自動で引き継ぐ（決定事項を参照）。②付け替え先に同じ ID の車両があれば上書きしない（元の持ち主に残る）。③引き継いだ走行ログが iCloud に上がるのは、自動アップロードの設定が有効なときだけ（無効なら履歴に手動アップロードのボタンが出る）。
- 機能フラグを有効にしたあと、ローカルモードに戻す手段はない（フラグを false に戻すとローカルモードの識別子で入り直すが、引き継ぎ済みのデータは Apple のアカウントの持ち物のまま見えなくなる）。戻す運用が要るか → 開発者に確認。
- アカウント削除で AccountSessionRevocation のレコードを消すかどうか。今の実装は消さない（ほかの端末へログアウトを知らせるのに要るため）。「iOS 版の CloudKit 化の設計案」の「消す」と食い違っている。→ 開発者に確認。
- アカウント削除で、端末内の整備記録は消えない（iOS 版の ProjectZD8AccountDataEraser も整備記録を消さないので、同じにした）。ローカルモードで削除して、もう一度「この PC で続ける」と、前の整備記録が見える。iOS 側も含めて消すべきか → 開発者に確認。
- 取得元の端末：Domain の ConnectionSessionAcquisitionPlatform に Windows がない。macOS と名乗ると事実と違うので、セッションの acquisitionDevice は記録しない（履歴に「取得端末不明」と出る）。取得証拠の platform は macOS の値を使っている。Domain に Windows を足すと iOS 版との JSON 互換に影響するので、iOS 側と一緒に決める。
- CloudKit Web Services の確かめていない点：新しい ckWebAuthToken が応答のどこに入るか（JSON の ckWebAuthToken / ckSession と同名のヘッダーを見ている）、CloudKit Console の Sign In Callback にカスタム URL スキームを直接入れられるか（入れられなければ https のページ経由。docs/web/cloudkit-callback.html）、update / forceReplace の実際の動き、asset の receipt の形。
- Web の Sign in with Apple で得た sub が iOS の credential.user と一致するか（同じ Team・同じ主 App ID なら一致するはず）。実際につないで確かめる。
- Sign in with Apple のリダイレクト先ドメインをどれにするか（開発者が用意する）。
- Swift の sortedKeys の並び順と、iOS が実際に書いた JSON とのバイト単位の比較はしていない（読み込みは順番に依存しない）。
- 生ログストアと SQLite にまたがる操作（セッション終了・回復・生ログの置換）は、SQLite のトランザクション中に LogStore を fsync してから SQLite を確定する。fsync のあとに SQLite の確定だけが失敗すると不整合が残る（まれ）。
- 生ログの除去と復元で、Windows は取得証拠を残したまま生ログだけを消す。iOS は Raw を参照する PID 要求があると外部キーで失敗するので、iOS 側の不具合かもしれない。→ iOS 側で確認。
- 計測中のセッションは batch の証拠をすべてメモリに持つ。2 時間を超える長いセッションのメモリ量は未測定。
- 実機で見る必要がある点（デバイス）：FTDI ドライバーが COM ポートのインターフェイスを登録するか、BusReportedDeviceDesc と Manufacturer が値を返すか、GetGattServicesAsync(Uncached) で接続が始まるか、SPP をキャッシュから取れない機器での 10 秒の照会。
- 画面の確かめていない点：免責事項ダイアログを出すと左下に白い弧が描かれる（見た目だけ。原因不明）。画面担当が直す前、設定画面への移動直後にアダプター選択を開くと XAML の内部で落ちる（0xc000027b）ことがあった。描き直しを減らして再現しなくなったが、根本原因は不明。
- 言語を切り替えても、WinUI 標準の文言（「ナビゲーションを閉じる」など）は OS の言語のまま。
- 時間の単位（h・m・s）とバイト数（1 KB = 1,000 バイト）は自前の書式で、Apple の書式と少し違う。
- iOS のテストで移植しなかったもの：旧 DB（v8）からの移行（Windows に旧 DB はない）、日時が無限大のテスト（C# にない）、取得性能の計測マトリクス（ベンチマークで代用）。
- Application 移植で Swift と完全には同じにならない点：
  - Swift の `Task {}` は後で走るが、C# の async は最初の await まで同期で走る。状態遷移の順番は同じにしたが、同じ同期区間で続けて操作を送ると（例：VehicleSaved の直後に EditCancelled）見える状態が変わることがある。LiveTelemetryModel だけは Task.Yield で Swift に近づけた。モデルは UI スレッド（単一スレッド）から呼ぶ前提。
  - Swift の actor 3 つ（取得コントローラー、マニフェスト作成、バッチ保存）は lock 付きのクラスにした。ポートに CancellationToken がないので、実行中の呼び出しは止められず、世代番号の確認で古い結果を捨てる（Swift と同じ結果）。
  - 並べ替え：localizedStandardCompare は現在のカルチャ＋IgnoreCase＋NumericOrdering で近似。LINQ の安定ソートなので同値の並びは Swift と違うことがある。
  - Swift でクラッシュする入力（PID 定義の重複、範囲の min>max）は C# ではクラッシュしない（エラー表示・false になる）。
  - SessionLogAnalysisModel は表示用の配列をバッチごとにコピーする（とても長いログで遅くなる可能性）。
  - 計算式の中の漢数字（一 など）を Swift は数字として扱うが .NET は扱わない。double の NaN 同士の比較が等しくなる。日時の精度が 100ns 単位。
- リモート（GitHub）にはまだ push していない。git の user.name / user.email は未設定（コミットは -c で Ryokugyoku を指定）。
- ライセンスが PolyForm Noncommercial なので、Windows 版も同じライセンスにするか。

## 決定事項
- 2026-11-10: 画面は Windows の慣習を優先してよい（開発者の指示「機能を保てばレイアウトは大きく変えてよい」）。モデルが頻繁に変わる画面では、押している最中に作り直さない（RenderGate）ことを全画面の決まりにする。
- 2026-11-10: ローカルモードのデータは、この PC で最初に Apple でサインインしたアカウントへ、確認のダイアログなしで自動で引き継ぐ（開発者の判断）。Windows のユーザーごとに 1 人が使う前提。家族で 1 台の PC を共有すると、先にサインインした人のアカウントに入る。
- 2026-11-10: Sign in with Apple と iCloud 同期は「近日対応（Coming Soon）」にする（開発者の指示）。切り替えは 1 か所の機能フラグ `AppleSignIn:Enabled`（appsettings.Local.json。環境変数 PROJECTZD8_APPLE_SIGN_IN_ENABLED が優先）。既定は無効で、Services ID などがそろっていてもフラグが true でなければ使わない。フラグを当てはめた設定（CloudServiceConfiguration.ForActiveFeatures）だけを組み立てに渡すので、無効の間は Apple にも CloudKit にも通信しない。フラグが true でも設定が足りなければ Coming Soon のまま（Debug の出力に理由を出す）。
- 2026-11-10: iCloud 同期も Apple のサインインと同じフラグに従う。CloudKit のレコード名は Apple の sub から作るので、Apple のサインインなしでは同期できないため。
- 2026-11-10: Coming Soon の間も、ログイン画面の「Apple アカウントで続ける」と設定画面の「iCloud にサインイン」は消さず、押せないボタンに「近日対応」の印を付けて出す。近く使えるようになることと、今はこの PC の中だけに保存することを利用者に伝えるため。
- 2026-11-10: ローカルモードのデータは、フラグを有効にしたあと、この PC で最初に Apple でサインインしたアカウントへ 1 回だけ自動で引き継ぐ（LocalAccountDataAdoptingAuthorizationPort）。SQLite の車両・写真・接続セッション・整備記録の accountIdentifier を 1 回のトランザクションで付け替え（生ログと取得証跡はセッションの ID で引くので一緒に移る）、設定は引き継ぎ先になければ写す。引き継いだあとは既存の同期で iCloud に上がる。理由：データの ID は UUID なので付け替えだけで済み、CloudKit の車両カタログの 3 者の突き合わせでも「この端末だけにある車両」として上がる。2 つ目の Apple アカウントには引き継がない（データの持ち主を 1 人に決めるため）。失敗してもサインインは成功させ、次のサインインでやり直す。
- 2026-11-10: Windows は接続セッションの同期で macOS の役割（ConnectionSessionSyncDeviceRole.MacOS。生ログを CloudKit に保管し、Mac 受領証を出す）を担う。Application には iOS と macOS の 2 つの役割しかなく、デスクトップで全ログを持つ Windows は Mac と同じ立場のため。iOS 側の生ログのキャッシュを消す判断に影響する点は、iOS と同期するときに見る。
- 2026-11-10: 設定がないとき、ログイン画面のボタンは「この PC で続ける」にし、「Apple アカウントのみ」の説明を隠し、ローカルモードの説明を出す。免責事項の同意ボタンも「同意して続ける」にする（実際の動きと文言を合わせるため）。Windows 版だけの文言は Strings/WindowsOnly.xcstrings に置く（iOS 版の xcstrings を変えないため）。
- 2026-11-10: iCloud（CloudKit）へのサインインは、Apple のサインインとは別に、設定画面の「iCloud 同期」カードから行う。CloudKit の Web 認証は Apple ID の入力を伴うので、利用者が自分で始める操作にした。
- 2026-11-10: 既定アダプターの到来の確認は、iOS のローカル通知の代わりにシェルの InfoBar で出す（アプリが動いている間だけ監視するため、トーストは使わない）。
- 2026-11-10: 対応していない表示言語のときは英語で出す（csproj の DefaultLanguage=en-US）。iOS 版の元の言語は日本語だが、海外の利用者に読める言語を優先した。
- 2026-11-10: 開発者の設定ファイル appsettings.Local.json は、データのフォルダ（LocalState）、実行フォルダの順に探す。リポジトリのルートに置くとビルドで実行フォルダへコピーする（コミットはしない）。
- 2026-11-10: 組み立てで Swift のプロトコル合成に当たる型として、Data に IConnectionSessionStorage と IVehiclePIDCapabilityStorage を置いた（SQLite 実装と使えないときの実装が両方実装する。メンバーは足さない）。
- 2026-11-10: 組み立ては DI コンテナを使わず、iOS 版と同じ静的な組み立て（App/Composition/WindowsApplicationComposition）にした。依存の作り方を iOS 版と 1 対 1 で見比べられるようにするため。
- 2026-11-10: App/ フォルダの名前空間は ProjectZD8ForWindows.AppRoot.*（ProjectZD8ForWindows.App は App クラスとぶつかるため）。
- 2026-11-10: グラフはチャート用の NuGet を使わず、XAML の Polyline と Path で描く（点が多いと間引く）。
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
