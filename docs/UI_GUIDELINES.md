# 画面の作り方（UI ガイドライン）

画面を作り直すときは、この文書だけ読めば同じ見た目・同じ応答性で作れるようにしてある。
部品とトークンは `Platform/Windows/DesignSystem/`、見本はホーム（`Features/DeviceConnection/Views/WindowsHomeView.xaml.cs`）。

## 1. 方針

- **先進的・計器的**。Mica の背景に、薄い層のカード（半透明の単色の塗り＋1px の単色の線）。発光（グラデーションの縁）は各画面の主役のカード 1 枚だけで、面の塗りは単色（大きな面のグラデーションは伸び縮みのたびに描き直しが重い。実測で幅を変える間の CPU が約 4 ポイント増えた）。先進的な見た目は色・余白・文字・動きで出す。数値は大きく等幅。余白はゆったり。
- **色**：アイコンの紺＋シアン（主）＋オレンジ（小さな強調）。シアンは選択・フォーカス・主の操作・分析の深さだけ。オレンジは注意や信号のピークだけ（接続・正常には使わない）。緑は「接続中・記録中・完了」をアプリの状態が裏付けるときだけ。
- **明・暗・ハイコントラストの 3 つで成立させる**。色は必ずトークンから引く（ハイコントラストではシステムの色に置き換わる）。
- **軽く**：要素は作り直さず値だけ差し替える。更新は 1 フレームに 1 回まで。動きは GPU（Composition）で動くものだけ。

## 2. 幅クラス

`ResponsiveLayout`（`ResponsiveLayout.cs`）が**ウインドウの幅**を 1 か所で判定する。最小ウインドウは 640×420。

| クラス | ウインドウの幅 | ナビ | 中身の幅の目安 | 列の目安 |
|---|---|---|---|---|
| Compact | 〜759 | 画面の下のタブ帯（高さ 48。全項目を等間隔・記号＋短い題。タイトルバーの ≡ は出さない） | 〜760 | 1 列。一覧と詳細は片方だけ |
| Medium | 760〜1199 | 記号だけの細いレール（48） | 710〜1150 | 1〜2 列 |
| Expanded | 1200〜1599 | 題つきのペイン（232） | 970〜1370 | 2〜3 列。一覧と詳細を左右に |
| Wide | 1600〜 | 題つきのペイン（264） | 1340〜（中身は最大 1360 で中央） | 3〜4 列 |

確かめる幅：640 / 900 / 1280 / 1920（それぞれ Compact / Medium / Expanded / Wide）。（Compact の境目は 759 / 760）

原則：

- **画面は SizeChanged を見ない**。`ResponsiveLayout.Bind(this, ApplyLayout)` で購読し、クラスが変わったときだけ Grid の行・列・余白・Visibility を差し替える（木を作り直さない）。外されたら自動で解除される。XAML の画面は `ResponsiveLayout.BindVisualStates(this)` で VisualState（"Compact" など）へ移す。
- 横スクロールを出さない。文字は折り返すか、`TextTrimming` で切る。チップ・状態の印・ボタンの並びは `FlowPanel` で折り返す（等幅の枠に詰めて文字を切らない）。固定幅の要素は Compact で入るか確かめる。
- 本文の行は `Metrics.ReadingMaxWidth`（720）まで。ページの中身は `Metrics.ContentMaxWidth`（1360）まで（`PageScaffold` がやる）。
- 例外：グラフや一覧の**中**で、要素の実際の幅に合わせて描くもの（グラフの再描画など）は SizeChanged を見てよい。そのときも木は作り直さず、描き直すのは図形だけ。

## 3. トークン（直値を書かない）

`DesignTokens.cs`・`Typography.cs`・`RevTorqueTheme.xaml`。

**余白 `Space`**：XXS 2 / XS 4 / S 8 / M 12 / L 16 / XL 20 / XXL 24 / XXXL 32。幅クラス別：`Space.PageGutter(c)`（16/24/32/40）、`Space.Section(c)`（16/20/24/24）、`Space.Gap(c)`（カードどうし 12/16）、`Space.CardPadding(c)`（16/20）。

**角丸 `Radius`**：S 4（印）/ Control 6（ボタン・入力欄。テーマで全コントロールに適用済み）/ M 8（行・静かな面）/ L 12（カード）/ XL 20（主役のカード）/ Pill。

