# ProjectZD8ForWindows 進捗

最終更新: 2026-11-12
README 確認済みのコミット: 9384e06（画面刷新の一連のマージまで。「画面と操作」の節を新しい画面に合わせた）

## 概要
- iOS/macOS アプリ「RevTorque Insight」（GitHub: Ryokugyoku/ProjectZD8、Swift/SwiftUI）を Windows（WinUI 3 / .NET 10）向けに作り直す。iOS 版と CloudKit で同期する。

## 現状
- iOS 版と同じ機能のアプリとして一通りできた。ローカルモード（アカウントなし）とデモ用アダプターで、全画面を実際に操作して確かめた（下の「確かめたこと・確かめていないこと」）。
- **Sign in with Apple と iCloud 同期は「近日対応（Coming Soon）」**（2026-11-10、開発者の指示）。機能フラグ `AppleSignIn:Enabled`（既定 false）で止めている。ログイン画面の「Apple アカウントで続ける」と設定画面の「iCloud にサインイン」は押せないボタンに「近日対応」の印。使える入口は「この PC で続ける」（ローカルモード）だけ。Services ID が出たら、docs/CLOUD_SETUP.md の「0. 有効にする手順（チェックリスト）」のとおりフラグを true にするだけで動く（コードとテストは残してある）。
- 層：Domain（`src/ProjectZD8.Domain`）、Application（`src/ProjectZD8.Application`）、Data（`src/ProjectZD8.Data` と、Windows の API を使う `src/ProjectZD8.Data.Windows`）、画面と組み立て（ルートのアプリ。`App/`、`Platform/Windows/`）。
- テスト：Domain 56 件、Application 172 件、Data 330 件、Diagnostics 71 件、計 629 件すべて合格。全プロジェクトのビルド（`ProjectZD8ForWindows.slnx`、x64）は警告 0・エラー 0。
- 保存：マスタ系は SQLite（`<データ>\projectzd8.sqlite`）、生ログと取得の証跡は自作の追記専用ログストア（`<データ>\logs\<セッション>\`）。iOS 版は全部を GRDB（SQLite）に入れているので、ここが違う（README にも書いた）。データのフォルダはパッケージの LocalState。
- 生ログストアの性能（Release、24 コア、1 セッションに 100 万件、`tools/ProjectZD8.LogStoreBenchmark`）：書込み 124 万件/秒、Append の遅延 p50 0.6µs・p99 2.0µs・p99.9 20.7µs（最大 14.1ms）。sequence の範囲読取（1,000 件）p50 0.66ms・p99 1.06ms、時間窓（10 秒分）p50 0.65ms・p99 1.09ms、全件読取 377ms。ディスク 26.5MiB（27.8 バイト/件）。開き直し（索引あり）1.2ms、異常終了後の開き直し（全走査）204ms。
- OBD：USB シリアル（COM、115200bps）、Bluetooth Classic（RFCOMM / SPP）、BLE（GATT の UART）、デモ用アダプター。機器の検出、既定アダプターの記憶、BLE の既定アダプターの到来を監視してシェルの InfoBar で接続を尋ねる処理（iOS と同じユースケース）。
- 同期と認証：Swift の Codable と同じ JSON、CloudKit Web Services のクライアントと各リポジトリ（車両・整備・接続セッション・AccountSettings・AccountSessionRevocation）、Web 版の Sign in with Apple（ID トークンの検証と sub の取り出し）、CloudKit の Web 認証（設定画面の「iCloud 同期」カード）、PasswordVault（だめなら DPAPI）への保存。機能フラグが有効で設定（`appsettings.Local.json` か環境変数 `PROJECTZD8_*`）もそろったときだけ使う。それ以外はローカルモード。
- 翻訳：iOS 版の xcstrings（904 キー）と Windows 版だけの文言（`Strings/WindowsOnly.xcstrings`、30 キー）から、日本語・英語・スペイン語の .resw を生成して取り込んだ。
- git は C:\Program Files\Git にあるが PATH に入っていない（Git Bash の git は使える）。GitHub CLI（gh）はない。.NET SDK 10.0.403 あり。iOS 版を参照用に ..\ProjectZD8 に clone 済み（8bcda6f）。

## 確かめたこと・確かめていないこと
- 確かめた（2026-11-10、x64 Debug をパッケージ登録して起動。スクリーンショットは共有メモのフォルダの screenshots/final/）：ローカルモードのログイン → 免責事項 → ホーム → 設定でアダプターを探索（実機なし。デモの USB 3 件だけが出て、Windows の探索はエラーなし）→ デモ USB を既定に → 接続 → VIN の識別と車両の登録（SQLite）→ ライブ PID（87 件）→ 生ログがログストアに書かれることをファイルで確認 → 切断 → 履歴・セッション詳細（2,407 件、正常終了）・分析（折れ線）→ 整備記録の作成 → 終了（索引ファイルが書かれる）と再起動（サインインの復元、履歴が残る）→ PID 収集設定 → 英語への切替 → アカウント削除（ログと既定アダプターが消え、ログイン画面に戻る）。2 つ目の起動（revtorque-insight:// のプロトコル起動）は起動中のインスタンスへ転送された。
- 確かめた（2026-11-11、開発者の報告）：main の Debug ビルドで実機の OBD アダプターを車につなぎ、探索 → 接続 → VIN の識別と車両の登録 → ライブ PID の表示 → 走行ログの記録と履歴・分析 → 切断・再接続まで、すべて動いた。アダプターの種類（USB・Bluetooth Classic・BLE）と車種はまだ聞いていない。main には診断ログの残り（②〜⑤）が入っていないので、obd.connect / obd.wire のログは確かめていない。
- 確かめていない：実機の OBD アダプターのうち上で確かめた以外の接続方式と、探索・到来の監視、Release（Store 版）ビルドでの実機接続、2 台目以降の車種、実際の CloudKit と Apple のサインイン（テストは偽の HTTP 応答だけ）、電源断での fsync の効き目、ARM64 と x86 のビルド、狭いウインドウ（640×420）、写真の取り込み、停止確認・Raw の解放・セッション削除の各ダイアログ、Release ビルド（トリミングあり）での起動。

## 直近の作業
- 2026-11-12: Partner Center の「In the Microsoft Store. Update in progress」の原因を確かめた（開発者の質問）。提出 2（11/11 に作成、**下書きのまま**、未提出）が残っているため。中身は全項目「Unchanged」、パッケージも 1.0.0.0 のままで、何も変えていない。審査待ちではないので待っても消えない。公開中の提出 1（1.0.0.0）には影響なし。開発者の指示で提出 2 を削除し、概要ページが「In Microsoft Store」と「Start update」だけに戻ったことを確かめた（アプリ一覧の表示は未確認）。掲載画像は古いまま。
- 2026-11-12: ライブ取得中のリサイズの重さを直した（別セッション・Opus 5.5、21d8cd3。リードが main にローカルでマージ＝db95ae8、push なし。マージ後の main で警告 0・629 件合格をリードが確認）。原因はライブのタイルではなく、シェルが幅クラスの境目をまたぐたびにナビの形（ペインの形・幅・下タブ）を切り替えていたこと（1 回 7〜12 ms）。ウインドウの縁をドラッグしている間は形を保ち、止まって 150 ms 後に 1 回だけ切り替える（最大化・スナップ・起動時はすぐ）。接続コンソールもライブの更新ごとにツールチップなどを設定し直していたのを、変わったときだけにした。計測（刷新前 a411ec9 / 直す前の main / 今回、UI スレッドの CPU）：ライブ中のリサイズ 34.1 / 37.5 / 34.2%、ホームのリサイズ 28.1 / 30.2 / 28.1%。未達・未確認：最悪の 1 ステップ（PC が混んでいて 3 つとも 80〜220 ms で比べられず）、ワーキングセット +1〜2 MB（原因不明）、プロファイラーなし（タイマーと A/B で切り分け）。
- 2026-11-12: スクリーンショット 6 枚（docs/store/screenshots/01〜06、1904×1072、日本語・暗い外観・デモ USB）を新しい画面で撮り直した（別セッション・Sonnet 5.5、0b93617。リードが main にローカルでマージ＝9384e06、push なし。警告 0・629 件合格をリードが確認、01-live-data.png はリードも目で見た）。スペイン語の切れ 2 件を直した（ガレージの車両カードのボタンを折り返しに、整備の絞り込みタブを横スクロールに。幅 1280 で「Mantenimie…」の見切れは残る）。英語・スペイン語でホーム・ライブ・履歴・整備・ガレージ・設定を幅 640 / 1280 で確認、日本語で不具合レポートの zip の保存を確認（セッションの報告）。未確認：英・西の履歴の詳細・分析・車両編集、ハイコントラスト。**Partner Center の掲載画像は古いまま**（6 枚 × 3 言語の差し替えは開発者の作業）。開発版パッケージは撮影時は ui-shots の作業ツリーを指していた（その後 main の bin に登録し直した）。アプリのデータは作業前と一致（ハッシュで確認との報告）。これで画面の刷新は一通り完了。
- 2026-11-11: 画面の刷新を始めた（開発者の依頼：レスポンシブ対応、レイアウトの根本的な最適化、モダンで「実用的より先進的」な見た目、軽量化。細かい作業は別セッションに任せ、リードのトークンを抑える）。土台（幅クラスの仕組み・トークン・共通部品・シェル・docs/UI_GUIDELINES.md・見本としてホーム画面）を別セッション（Opus 5.5）に依頼し、枝 claude/ui-foundation-798e8a（3472c00）にできた（**main には未反映**、push なし。警告 0、629 件合格。シェルとホームを幅 640 / 900 / 1280 / 1920・明暗で目視済み、とセッションが報告）。幅クラスは Compact < 760、Medium 760〜1199、Expanded 1200〜1599、Wide 1600 以上。ナビは Compact がタイトルバーの ≡ から重ねて開く、Medium が記号だけのレール、Expanded / Wide が題つきのペイン。接続バーはタイトルバーの接続コンソールに置き換え。中身の最大幅 1360、本文 720。計測（x64 Debug、6 回）：起動 約 1,650 → 1,700 ms、ワーキングセット 約 188 → 191 MB（土台だけでは軽くならない。各画面を「値だけ差し替え」に移してから測り直す）。確かめていない：ハイコントラストの見た目、英語・スペイン語、KeyedChildren と VirtualList の実動作。続けて、この枝から画面ごとの 4 セッション（Sonnet 5.5。ui-live＝ライブ表示とアダプター選択、ui-history＝履歴・詳細・分析、ui-garage＝ガレージ・車両編集・PID 設定・整備、ui-settings＝設定・ログイン・アカウント削除）を別々の作業ツリーで並行で出し、各 1 コミットで終わった：claude/ui-live-806f62（e06feaa）、claude/ui-history-b2a8ea（aa777e1）、claude/ui-garage-cd538f（21dc34b）、claude/ui-settings-80105b（23c0f8b）。Compact のナビを下のタブ帯に替える枝 claude/ui-nav-052122（0e29c3e、実アプリで確認済みと報告）もできた。5 つの枝のファイルに重なりがないことをリードが確認。画面の 4 つは目視なし（ビルドとテストだけ）。続けて、統合・目視・計測・手直し・README とガイドの更新を 1 セッション（Opus 5.5、ui-integrate）に依頼し、枝 claude/ui-integrate-784775（6df2d5b）に 5 本のマージ＋実アプリで見つけた 15 件の直し＋README「画面と操作」・UI_GUIDELINES・USER_GUIDE の文章の更新が入った（警告 0、629 件合格。**main には未反映**）。全画面を幅 640 / 900 / 1280 / 1920・明暗、主な画面を英・西で確認済みとの報告。計測（x64 Debug、main → 統合）：起動 約 1,400 → 1,445 ms、ワーキングセット 約 190 → 193.5 MB、ライブ中 CPU は同程度（大半は取得側）、リサイズ 1 ステップ最大 15–19 → 28–54 ms・CPU 34–40 → 44–46%。**軽量化は未達**なので、同じセッションに切り分けと直し（カードのグラデーション境界線・発光の削減、遅延生成）、残りの問題（設定の目次の固定、整備のページ見出し、履歴の高さ 420）、ログイン → 免責事項 → 最初の車両登録の目視を追加で依頼し、e180f54 で終わった。**リードが main にローカルでマージした（5519665、push なし。マージ後の main で警告 0・629 件合格をリードが確認）**。切り分けで分かった主因：シェルがリサイズのたびと起動直後に、タイトルバーの押せる領域を Windows に登録し直していた（落ち着いてから 1 回に直した）。主役カードのグラデーションの面は単色にした（発光の境界線は残す）。最終の計測（x64 Debug、main → 刷新後）：起動 1,388 → 1,372 ms、ワーキングセット 189.3 → 192.0 MB、ホームのリサイズ中 CPU 35.2 → 33.2%、ライブ 60 秒の CPU 2.7 / 2.1 → 2.7 / 2.3%、**ライブ取得中のリサイズは CPU 31–37 → 44%・最悪の 1 ステップ 14–19 → 39–54 ms で main より重いまま**（原因は未特定。ライブのタイルが重い推測）。追加で直した：設定の目次の固定、整備のページ見出し、高さ 420 での整備・履歴の見出しの折りたたみ、車両登録画面の見出しとボタン高さ。目視済み（セッションの報告）：ログイン → 免責事項 → アダプター設定 → 接続 → 車両登録 → 編集 → ガレージ（幅 640 / 1280、明・暗）。未確認：ハイコントラスト、不具合レポートの保存、アカウント削除の実行、最後の直しのあとの英語・スペイン語の全画面。古くなった画像：docs/store/screenshots/01〜06。開発版パッケージは統合の作業ツリー（.claude/worktrees/ui-integrate-784775）の x64 Debug を指している。アプリのデータは作業前の状態に戻してある（テスト中のデータと退避の写しは統合セッションの scratchpad に残っている）。
- 2026-11-11: 取得の診断ログの直し 3 点ができた（別セッション・Sonnet、コミット 0180561。開発者の承認を得て main にマージ済み＝efd5a9d、push なし。マージ後の main で 629 件合格）。①`acq.started` / `acq.stopped` に `sid` を付ける。②動いている取得が無い停止は `acq.stop.nothing-running`（Debug）にする（原因：起動時に認証が SignedOut になると WindowsApplicationFlow.OnAuthenticationChanged が StopRequested を送る。呼び出し元は変えていない）。③正常なバッチは 10 個ごとに `acq.batch.summary` 1 行にまとめ、異常のあるバッチと世代の最初のバッチだけ `acq.batch.completed` を書く（間隔は AcquisitionLog.NormalBatchSummaryInterval）。見積もりで 1 時間 約 6.5 MB → 約 0.4 MB。リードがブランチで全テスト 629 件（Application 172）合格を確かめた。実アプリでは未確認。残る点：世代の切り替えが重なると `acq.stopped` に `sid` が付かないことがある。
- 2026-11-11: 開発者が Debug ビルドで実機の OBD アダプターにつなぎ、動くことを確かめた（初めての実機・実車確認、main の Debug）。探索から切断・再接続まで全部動いた。市場分析で挙げた弱み「実機での確認がない」が一部解消（残りは接続方式・車種の広さと Release ビルド）。アダプターの種類と車種は未記録。
- 2026-11-11: 診断ログを実アプリで確かめた（開発者の承認。x64 Release を `-p:DiagnosticsChannel=Beta`、続けて `Stable` でビルドしてパッケージ登録、UI オートメーションで操作）。**確かめた**：Beta でログイン → デモ USB を既定に → 接続 → 車両の登録 → 約 65 秒のライブ取得（87 PID）→ 切断で、JSONL 278 行・118 KB（取得中は約 1.8 KB/秒＝約 6.5 MB/時。ほぼ `acq.batch.completed` の Debug）。VIN の全桁とユーザー名はログに出ていない。設定の「診断」3 行の表示（日本語・暗い外観。Beta では詳細ログが無効表示）、合計サイズの表示、「書き出す」→ 保存ダイアログ → zip（jsonl 4 個と summary.json だけ）、scripts/Analyze-Diagnostics.ps1 で zip を読めること、強制終了 → 再起動で `app.previous-run.crashed` と InfoBar。Stable では 40 秒の接続・取得・切断・終了でログは 2 行（app.start と app.exit）だけ。Stable で詳細ログのスイッチを入れると期限（7 日後）が出て、`diag.level.changed` のあと Debug が書かれる（切ると戻る）。**確かめていない**：エラー表示の報告番号（エラーが起きなかった）、Stable で Error 時にリングバッファが吐かれること・未処理例外の crash-*.jsonl（どちらも単体テストだけ）、「診断ログを消す」のダイアログ、英語・スペイン語・明るい外観、7 日後の自動切り。
- 2026-11-11: 診断ログの 4 ブランチを、開発者の承認を得て main にマージした（409c806、560347b、c46de1d、c87a639。push はしていない）。マージ後の main で警告 0・テスト 623 件合格。docs/USER_GUIDE.html に設定の「診断」行、データとプライバシーの診断ログの説明、FAQ「不具合を報告するには」を足した（claude.ai の Artifact 版は古いまま）。docs/LOGGING_STRATEGY.md の状態を「実装済み」にし、方針と変わった点を書いた。
- 2026-11-11: 診断ログの残り 4 つの作業が各ブランチで終わった（**main にはまだ入れていない**、push なし）：②OBD と取得＝claude/diagnostics-obd-e015e8（f205b45、Opus）、③保存・認証・同期・モデル＝claude/diagnostics-store-sync-c8039f（0d9044b、Sonnet）、④設定画面・報告番号・異常終了の通知＝claude/diagnostics-ui-62f3a3（6de1940、Sonnet）、⑤release.yml・Analyze-Diagnostics.ps1・README・PRIVACY.md・STORE_RELEASE＝claude/diagnostics-release-docs-efe87f（5996aad、Haiku）。リードが一時の作業ツリーで 4 つを試しにマージして確かめた：衝突 0、ビルド警告 0、テスト 623 件合格（Domain 56、Application 166、Data 330、Diagnostics 71）、EventId の重複なし、`-c Release -p:DiagnosticsChannel=Beta` のビルド成功。確かめていない：画面（設定の「診断」3 行、報告番号、異常終了の InfoBar）の目視、実機アダプター、Data.Windows の Transport・探索のログ（テストなし）、Beta / Stable で起動したときのログの量、PowerShell 7 でのスクリプト、GitHub Actions の実行。
- 2026-11-11: 市場価値と競合の分析をした（開発者の質問。会話で回答、ファイルは作っていない）。iOS 側の査定（ProjectZD8/PRODUCT_VALUE_AND_APP_STORE_PRICING_ASSESSMENT.md、2026-07-20）に Windows 版を足して見直した。再構築コストは iOS＋Windows で約 2,000万〜4,000万円、ソフトウェア資産としては 400万〜900万円、売上ゼロの今の売却相場ではもっと低い（数十万〜数百万円）。いちばん大きい弱みは DTC（故障コード）の読取・消去と CSV 書き出しがないこと、実機・実車での確認がないこと。強みは iPhone と PC/Mac をつなぐ記録基盤、壊れにくい生ログストア、Windows 11 らしい画面、端末内だけの保存。
- 2026-11-11: 診断ログの土台ができた（別セッション・Opus、コミット b66318d。開発者の承認を得て main にマージ済み＝6e8834c、push はしていない。マージ後の main でも 575 件合格）。`src/ProjectZD8.Diagnostics` と `tests/ProjectZD8.Diagnostics.Tests`（53 件）。リードがブランチで全テストを走らせ、575 件（既存 522 ＋ 53）合格を確かめた。Debug 版で app.start / app.exit と、強制終了後の app.previous-run.crashed が書かれることはセッションが確認。確かめていない：Beta・Stable チャンネルでのビルド、実アプリでの未処理例外（どちらも単体テストだけ）。呼び出し側の待ち時間は有効な 1 行で平均約 1µs・p99 3.4µs、無効なレベルは割り当て 0。方針の文書と違う点：リングバッファから吐いた行に `"ring":true` が付く、app.start / app.exit / app.previous-run.crashed / diag.dropped はレベルに関係なく必ず書く、キューは 3/4 で Trace・Debug を捨て満杯で Info 以上も捨てる。
- 2026-11-11: **注意**：上の確認で登録した開発版パッケージ（Ryokugyoku.RevTorqueInsight、作業ツリーの bin を指す）が残っている。Store 版と競合するので、Store 版を入れる前に外す（`Get-AppxPackage Ryokugyoku.RevTorqueInsight | Remove-AppxPackage`）。
- 2026-11-11: 診断ログの戦略を立てた（開発者の依頼）。docs/LOGGING_STRATEGY.md。現状は Debug.WriteLine 9 か所と %TEMP% の unhandled.log だけで、Release では何も残らない。方針：JSON Lines のファイル（LocalState\diagnostics\、RDB なし）、Beta は Debug 以上を全部、Stable は Warning 以上＋直近 2,000 件のリングバッファを Error・クラッシュ時に吐き出す、自動送信なし・設定画面から不具合レポート（zip）を書き出す。実装は 5 つの作業に分け、新しいセッションで難しさ別のモデル（Opus / Sonnet / Haiku）に任せる。土台は実装済み（上の項目）。
- 2026-11-11: Store の審査結果を確認した。1.0.0.0 は合格し、プライベート対象ユーザー向けに公開済み（「In Microsoft Store」）。iCloud ボタンは指摘されなかった。
- 2026-11-11: お客様向けのご利用ガイド docs/USER_GUIDE.html を作った（開発者の依頼）。準備・はじめかた・アダプターのつなぎ方・各画面・ショートカット・データとプライバシー・FAQ。画像は docs/store/screenshots/ を相対パスで参照。claude.ai の Artifact（非公開、https://claude.ai/artifact/XnjyGvo4QDDjsDPSqzf5Xg）にも公開した。お客様に見せるには共有設定が要る。
- 2026-11-11: Microsoft Store に 1.0.0.0 を提出した（審査中）。Partner Center でプロパティ・年齢区分（3+）・パッケージ（x64、Desktop のみ）・3 言語の掲載情報・認定メモ・runFullTrust の理由・無料・プライベート対象ユーザー（既知のユーザー グループ「RevTorque Insight Beta Testers」、開発者の個人の Microsoft アカウント 1 件）を設定した。IARC の同意、パッケージとスクリーンショットのアップロード、アプリ開発者契約 8.11 への同意は開発者が行った。手元のビルドで .msixupload の中身が古い Identity のまま再利用され弾かれたので、出力を消して作り直した（docs/STORE_RELEASE.md に注意を追記）。
- 2026-11-10: Store の最初の提出の準備（開発者の依頼）。PRIVACY.md、docs/store/listing.md（日・英・西の掲載文、認定メモ、年齢区分の答え）、docs/store/screenshots/（UI オートメーションでデモ用アダプターを操作して撮影）を作って push した。CI が GitHub で成功。Partner Center で提出の下書きを作り、プロパティを入力・保存し、年齢区分のアンケートに答えた（同意と保存は開発者）。
- 2026-11-10: ロゴを作り、Partner Center で「RevTorque Insight」を予約した（開発者の依頼）。ロゴは iOS 版のアプリアイコンを元に scripts/GenerateAppIcons.cs で 72 ファイルを作った（タイル・ストア・スプラッシュ・ロック画面、タスクバーとスタート用の角丸）。パッケージに入ることとタスクバーの表示を確かめた。Identity を Partner Center の値にして、Store 用の .msixupload が作れることを確かめた。
- 2026-11-10: Microsoft Store のベータ公開の準備と自動リリースを作った（開発者の依頼）。GitHub Actions の CI（ビルドとテスト）と Release（タグで Store に提出）、scripts/Set-PackageIdentity.ps1、docs/STORE_RELEASE.md。Store 用の .msixupload を手元で作れることを確かめた（シンボルを付けると dotnet CLI では失敗するので外した）。トリミングした Release が起動直後に落ちたので、トリミングを止めて、起動とウインドウの応答を確かめた（画面の操作は未）。ワークフローは GitHub ではまだ動かしていない。
- 2026-11-10: LICENSE（PolyForm Noncommercial 1.0.0）を足し、GitHub に公開リポジトリとして上げた。
- 2026-11-10: 画面を Windows 向けに作り直した（開発者の依頼「ボタンが若干不便」）。原因：多くの画面がモデルの変化（ライブ取得・ログ読込み・アダプター探索で 1 秒に何度も）のたびにボタンを作り直し、押してから離すまでにボタンが消えてクリックが取りこぼされていた。対策：RenderGate（押している最中・メニューやドロップダウンを開いている間は作り直しを待たせる）と、描く内容の鍵が変わったときだけ作り直す方式を全画面に入れた。あわせて、ナビを 1 行・設定を下に、全画面共通の接続バー（接続・切断・ライブ表示）、戻る（ボタン・Alt+←・マウスの戻る）、履歴のパンくずと広い幅での一覧・詳細の 2 列、右クリックメニュー（セッション・整備記録・車両）、整備の「新しい記録」メニューと ListView、Ctrl+S・F5、設定を Windows 11 の設定アプリ風の行に。不具合も直した：車両の編集で写真を選ぶと入力が消えていた、PID 収集設定でスイッチを切り替えるたびに一覧が先頭に戻っていた。整備記録の削除に確認ダイアログを足した（モデルは確認なしで消すため）。ビルド警告 0、テスト 522 件合格、起動して応答することまで確認（画面の目視は未）。
- 2026-11-10: Sign in with Apple と iCloud 同期を「近日対応（Coming Soon）」にした（開発者の指示）。機能フラグ AppleSignIn:Enabled（既定 false）を足し、ログイン画面と設定画面に押せないボタンと「近日対応」の印を出した。有効にしたときに、ローカルモードのデータを最初の Apple アカウントへ引き継ぐ処理（SqliteAccountDataReassignment と LocalAccountDataAdoptingAuthorizationPort）を作った。切り替えと引き継ぎのテスト 28 件を追加。docs/CLOUD_SETUP.md に有効にする手順のチェックリストを書いた。起動してログイン画面と設定画面（日本語・英語）を目で確かめた（screenshots/coming-soon/）。
- 2026-11-10: Data 層・画面・組み立て・翻訳・マニフェストを作り、アプリを完成させた。サブエージェント 4 つ（永続化、デバイス、ネットワークと認証、画面）を別の作業ツリーで並行させ、リードがまとめて組み立てを本物の Data 層に差し替えた。ローカルモードとデモ用アダプターで全画面を操作して確かめた。
- 2026-11-10: Application 層とそのテストを C# に移植した（9 機能、テスト 155 件合格）。
- 2026-11-10: 全プロジェクトを .NET 10 に上げた。Domain 層とそのテストを C# に移植した（56 件合格）。移植の規約を docs/PORTING_CONVENTIONS.md に書いた。
- 2026-11-10: iOS 版の CloudKit と認証の実装を調べ、Windows から同期する方式を設計した（下の「同期の設計メモ」）。

## 次にやること
- 画面の刷新の残り（main にマージ済み＝9384e06、push していない）：①開発者が実際に触って見た目の方向を確認する。②push するかどうかは開発者の指示を待つ。作業ツリーと枝（claude/ui-* の 9 本、すべて main にマージ済み）は 2026-11-12 に開発者の指示で片づけた。開発版パッケージは main の x64 Debug（bin\x64\Debug\…\win-x64）に登録し直した（データは保持）。.claude/worktrees/ に空のフォルダーが 3 つ（ui-integrate-784775、ui-perf-50684d、ui-shots-c21bcc）残っている（そのセッションが開いたままで消せない。セッションをアーカイブすれば消せる）。③Partner Center の掲載画像 6 枚 × 3 言語を差し替える（次のバージョンの提出のとき）。claude.ai の Artifact 版のご利用ガイドも古い。④ハイコントラスト、英・西の履歴の詳細・分析・車両編集を見る。⑤リードが見て気になった点：広い幅でもタイトルバーに ≡ が出ている、ライブの分類チップが楕円に見える、幅 1900 でライブの下半分が空く。
- 診断ログの残りの確認：①実機の OBD アダプターで接続し、`obd.connect` / `obd.wire` のログで段階が追えるかを見る。②わざとエラーを起こして（アダプターを抜くなど）エラー表示の報告番号と、Stable でリングバッファが吐かれることを見る。③英語・スペイン語・明るい外観の「診断」行、「診断ログを消す」。④push する前に PRIVACY.md の診断ログの文面を開発者が確認する。push 後、リポジトリの Variables に RELEASE_CHANNEL=beta を入れる（空でも Beta）。⑤PC に登録してある開発版パッケージを外す（`Get-AppxPackage Ryokugyoku.RevTorqueInsight | Remove-AppxPackage`）。
- 1.0.0.0 は審査に通り、Store に公開済み（Partner Center の状態「In Microsoft Store」、確認は 2026-11-11 以降）。テスターのアカウント（Store にサインインしている個人の Microsoft アカウント）で https://apps.microsoft.com/detail/restricted/9N7FN5KKVR1M から入れて、起動・デモ接続・データの保存を確かめる（まだ確かめていない）。2026-11-11 20 時ごろ：テスター用リンクも Store アプリの製品ページ（ms-windows-store://pdp/?productid=9N7FN5KKVR1M）も「お探しのものはここにはありません」、Store アプリ内の検索にも出ない。確かめたこと：Store アプリはグループと同じ個人の Microsoft アカウントでサインイン済み、公開中の設定はプライベート＋グループ選択済み、Windows 11 Pro x64・地域日本、wsreset でキャッシュを消しても同じ。公式の反映時間の記載はなく、フォーラムでは数日待てとの例あり → Store 側の反映待ちと判断。2026-11-13 ごろまで待っても出なければ Partner Center のサポートに問い合わせる。撮影用に登録した開発版（同じパッケージ名・署名なし）は 2026-11-11 に削除済み。今後、手元で同じパッケージ名の開発版を登録すると Store 版と競合するので注意（Store 版を入れている間は、開発版を登録する前に Store 版を外す）。
- 自動提出の設定：Partner Center で Entra のアプリ（マネージャー）を作り、GitHub の Secrets（PARTNER_CENTER_*）と Variables（STORE_PRODUCT_ID=9N7FN5KKVR1M）を登録する（docs/STORE_RELEASE.md の 3）。そのあとはタグ v1.2.3 の push で提出される。次のバージョンは 1.0.1 以上にする。
- パッケージ ID を Store の値（Ryokugyoku.RevTorqueInsight）に変えたので、手元の開発用のインストール（d2fa5ca6-… の仮の ID）とはデータのフォルダが別になる。古い方は不要になったら「アプリ」から削除する。
- GitHub Actions：CI は GitHub 上で成功した（2026-11-10、fae48b4）。Release はまだ動かしていない（最初の提出は手元で作った同じ形のパッケージを使う）。
- ローカルモードでも、履歴の詳細に「iCloudにアップロード」ボタンと「アップロード待ち」の印が出る（押しても失敗する）。Garage の副題「iPhoneと同期します」も同じ。Store の審査で「動かない機能」と言われるおそれがあるので、ローカルモードでは隠す（認定メモには「iCloud の機能は近日対応」と書いた）。
- 画面の作り直し（2026-11-10）を目で確かめる：接続バーの接続・切断、履歴の 2 列（幅 1100 以上）とパンくず・戻る、右クリックメニュー、整備の「新しい記録」と ↑↓ の選択、設定の行、ライブ取得中に分類ボタンを連打してもクリックが届くこと、暗い外観・英語・狭い幅（640）。
- Services ID が発行されたら、docs/CLOUD_SETUP.md の「0. 有効にする手順（チェックリスト）」を順に進め、最後に AppleSignIn:Enabled を true にする。
- 開発者の準備（docs/CLOUD_SETUP.md）：Apple Developer で Services ID とリダイレクト先の https ドメインを用意し、docs/web/ のページを置く。CloudKit Console で API トークン（Sign In Callback）を作り、AccountSettings と AccountSessionRevocation のスキーマを Production に反映する。appsettings.Local.json を置く。
- 実際の Apple・CloudKit につないで確かめる（下の検討事項の「CloudKit Web Services の確かめていない点」と「sub の一致」）。
- 実機の OBD アダプター（OBDLink EX の USB、OBDLink MX+ の Bluetooth、BLE の ELM327）で接続・識別・ライブ取得を確かめる。
- iOS 版の設定とログアウト通知を CloudKit 化する（ProjectZD8 リポジトリ側の作業。下の設計案）。それまで設定とログアウト通知は iOS と同期しない。
- 画面の細部：ローカルモードでも Garage の副題（「iPhone と同期します」）と整備の最終同期時刻（雲の記号つき）がクラウドに保存したように見える。ローカルモードでは出し分ける。
- Application.Tests の中のメモリ上の代用リポジトリと PID 定義の写しを、Data の実装に置き換える（Application.Tests から Data を参照する）。未移植の往復テスト（testRoundTripBetweenIPhoneAndMacPreservesSharedArchiveMetadata）を移す。
- ARM64 と x86 のビルドを確かめる（Store に出すのは今は x64 だけ）。
- JSON を System.Text.Json のソース生成に移せば、トリミングを戻せるかもしれない（パッケージを小さくする）。急がない。

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
- 掲載画像（6 枚 × 3 言語）の差し替えをいつ出すか（2026-11-12）→ 開発者に確認：画像だけ先に出すと、公開中の 1.0.0.0 は古い画面のままで食い違う。刷新後の 1.0.1 のパッケージとまとめて出す案がある。
- 画面の刷新（2026-11-11）→ 開発者に確認：①「先進的」の方向は、アイコンの紺＋シアン＋オレンジを軸にした計器的な見た目（Mica、薄い層のカード、大きな等幅数字、軽い動き）とリードが決めた。違えば早めに指示がほしい。②刷新後は Store のスクリーンショットとご利用ガイドの画像が古くなる。③軽量化は「main と同等」止まり（起動・ホームのリサイズ・ライブ中 CPU は同等、メモリ +1〜3 MB、ライブ取得中のリサイズは重い）。もとの画面も鍵が変わったときだけ作り直す方式で、ライブ中の CPU の大半は取得側だったため、画面側で大きく下げる余地は小さかった。さらに軽くするなら取得側（Application / Data）に手を入れる話になる → 開発者に確認。④ガレージが切断後も「車両接続を開始しています…」「現在接続中 1」と出る（main の刷新前から同じ。Application の状態が原因）。直すか。⑤ナビの「Garage」は日本語でも英語のまま（iOS 版の文言どおり）。
- 診断ログを実アプリで動かして気づいた点（2026-11-11）→ 直すか開発者に確認：①デモ用アダプターでは `obd.connect` と `obd.wire` が 1 行も出ない（ELM の経路を通らないため）。実機でしか確かめられない。接続要求から車両の保存までの間（識別）もデモではログが空く。②③④（sid の付け漏れ、起動時の余分な行、Beta のログ量）は efd5a9d で直した。実アプリでの量の再確認はまだ。⑤不具合レポートの保存先の初期値はデスクトップ。⑥今この PC には Stable ビルドの開発版パッケージ（main の bin を指す）が登録されている。Store 版を入れる前に外す。
- 診断ログの実装で出た点（2026-11-11）→ 開発者に確認・あとで直す：①`RELEASE_CHANNEL` は提出の種類に関係なく効くので、一般公開後に stable を入れるとフライトも stable になる（戦略文書 5 の「フライト＝beta」と違う。フライトを使い始めるときに release.yml を直す）。②詳細ログの 7 日切りは起動時だけ判定する。③エラー表示の報告番号は画面を作り直したときに読むので、状態が変わらないまま次の Error が出ると古い番号のまま。④起動の組み立てのうち診断ログを始める前（設定ファイルの読み込み）の失敗は記録されない。⑤Application のモデルの失敗は専用の分類がなく `ui.*.operation.failed`。⑥テスト用の記録シンクが Data.Tests・Application.Tests・Diagnostics.Tests に 3 つ重複している（土台で公開して 1 つにできる）。⑦接続前の識別と PID 対応の調査には `sid` が付かない。
- 製品の方向（2026-11-11 の市場分析から）→ 開発者に確認：①DTC の読取・消去と CSV 書き出しを次に入れるか（競合のほぼ全部にあり、ないと有料化しにくい）。②有料化するなら PolyForm Noncommercial のまま公開リポジトリで続けるか（コードが見える・商用利用を自分で行う場合の扱い）。③Windows 版の狙う利用者（ノート PC でログを取る走行会・チューニング勢か、一般の運転者か）。
- 少額サブスクの案（2026-11-11 の会話）→ 開発者に確認：基本無料＋Pro 年額 1,200〜1,800円（月額を出すなら 200円）。課金はベータ明けかつ DTC・CSV・同期が入ってから。注意点：①iOS（App Store）と Windows（Microsoft Store）の購入を共通にするには、購入状態を確かめる自前のサーバーかアカウントの仕組みが要る（今の「サーバーなし・端末内だけ」の方針と食い違う）→ 当面はストアごとに別の購入にする案。②CloudKit の容量は利用者の iCloud なので開発者の原価はほぼゼロ。そのぶん「続けて払う理由」（対応車種・PID の追加、書き出し形式、異常検知の更新など）を毎年出し続ける必要がある。③1 年目の手取りの目安は年 8万〜77万円ほど（年 3,000〜20,000 ダウンロード、課金率 2〜3%、年額 1,500円、手数料 15% の仮定。継続率 60% なら 2 年目以降に積み上がる）。
- 診断ログ（docs/LOGGING_STRATEGY.md の 10）→ 開発者に確認：①（決定済み → 決定事項）②Beta と Stable の分け方は、ビルド時の DiagnosticsChannel を release.yml の Variables `RELEASE_CHANNEL` で渡す案でよいか（今は beta、一般公開後はフライト＝beta・通常＝stable）。③レポートの zip の渡し方（メールか GitHub Issue か）。④iOS 版とイベント名を揃えるか。
- 診断ログで NuGet を 1 つ足す案（Microsoft.Extensions.Logging.Abstractions）。「NuGet を増やさない」これまでの方針と違うが、ソース生成の LoggerMessage が使えるので採った。ロガーは静的な入口（DiagnosticLog.For<T>()）で取り、コンストラクターには足さない（DI なしの組み立てとテストを変えないため）。
- ご利用ガイド（docs/USER_GUIDE.html）をお客様にどう届けるか未定：Artifact の共有リンク、GitHub Pages、Store の「Web サイト」欄に載せる、など → 開発者に確認。ガイドの FAQ（USB ドライバー、スリープ抑止）とガレージ・設定の説明は実機・画面で確かめていない。機能を変えたらガイドも直す。
- Store の審査で、ログイン画面と設定画面の押せない「近日対応」ボタンが「機能が完全でない」と指摘されるかもしれない。指摘されたら、Coming Soon の間はボタンを隠すか → 開発者に確認。
- 配布する CPU：今は x64 だけ。ARM64（Snapdragon の PC）も出すか → 開発者に確認。出すなら、ワークフローで ARM64 も作り、2 つの .msixupload を同じ提出に入れる工夫が要る（msstore publish は 1 ファイル）。
- GitHub のリリースは、ベータの間は pre-release にしている。一般公開のあとに外すか。
- .msixupload にシンボルが入らない（dotnet CLI では作れない）。Store のクラッシュ解析が読みにくい。要るなら Visual Studio の MSBuild でビルドする。
- Windows App Certification Kit（WACK）は手元で動かしていない。Store の審査で同じ検査がある。
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
- GitHub（Ryokugyoku/ProjectZD8ForWindows）に push 済み。git の user.name / user.email は未設定（コミットは -c で Ryokugyoku を指定）。

## 決定事項
- 2026-11-12: Partner Center の下書きの提出 2（変更なし）は削除する（開発者の指示）。理由：中身が空で、残すと「Update in progress」の表示が消えず、自動提出の妨げにもなりうるため。
- 2026-11-11: 画面の刷新の検討事項はリードに一任（開発者の指示「あなたが思う最高な状態で実装を進めて完了させて。セッションとモデルは分けて」）。リードの判断：①Compact のナビは下に並べるタブ帯（1 回押すだけで移れる。ハンバーガーは 2 回かかる）。②接続コンソールはホームでも状態だけ出す（画面を移ってもタイトルバーが変わらないほうが落ち着く）。③統合・目視・計測・手直しは 1 セッション（Opus 5.5）に任せ、確かめたうえで main にローカルでマージする（push はしない）。
- 2026-11-11: 画面の刷新は WebView2 に移さず WinUI 3 のまま行う（開発者は移行を許可、判断はリード）。理由：WebView2 は Chromium のプロセスが増えてメモリ（100MB 以上）と起動時間が増え、「軽量化」に反する。軽量化は「モデルの変化で木を作り直さず値だけ差し替える」「ライブ表示は 1 フレーム 1 回に間引く」「一覧の仮想化」で行う。
- 2026-11-11: 画面の刷新は、土台を 1 セッション（Opus 5.5）で先に作り、その枝から画面ごとのセッション（Sonnet 5.5）を並行で出す。作業セッションは PROGRESS.md と README を触らず、リードがまとめて直す（マージの衝突を避けるため）。
- 2026-11-11: 診断ログは開発者のサーバーへ自動送信しない。端末内（LocalState\diagnostics\）に置き、利用者が設定画面から不具合レポート（zip）を書き出して渡す（開発者が承認）。PRIVACY.md の「クラッシュ情報の自動送信も行いません」と合わせるため。
- 2026-11-11: 実装（コーディング）は作業ごとに新しいセッションで行い、難しさに応じてモデルを分ける（開発者の指示）。
- 2026-11-11: 診断ログの保存先は RDB にしない。分析できる形（JSON Lines）で置く。テストリリースは不具合を突き止められる詳しさ、リリース版は最小限にしつつバグ報告のときにすぐ分かるようにする（開発者の指示）。
- 2026-11-11: Store の配布はプライベート対象ユーザー（メールで限定）のまま続ける。「公開＋直接リンクのみ（リンクを知っている人なら誰でも）」も検討したが、やめた（開発者の判断）。理由：一度「公開」で保存するとメールで限定するプライベート配布に戻せず、再審査も要るため。テスターを増やすときは既知のユーザー グループにメールを足す。
- 2026-11-11: 最初の提出は、ローカルモードの iCloud ボタンを直さずにそのまま出す（開発者の判断「このまま提出して」）。審査で指摘されたら直して出し直す。
- 2026-11-11: Store のカテゴリは Utilities + tools、配布先は Windows デスクトップだけ、価格は無料、掲載言語は日本語・英語・スペイン語（パッケージの言語と同じ）。
- 2026-11-10: Windows 版のロゴは iOS 版のアプリアイコン（R の回転計とシアン・オレンジの信号の線）をそのまま使う（開発者が Claude に一任）。ブランドを揃え、Windows 版の配色もこのアイコンから取っているため。タスクバーとスタートは紺の角丸の板に図柄を置く。
- 2026-11-10: Store の製品名は「RevTorque Insight」（Store ID 9N7FN5KKVR1M、パッケージ名 Ryokugyoku.RevTorqueInsight）。Identity の値は公開されている値なのでマニフェストにコミットする。
- 2026-11-10: ベータ版は Microsoft Store のプライベート対象ユーザー（既知のユーザー グループ）で配る。一般公開のあとはパッケージ フライト（開発者の依頼「指定したユーザーのみにテスト公開」）。最初の提出だけ Partner Center で手で行う（Store の API は既存の提出を元にしか作れないため）。
- 2026-11-10: リリースは GitHub Actions で自動にする（開発者の依頼）。タグ v1.2.3 の push で、テスト → x64 の .msixupload → GitHub の pre-release → msstore CLI（microsoft/microsoft-store-apppublisher）で Store に提出。パッケージのバージョンはタグから作る（1.2.3.0）。STORE_PRODUCT_ID が空なら提出しない。仮の Publisher のままなら提出しない。
- 2026-11-10: Release でもトリミングしない（PublishTrimmed=False）。トリミングした Release のパッケージが起動直後に落ちた（Microsoft.UI.Xaml.dll、0xc000027b）。トリミングなしなら起動する。パッケージは約 80 MB。
- 2026-11-10: Windows 版も iOS 版と同じ PolyForm Noncommercial License 1.0.0 にし、GitHub に公開リポジトリ（ProjectZD8ForWindows）として置く（開発者の指示）。LICENSE は iOS 版と同じ文面。
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
