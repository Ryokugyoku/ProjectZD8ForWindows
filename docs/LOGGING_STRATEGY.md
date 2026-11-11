# 診断ログの戦略

アプリの動きと不具合を調べるためのログ（以下「診断ログ」）の方針です。
OBD の生ログ（`<データ>\logs\<セッション>\` のログストア）とは別物です。混ざらないよう、フォルダーも用語も分けます。

状態：実装済み（2026-11-11。作業表の #1〜#5 を main に入れた）。画面の目視、実機アダプター、Beta / Stable で起動したときのログの量は、まだ確かめていません。
実装で方針と変わった点：リングバッファから書き出した行には `"ring":true` が付く。`app.start`・`app.exit`・`app.previous-run.crashed`・`diag.dropped` はレベルに関係なく必ず書く。ファイル名の実行 ID は 6 桁、報告番号は先頭 4 桁＋`seq`。`RELEASE_CHANNEL` は提出の種類に関係なく効く（フライトだけ beta にする分岐はまだない）。Application のモデルの失敗は `ui.*.operation.failed`。

## 1. 今の状態と問題

- 診断ログの仕組みがない。`Debug.WriteLine` が 9 か所あるだけで、Release ビルドでは何も残らない。
- 捕まえられなかった例外は `%TEMP%\RevTorqueInsight\unhandled.log` に文字で追記するだけ（`App.xaml.cs`）。UI スレッド以外の例外は記録されない。
- `catch` が 63 ファイル・約 190 か所あり、多くは例外を画面用のエラー状態に変えるだけで、原因（例外の型・スタック）が消える。
- 実機の OBD アダプター（USB・RFCOMM・BLE）は未検証。テスターの手元で起きた不具合を、今は調べる手段がない。
- 取得性能の ETW（`AcquisitionPerformanceEventSource`）はある。これは性能測定用としてそのまま残す。

## 2. 方針（要点）

| | 開発（Debug ビルド） | テストリリース（Beta） | リリース（Stable） |
|---|---|---|---|
| ファイルに書く最低レベル | Trace | Debug（`obd.wire` は Trace） | Warning |
| 直前の出来事の記憶（リングバッファ） | なし（全部書くため） | なし（全部書くため） | Debug 以上を直近 2,000 件。Error・クラッシュのときだけファイルへ吐き出す |
| 保存の上限 | 500 MB・14 日 | 200 MB・14 日 | 20 MB・30 日 |
| ELM327 の送受信の記録 | あり | あり | リングバッファの中だけ |
| 不具合レポートの書き出し | あり | あり | あり |

- **保存先は JSON Lines のファイル**（1 行 1 イベントの JSON）。RDB は使わない。DuckDB・jq・PowerShell・Python でそのまま集計できる。
- **自動送信はしない**。ログは端末内（LocalState）だけに置き、利用者が設定画面から「不具合レポート」（zip）を書き出して、開発者に渡す。PRIVACY.md の「クラッシュ情報の自動送信も行いません」と合わせる。
- **リリース版は「フライトレコーダー」方式**。ふだんは Warning 以上しか書かないが、メモリに直近の細かいイベントを持っておき、Error かクラッシュが起きた瞬間にまとめて書く。ログは小さいまま、不具合の直前に何があったかは分かる。
- どの版でも、**利用者に見えるエラーには必ず 1 行の Error ログと例外の中身**（型・メッセージ・スタック）が対応する。

## 3. 保存先と形式

### 場所

```
<データのフォルダー>\diagnostics\
  app-20261111-7f3a9c.jsonl      実行 1 回につき 1 系列（日付 + 実行 ID の先頭 6 桁）
  app-20261111-7f3a9c.1.jsonl    10 MB を超えたら連番で次のファイルへ
  crash-20261111-7f3a9c.jsonl    クラッシュのときに同期で書く（リングバッファの中身 + 例外）