**大きさ `Metrics`**：TitleBarHeight 48、ControlHeight 36、PrimaryControlHeight 44・PrimaryControlMinWidth 176、RowMinHeight 56、ReadingMaxWidth 720、ContentMaxWidth 1360、TileMinWidth 168、CardMinWidth 280、WideCardMinWidth 420、IconTileSmall/Medium/Large/Hero 32/40/56/80。

**動き `Motion`**：Fast 120ms / Normal 200ms / Slow 320ms、`Motion.IsEnabled`（OS の「アニメーション効果」）。

**文字 `TextStyles`**：Eyebrow（11・字間広め・シアン）/ Display（40。画面に 1 つ）/ PageTitle（28）/ SectionTitle（20）/ CardTitle（16 太字）/ Body / BodyStrong / SecondaryBody / Caption / SecondaryCaption / MetricHero（56）/ MetricLarge（36）/ MetricValue（20）/ MetricUnit / Monospace。数値は `Typography.Metric(...)`（等幅数字・折り返さない）。

**色**：`DesignTokens.Foreground(tone)`・`SubtleBackground(tone)`・`Stroke(tone)`（`Tone`：Accent / Caution / Success / Danger / Indigo / Neutral）。面は `DesignTokens.Brush("…")`：`RevTorqueCardBrush`・`RevTorqueCardRaisedBrush`・`RevTorqueCardStrokeBrush`・`RevTorqueDividerBrush`・`RevTorqueQuietFillBrush`・`RevTorqueHeroBrush`・`RevTorqueCardGlowStrokeBrush`・`RevTorqueSignalBrush`（シアン→オレンジ。計器の弧・強調の線）・`RevTorqueChartGridBrush`。WinUI のシステムのブラシ（`TextFillColorSecondaryBrush` など）は `DesignTokens.SystemBrush("…")`。`Color.FromArgb` を画面に書かない。

**スタイル**：`SurfaceCardStyle`・`HeroCardStyle`・`QuietSurfaceStyle`・`AccentButtonStyle`（主の操作は 1 画面に 1 つ）・`DangerButtonStyle`（取り消せない操作）・`ChromeButtonStyle`（枠のない記号だけのボタン）・`SubtleButtonStyle`。

## 4. 部品

| 部品 | 使いどころ | 差し替え |
|---|---|---|
| `PageScaffold` | 画面の外枠（縦スクロール・最大幅で中央・幅クラスの余白）。`Body` に節を足す | — |
| `AdaptiveGrid` | カード・タイルの格子。`AdaptiveGrid.Tiles()` / `Cards()` / `WideCards()`、`SetMaxColumns(c,m,e,w)`、`AnimatesReflow`。間隔を指定しなければ幅クラスのトークン。Collapsed の子は詰める。子は数十個まで | 子は `KeyedChildren` で |
| `AdaptiveSplit` | 主と副の 2 面。広いと左右、`CollapseBelow` より狭いと `Stack`（積む）か `Single`（片方だけ。`ShowsSecondary`）。設定はどの順に入れても入れた時点で並べ直す。外周の余白は持たないので、`PageScaffold` の外で使うときは `Space.PageGutter` を付ける（整備） | `Apply()` |
| `SurfaceCard.Create(content, tone, padding, tinted, variant)` / `SurfaceCard.Hero(content)` | カード。主役（Hero）は 1 画面に 1 枚 | — |
| `ScreenHeader` | 画面の見出し（記号のタイル・Eyebrow・題・副題・右の要約）。ホーム以外の画面は記号のタイルを付ける。Compact で要約を下へ回す。一覧の横の細い欄では右の要約を使わず、題の下に `FlowPanel` で並べる | `SetTitle` / `SetSubtitle` |
| `MetricBlock(value, [unit,] caption, …)` | 数値＋説明。等幅数字 | `SetValue` / `SetCaption` |
| `StatusPill.Create(glyph, text, tone)` | 状態の印（glyph が null なら点） | `StatusPill.Set(pill, …)` |
| `IconTile.Create(glyph, tone, size)` | 見出しの記号のタイル | `IconTile.SetTone(tile, …)` |
| `SettingsRow.Create(…)` / `Header` | 設定の行。Compact で操作を下の段へ | — |
| `ContentUnavailablePanel` | 表示するものがないとき | — |
| `FlowPanel` | 小さな部品（チップ・状態の印・操作のボタン）を左から並べ、収まらなければ次の行へ（`Wraps = false` で 1 行） | 子はそのまま |
| `GlyphLabel(glyph, text, …)` | 記号＋文字。ボタンの中身に置くと、名前のないボタンへ文字を読み上げ名として渡す | — |
| `VirtualList<TItem>` | 何百件にもなる一覧（仮想化した ListView。行はコードで 1 回だけ組み、使い回す）。行の要素に `AutomationProperties.Name` を付けると器へ写す（付けないと項目の ToString が読み上げられる） | `Sync(items, key)` |
| `KeyedChildren<TKey,TItem,TElement>` | 数十個までの手組みの並び（カード・行） | `Sync(items)` |
| `Retained.SetText / SetVisible / SetEnabled / SetStyle / SetForeground / SetGlyph` | 値が違うときだけ設定する | — |
| `UiUpdateThrottle` | モデルの知らせを束ね、1 フレームに 1 回まで（`MinInterval` でさらに間引く） | `Request()` / `FlushNow()` |

## 5. 更新の作法（作り直さない・間引く）

ホームが見本。形はこう：

```csharp
// 1) 要素はコンストラクターで 1 回だけ組み、フィールドに持つ
private readonly TextBlock _name = Typography.Text("", TextStyles.PageTitle);
// 2) 知らせは束ねる
_throttle = new UiUpdateThrottle(this, Update);            // ライブ値なら minInterval: TimeSpan.FromMilliseconds(100)
Loaded += (_, _) => { model.PropertyChanged += OnChanged; _throttle.FlushNow(); };
Unloaded += (_, _) => model.PropertyChanged -= OnChanged;
void OnChanged(object? s, PropertyChangedEventArgs e) => _throttle.Request();
// 3) Update は値だけを差し替える（同じ値なら触らない）
void Update() { var state = ...; Retained.SetText(_name, state.Name); StatusPill.Set(_pill, ...); }
// 4) ボタンの Click は、押した時点の状態を読み直して操作を送る（作ったときの state を閉じ込めない）
```

- **`Children.Clear()` して全部作り直さない**。並びが変わるものは `KeyedChildren` か `VirtualList` の `Sync` で差分だけ直す。
- 一覧は仮想化を前提にする（`VirtualList` か ListView / ItemsView）。StackPanel に何百も積まない。
- ライブ表示は 1 フレームに 1 回まで。人が読む数値は 100ms 間隔で十分（`MinInterval`）。グラフの点も同じ間引きの中で足す。
- **RenderGate の決まりは残す**：まだ「作り直す」方式の画面は、作り直す前に `_gate.TryDefer(Render)` を呼ぶ（押している最中・メニューやドロップダウンが開いている間は待つ）。要素を作り直さない方式に移した画面（ホームのように、ボタン・メニュー・ドロップダウンを作り直さない）は RenderGate が要らない。半分だけ移した画面は残すこと。

## 6. シェル

- タイトルバーまで伸ばした 48 の行に：戻る（戻れるときだけ）・ナビの開閉（≡。Medium 以上のみ）・ブランド・**接続コンソール**（点・アダプター名・状態・「ライブを表示」・接続／切断。ホームでは状態だけ）。狭いほど文字を省く（ツールチップに残る）。
- タイトルバーの押せる領域（`InputNonClientPointerSource` の Passthrough）は OS への重い呼び出し。配置が落ち着いてから 250ms 後に 1 回だけ登録する（`ScheduleTitleBarRegions`）。毎回の SizeChanged や起動直後に登録すると、起動が約 100ms、幅を変える間の CPU が数ポイント悪化した（実測）。
- 最初に見えないもの（Compact の下のタブ帯）は、その幅になったときに初めて作る。
- **ナビは幅クラスで形が変わる**（`ApplyWidthClass` が切り替わったときだけ値を差し替える。木は作り直さない）。Medium：記号だけのレール、Expanded / Wide：題つきのペイン、**Compact：画面の下のタブ帯**（`BottomBar`、高さ 48）。
  - ナビの形の切り替え（NavigationView の表示の形・ペインの幅・下のタブ帯）は 1 回で 7〜12ms の配置がかかる（実測。刷新前は形を変えなかった）。縁を引っぱって幅を**続けて**変えている間（`ResponsiveLayout.IsLiveResizing`：150ms 未満の間隔で 3 回以上）は、しきい値をまたぐたびに切り替えず、止まって 150ms 後に 1 回だけ切り替える（`ApplyNavigation`）。最大化・スナップ・起動のような 1 回きりの変化ではすぐ切り替える。タイトルバーの文字の省略と画面の中身は、待たせずにすぐ切り替える。これで、ライブ取得中に幅を往復させたときの UI スレッドの CPU が刷新前と同じになった。
  - タブ帯は行き先すべて（ホーム・ライブ・履歴・整備・ガレージ・設定）を等間隔に並べ、記号＋短い題（切れるときは `…`、ツールチップに Ctrl+n 付きの題）。1 回押すだけで移る。選択中はシアンの印（上辺の細い線が Composition で伸びて現れる。`Motion.IsEnabled` が false なら即時）と色。履歴の中断バッジも出す。
  - 選択中の行き先は 1 つ（`SelectedDestination`）。NavigationView の選択とタブの選択の両方を `Select` / `ShowDestination` が合わせるので、幅クラスが変わってもずれない。Compact では NavigationView のペインは使わない（開かない）。
  - 色は `DesignTokens.Foreground(Tone.…)`（テーマに追従）から引き、`ActualThemeChanged` で引き直す。`SystemBrush` はウインドウだけ明暗を変えたとき追従しないので、シェルのコードでは使わない。