```

- データのフォルダーはパッケージの LocalState（`WindowsApplicationDataLocation`）。生ログの `logs\` とは別のフォルダー。
- 起動時に、上限（合計サイズ・日数）を超えた古いファイルを消す。
- アカウント削除では消さない（個人を特定する値を入れないため。4 を参照）。設定画面に「診断ログを消す」を置く。

### 1 行の形

```json
{"ts":"2026-11-11T12:34:56.7890123Z","lvl":"error","cat":"obd.connect","evt":"obd.connect.failed","run":"7f3a9c2e","seq":1842,"sid":"3F2A…","msg":"接続に失敗しました","props":{"transport":"ble","stage":"gatt-discovery","elapsedMs":10012},"ex":{"type":"System.TimeoutException","msg":"…","stack":"…","inner":null}}
```

| キー | 内容 |
|---|---|
| `ts` | UTC、ISO 8601、100 ns まで |
| `lvl` | `trace` / `debug` / `info` / `warn` / `error` / `fatal` |
| `cat` | 分類（下の表）。フィルターの単位 |
| `evt` | イベント名。**変えない固定の名前**（`分類.対象.結果`）。集計はこれで行う。文言（`msg`）は変えてよい |
| `run` | 実行 ID（起動ごとの乱数）。1 回の起動の流れをつなぐ |
| `seq` | 実行内の通し番号。リングバッファから吐き出した行と通常の行を正しい順に並べ直すのに使う |
| `sid` | 接続セッションの ID（あるときだけ）。生ログ・履歴と突き合わせる |
| `msg` | 人が読む短い説明 |
| `props` | イベントごとの値（数値・短い文字列だけ） |
| `ex` | 例外（型・メッセージ・スタック・内側の例外） |

各実行の最初の行は `app.start`：アプリのバージョン、チャンネル（dev / beta / stable）、OS のビルド、CPU、表示言語、ローカルモードか、パッケージ版か。

### 分類（`cat`）

| 分類 | 内容 | 主な場所 |
|---|---|---|
| `app` | 起動・終了・前回の異常終了・設定の読み込み・機能フラグ | `App.xaml.cs`、`App/` |
| `ui` | 画面の移動、ダイアログ、利用者の主な操作（接続・切断・保存・削除） | `Platform/Windows/` |
| `obd.discovery` | 機器の探索、到来の監視 | Data.Windows `Devices/` |
| `obd.connect` | 接続・切断・初期化（ATZ など）・プロトコルの決定 | Data `Devices/OBD/`、各 Transport |
| `obd.wire` | ELM327 の送信と受信（Trace）。実機の不具合の主な手がかり | `SerializedELMCommandChannel`、各 Transport |
| `acq` | 取得の開始・停止、バッチの保存、落ちたバッチ、周期の遅れ | Application `Logging/`、`LiveTelemetry/` |
| `store.sqlite` | 開く・移行・失敗 | Data `Persistence/Sqlite/` |
| `store.log` | 生ログストアの開き直し・末尾の切り詰め・fsync の失敗 | Data `Persistence/LogStore/` |
| `auth` | サインイン・復元・失効（Coming Soon の間はほぼ出ない） | Data `Authentication/` |
| `sync` | CloudKit の要求と結果（HTTP の状態、レコード種別、件数、所要時間） | Data `Network/CloudKit/` |

### レベルの使い分け

- `fatal`：プロセスが落ちる。
- `error`：利用者の操作が失敗した（画面にエラーが出る）。**必ず例外を付ける。**
- `warn`：自動で回復した異常（再試行、末尾の切り詰め、タイムアウト後の再接続、フォールバック）。
- `info`：1 回の起動で数十件ほどの節目（起動、接続の成功、セッションの開始と終了、同期の完了）。
- `debug`：判断の理由と途中の状態（どの機器を候補にしたか、どのプロトコルを試したか、状態遷移）。
- `trace`：送受信の中身、1 件ごとの処理。量が多いので `obd.wire` などに限る。

## 4. 書かないもの（プライバシー）

診断ログは利用者が開発者に渡すファイルなので、次は入れない。

- VIN：末尾 4 桁だけ（`…1234`）。
- アカウントの識別子（Apple の sub、ローカルモードの ID）：SHA-256 の先頭 8 桁だけ。
- トークン・秘密（ID トークン、ckWebAuthToken、API トークン）：一切書かない。URL はクエリを落とす。
- Bluetooth のアドレス・シリアル番号：末尾 2 バイトだけ。機器の名前は書く。
- 車両の名前、整備記録の本文、写真、ファイルのフルパス（ユーザー名を含むため、データのフォルダーからの相対パスにする）。
- OBD の応答の中身は `obd.wire` に書く（車の状態の値で、個人を特定しない）。ただし VIN を返す要求（Mode 09 PID 02）の応答は伏せる。

伏せる処理は 1 か所（`Redact`）に集め、テストで確かめる。

## 5. 版の切り替え

- ビルド時の MSBuild プロパティ `DiagnosticsChannel`（`Dev` / `Beta` / `Stable`）をアセンブリのメタデータに埋め込む。Debug ビルドは `Dev`。Release ビルドは指定がなければ `Stable`。
- `release.yml` は、リポジトリの Variables の `RELEASE_CHANNEL` を渡す。**今はプライベート対象ユーザー向けのベータだけなので `beta` にしておく。** 一般公開のあとは、パッケージ フライト（`STORE_FLIGHT_ID` あり）の提出を `beta`、通常の提出を `stable` にする。
- 実行時の上書き（強い順）：環境変数 `PROJECTZD8_DIAGNOSTICS_LEVEL` → `appsettings.Local.json` の `Diagnostics:Level` → 設定画面の「詳細ログ」スイッチ → チャンネルの既定。
- リリース版の利用者にも、サポートのときに設定画面で「詳細ログ」を入れてもらえる（Beta と同じ量になる）。入れっぱなしを防ぐため、7 日で自動で切れる。

## 6. 不具合の報告を簡単にする

1. **エラー表示に報告用の番号を出す**：`7F3A-1842`（実行 ID の先頭 4 桁 + `seq`）。利用者がこの番号を伝えれば、ログの該当行がすぐ見つかる。
2. **設定画面に「不具合レポートを書き出す」**：zip を 1 つ作り、エクスプローラーで開く。中身は
   - `diagnostics\*.jsonl`（直近 7 日）
   - `summary.json`（バージョン、チャンネル、OS、CPU、言語、既定アダプターの種類、DB のスキーマ版、セッション数、直近の Error 10 件）
   - 生ログ・SQLite・写真は**入れない**（別に頼むときは、既存の書き出しを使う）。
3. **前回の異常終了を知らせる**：正常終了のときに `app.exit` を書く。起動時に前回の実行に `app.exit` がなければ `app.previous-run.crashed` を記録し、シェルの InfoBar で「前回は正常に終了しませんでした。レポートを書き出しますか」と尋ねる。
4. **クラッシュの記録**：`Application.UnhandledException`（UI）、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException` の 3 つを受け、リングバッファと例外を `crash-*.jsonl` に同期で書く。`%TEMP%` の `unhandled.log` はやめる。
5. Store のクラッシュ解析（Partner Center の正常性レポート）も見る。シンボルが入っていない問題（PROGRESS.md の検討事項）は別件として残す。