- 画面の中の階層は `IBackNavigable` を実装すれば、タイトルバーの戻る・Alt+←・マウスの戻るで 1 段戻る。
- InfoBar（アダプター到来・前回の異常終了）はシェルが画面の上に出す。画面の中に同じ知らせを重ねない。
- ページの切り替えは `EntranceThemeTransition`（シェルがやる）。画面は自分で出入りの動きを付けない。
- ショートカット：Ctrl+1〜6、Ctrl+,、Alt+←。画面独自のもの（Ctrl+S、F5）は画面の `KeyboardAccelerators` に。

## 7. やってはいけないこと

- 色・余白・角丸・文字の大きさの直値（`new Thickness(26)`、`Color.FromArgb(...)`、`FontSize = 17`）。トークンを使う。足りなければトークンを足す。
- 画面ごとの `SizeChanged` で木を作り直すこと。幅で分けたいときは `ResponsiveLayout.Bind`。
- モデルの知らせのたびの `Children.Clear()`。1 秒に何度も来る知らせを束ねずに描くこと。
- 大きな面の塗りにグラデーションを使うこと（縁の細い発光は主役のカード 1 枚まで）。幅を変えている間に毎回 OS へ何かを登録すること（タイトルバーの領域などは落ち着いてから 1 回）。
- Acrylic を画面に使うこと（重ねて開くナビ・メニュー・フライアウトなど、システムが使う所だけ）。`ThemeShadow` / `DropShadow` をカードに付けること（影はダイアログ・フライアウトなどシステムの重なりだけ）。
- `Storyboard` で常に動き続けるアニメーション（脈打つ点など）。動きは ThemeTransition・Composition の暗黙アニメーションで、出入りや配置の変化のときだけ。`Motion.IsEnabled` が false なら動かさない。
- 文字を足すとき `Strings/WindowsOnly.xcstrings` に日・英・西をそろえずに足すこと（足したら `scripts/ConvertXcstrings.cs` で .resw を作り直す）。
- 主の操作（`AccentButtonStyle`）を 1 画面に 2 つ以上置くこと。主役のカード（Hero）を 2 枚以上置くこと。

## 8. 画面ごとのレイアウト（最終の実装）

2026-11-11 に統合して実機（640 / 900 / 1280 / 1920、高さ 420、明・暗、日・英・西）で確かめた形。作り直すときもこれに合わせる。

**共通**
- 画面の頭は `ScreenHeader`（記号のタイル＋Eyebrow＋題＋副題）。ホームだけは記号なしの Display の見出し。整備・車両の登録の確認も同じ見出しを使う。
- 低い画面（画面の高さ `Metrics.ShortViewHeight` 480 未満。ウインドウの高さ 420〜560 前後）では、一覧のある画面は見出し（整備のページ見出し・履歴の車両の見出し）を畳んで一覧に高さを譲る。畳んだ情報はパンくずや別のカードに残っているものに限る。
- 外周の余白は `Space.PageGutter`（`PageScaffold` か、整備のように自前で付ける）。シェルの InfoBar（前回の異常終了など）も同じ余白で並ぶ。
- 文字の短い名前：Compact の下のタブは「ライブ」「履歴」（`windows.nav.tab.*`）。ペインの題は長い訳で末尾を「…」に（全文はツールチップ）。スイッチのオン／オフはアプリの言語の文字（`windows.toggle.*`）。

**ホーム**（見本）
- 見出し（Display）→ 主役のカード（左：計器の輪＋アダプター名＋状態、右：主の操作）→ 今後の機能の帯。Compact は主役のカードの中を積み、操作は全幅。

**ライブ表示**（`WindowsLiveTelemetryView`）
- 計器帯：エンジン回転数・車速を MetricHero で（Compact は縦に 2 つ、Medium 以上は横に 2 つ）。Expanded 以上は右に応答 PID 数と状態の印、Medium 以下は応答数を見出しの右へ。
- 状態の帯（InfoBar）は読み取り中・停止中・失敗で出す。取得が落ち着いたら（Loaded）Expanded 以上では帯を閉じ、右の印だけにする（同じ文言を 2 か所に出さない）。
- 分類のボタンはどの幅でも `FlowPanel` で折り返す。値の格子は `SetMaxColumns(2, 3, 4, 6)`。
- 値は要素を保持して `UiUpdateThrottle(100ms)` で差し替え、並びは `KeyedChildren`。切断後に残る最後の値は薄く（不透明度 0.55）して、読み取り中の値と見分ける。

**履歴・詳細・分析**（`WindowsConnectionHistoryView`・`WindowsConnectionSessionDetailView`・`WindowsSessionLogAnalysisView`）
- アーカイブ：見出しのカード（総数・総走行時間）→ 車両カードの格子（`Cards()`、Compact は 1 列）。
- セッション一覧と詳細：`AdaptiveSplit`（一覧 400、`SecondaryFirst`、Expanded 以上で左右、Medium 以下は `Single`）。一覧は `VirtualList`。行の時間・距離の列は幅をそろえ、状態の印は折り返す。
- 詳細：操作のボタンは文字の幅のまま `FlowPanel` で折り返す。見出しの下に状態・車種・ID を並べる。
- 分析：見出し（件数は出さない）→ 要約の 3 枚 → タブ（走行サマリー・折れ線・相関）。グラフは最小の高さ 320（Compact 240）、右に PID の選択と説明（Medium 以下は上に積む）。

**ガレージ・車両編集・PID 設定**（`WindowsGarageView`・`WindowsVehicleEditorView`・`WindowsVehiclePIDSettingsDialog`）
- ガレージ：見出し（右に同期）→ 要約タイル → 車両カード（`SetMaxColumns(1, 2, 3, 4)`、写真の高さは Compact 140・ほか 176）。
- 車両編集：`AdaptiveSplit`（写真 320 を左、Expanded 未満は写真を上に積む）。入力のカードは `AdaptiveGrid`（最小 240）。保存は右上（Ctrl+S）、戻るは左上の丸いボタン。
- PID 設定のダイアログ：一覧は `VirtualList`、高さはウインドウに合わせて 220〜560。

**整備**（`WindowsMaintenanceView`・`WindowsMaintenanceEditorView`）
- 見出し（`ScreenHeader`：MAINTENANCE LOG・整備・副題。低い画面では畳む）→ タイムラインと詳細：`AdaptiveSplit`（タイムライン 340、`SecondaryFirst`、Expanded 以上で左右、Medium 以下は `Single`）。タイムラインの頭は「記録」の小見出しと新規・同期のボタン。外周に `Space.PageGutter`。
- 編集：入力は `AdaptiveGrid`（最小 240）、写真は `Tiles()`。ツールバー（削除・取消・保存）は Compact で記号だけ。

**設定**（`WindowsSettingsView`）
- 中身の最大幅 1000。グループ（全般・接続・データと同期・診断・アカウント）ごとに見出し、Expanded 以上は左に目次（押すとそのグループへ移る。スクロールしても上に留まる。留める位置は Composition の式で GPU が決め、スクロールのたびに UI のスレッドで動かさない）。行は作り直さず値だけ差し替える。Compact は行の操作を下の段へ。

**ログイン**（`WindowsLoginView`）
- 常に暗い外観。Expanded 以上（または Medium で高さが十分）は紹介とサインインのパネルを左右、それ以外は積む。