## 7. 分析のしかた

ファイルのままで集計する。例（DuckDB、サーバーなし）：

```sql
-- エラーの多い順
SELECT evt, count(*) n, count(DISTINCT run) runs
FROM read_json_auto('diagnostics/*.jsonl') WHERE lvl IN ('error','fatal')
GROUP BY evt ORDER BY n DESC;

-- 報告用の番号 7F3A-1842 の直前 200 行
SELECT ts, lvl, evt, msg, props FROM read_json_auto('diagnostics/*.jsonl')
WHERE run LIKE '7f3a%' AND seq BETWEEN 1642 AND 1842 ORDER BY seq;
```

PowerShell だけでも読める：

```powershell
Get-Content diagnostics\*.jsonl | ConvertFrom-Json | Where-Object lvl -eq 'error' | Group-Object evt | Sort-Object Count -Descending
```

`scripts/Analyze-Diagnostics.ps1` に、レポートの zip を渡すと「環境・エラーの一覧・各エラーの直前の流れ」を出す処理を用意する。

## 8. 実装の形

- **新しいプロジェクト `src/ProjectZD8.Diagnostics`（net10.0）**。Application・Data・Data.Windows・アプリ本体から参照する。Domain からは参照しない（Domain は純粋なまま）。
- API は `Microsoft.Extensions.Logging.Abstractions` の `ILogger` と、ソース生成の `[LoggerMessage]`（イベント名と ID を固定でき、無効なレベルでは割り当てが起きない）。追加する NuGet はこの 1 つだけ。
- ロガーは `DiagnosticLog.For<T>()` で取る（静的な入口。既定は何もしないロガー）。コンストラクターに足さないので、既存のテスト 522 件と静的な組み立て（DI なし）に手を入れずに済む。組み立て（`WindowsApplicationComposition`）の最初で本物の出力先を差し込む。
- 出力先（自作）：上限付きキュー + 背景スレッド 1 本で JSONL に追記する（生ログストアの書込みと同じ考え方）。キューがあふれたら Trace / Debug から捨て、捨てた件数を `diag.dropped` に書く。**計測のスレッドを待たせない。**
- 取得のホットパス（PID 1 件ごと）には Trace を置かない。バッチ単位で 1 行にする。`obd.wire` は ELM のコマンド 1 往復で 1 行。
- 性能の目安：Beta で取得中の CPU の増加 2% 以内、ログの無効なレベルの呼び出しは割り当て 0。`tools/ProjectZD8.LogStoreBenchmark` と同じ形で測る。

## 9. 実装の順番と担当モデル

実装は作業ごとに新しいセッションで行い、難しさでモデルを分ける。

| # | 作業 | 難しさ | モデル | 前提 |
|---|---|---|---|---|
| 1 | 土台：`ProjectZD8.Diagnostics`（JSONL の書込み、レベル、リングバッファ、ローテーションと掃除、`Redact`、チャンネル）、クラッシュの 3 つのハンドラー、`app.start` / `app.exit`、組み立てへの差し込み、テスト | 高（並行処理・クラッシュ時の同期書込み・性能） | Opus | なし |
| 2 | OBD と取得の計装：`obd.discovery` / `obd.connect` / `obd.wire` / `acq`、Data.Windows の Transport 3 種 | 高（ホットパス、何を残せば実機の不具合が分かるかの判断） | Opus | 1 |
| 3 | 保存・認証・同期と Application のモデルの計装：`catch` で消えている例外を Error にする、`Debug.WriteLine` 9 か所の置き換え | 中（数は多いが型どおり） | Sonnet | 1 |
| 4 | 画面：設定の「診断」行（詳細ログ、レポートの書き出し、ログを消す）、エラー表示の報告用番号、前回の異常終了の InfoBar、翻訳 3 言語（`WindowsOnly.xcstrings`） | 中 | Sonnet | 1 |
| 5 | `release.yml` の `RELEASE_CHANNEL`、`scripts/Analyze-Diagnostics.ps1`、README・PRIVACY.md・USER_GUIDE・STORE_RELEASE の更新 | 低 | Haiku | 1 |

2〜5 は 1 が main に入ってから、触るファイルが重ならないので並行できる（3 と 4 は Application のモデルで重なりうるので、3 は `src/`、4 は `Platform/` と `App/` に限る）。

## 10. 決めていないこと

- **開発者のサーバーへの自動送信**：しないと決めた（2026-11-11、開発者が承認）。将来テスターが増えて手渡しが回らなくなったら、同意を取ったうえで Azure Blob などへ JSONL をそのまま上げる形にできる（形式は変えずに済む）。そのときは PRIVACY.md と Store の申告を先に直す。
- **Beta と Stable の分け方**：5 の案（`RELEASE_CHANNEL`）でよいか。今 Store に出ている 1.0.0.0 は、仕組みが入る前なのでログが出ない。
- **レポートの渡し方**：zip をメールで送ってもらうか、GitHub の Issue に付けてもらうか。
- **iOS 版と `evt` の名前を揃えるか**：揃えると両方のログを同じ問い合わせで見られる。
