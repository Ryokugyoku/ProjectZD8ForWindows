# 画面の作り方（UI ガイドライン）

画面を作り直すときは、この文書だけ読めば同じ見た目・同じ応答性で作れるようにしてある。
部品とトークンは `Platform/Windows/DesignSystem/`、見本はホーム（`Features/DeviceConnection/Views/WindowsHomeView.xaml.cs`）。

## 1. 方針

- **先進的・計器的**。Mica の背景に、薄い層のカード（半透明の塗り＋細い線＋上辺だけの控えめな発光）。数値は大きく等幅。余白はゆったり。
- **色**：アイコンの紺＋シアン（主）＋オレンジ（小さな強調）。シアンは選択・フォーカス・主の操作・分析の深さだけ。オレンジは注意や信号のピークだけ（接続・正常には使わない）。緑は「接続中・記録中・完了」をアプリの状態が裏付けるときだけ。
- **明・暗・ハイコントラストの 3 つで成立させる**。色は必ずトークンから引く（ハイコントラストではシステムの色に置き換わる）。
- **軽く**：要素は作り直さず値だけ差し替える。更新は 1 フレームに 1 回まで。動きは GPU（Composition）で動くものだけ。

## 2. 幅クラス

`ResponsiveLayout`（`ResponsiveLayout.cs`）が**ウインドウの幅**を 1 か所で判定する。最小ウインドウは 640×420。

| クラス | ウインドウの幅 | ナビ | 中身の幅の目安 | 列の目安 |
|---|---|---|---|---|
| Compact | 〜759 | ハンバーガー（タイトルバーの ≡ で重ねて開く） | 〜760 | 1 列。一覧と詳細は片方だけ |
| Medium | 760〜1199 | 記号だけの細いレール（48） | 710〜1150 | 1〜2 列 |
| Expanded | 1200〜1599 | 題つきのペイン（232） | 970〜1370 | 2〜3 列。一覧と詳細を左右に |
| Wide | 1600〜 | 題つきのペイン（264） | 1340〜（中身は最大 1360 で中央） | 3〜4 列 |

確かめる幅：640 / 900 / 1280 / 1920（それぞれ Compact / Medium / Expanded / Wide）。

原則：

- **画面は SizeChanged を見ない**。`ResponsiveLayout.Bind(this, ApplyLayout)` で購読し、クラスが変わったときだけ Grid の行・列・余白・Visibility を差し替える（木を作り直さない）。外されたら自動で解除される。XAML の画面は `ResponsiveLayout.BindVisualStates(this)` で VisualState（"Compact" など）へ移す。
- 横スクロールを出さない。文字は折り返すか、`TextTrimming` で切る。固定幅の要素は Compact で入るか確かめる。
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
| `AdaptiveSplit` | 主と副の 2 面。広いと左右、`CollapseBelow` より狭いと `Stack`（積む）か `Single`（片方だけ。`ShowsSecondary`） | `Apply()` |
| `SurfaceCard.Create(content, tone, padding, tinted, variant)` / `SurfaceCard.Hero(content)` | カード。主役（Hero）は 1 画面に 1 枚 | — |
| `ScreenHeader` | 画面の見出し。Compact で要約を下へ回す | `SetTitle` / `SetSubtitle` |
| `MetricBlock(value, [unit,] caption, …)` | 数値＋説明。等幅数字 | `SetValue` / `SetCaption` |
| `StatusPill.Create(glyph, text, tone)` | 状態の印（glyph が null なら点） | `StatusPill.Set(pill, …)` |
| `IconTile.Create(glyph, tone, size)` | 見出しの記号のタイル | `IconTile.SetTone(tile, …)` |
| `SettingsRow.Create(…)` / `Header` | 設定の行。Compact で操作を下の段へ | — |
| `ContentUnavailablePanel` | 表示するものがないとき | — |
| `VirtualList<TItem>` | 何百件にもなる一覧（仮想化した ListView。行はコードで 1 回だけ組み、使い回す） | `Sync(items, key)` |
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

- タイトルバーまで伸ばした 48 の行に：戻る（戻れるときだけ）・ナビの開閉（≡）・ブランド・**接続コンソール**（点・アダプター名・状態・「ライブを表示」・接続／切断。ホームでは状態だけ）。狭いほど文字を省く（ツールチップに残る）。
- 画面の中の階層は `IBackNavigable` を実装すれば、タイトルバーの戻る・Alt+←・マウスの戻るで 1 段戻る。
- InfoBar（アダプター到来・前回の異常終了）はシェルが画面の上に出す。画面の中に同じ知らせを重ねない。
- ページの切り替えは `EntranceThemeTransition`（シェルがやる）。画面は自分で出入りの動きを付けない。
- ショートカット：Ctrl+1〜6、Ctrl+,、Alt+←。画面独自のもの（Ctrl+S、F5）は画面の `KeyboardAccelerators` に。

## 7. やってはいけないこと

- 色・余白・角丸・文字の大きさの直値（`new Thickness(26)`、`Color.FromArgb(...)`、`FontSize = 17`）。トークンを使う。足りなければトークンを足す。
- 画面ごとの `SizeChanged` で木を作り直すこと。幅で分けたいときは `ResponsiveLayout.Bind`。
- モデルの知らせのたびの `Children.Clear()`。1 秒に何度も来る知らせを束ねずに描くこと。
- Acrylic を画面に使うこと（重ねて開くナビ・メニュー・フライアウトなど、システムが使う所だけ）。`ThemeShadow` / `DropShadow` をカードに付けること（影はダイアログ・フライアウトなどシステムの重なりだけ）。
- `Storyboard` で常に動き続けるアニメーション（脈打つ点など）。動きは ThemeTransition・Composition の暗黙アニメーションで、出入りや配置の変化のときだけ。`Motion.IsEnabled` が false なら動かさない。
- 文字を足すとき `Strings/WindowsOnly.xcstrings` に日・英・西をそろえずに足すこと（足したら `scripts/ConvertXcstrings.cs` で .resw を作り直す）。
- 主の操作（`AccentButtonStyle`）を 1 画面に 2 つ以上置くこと。主役のカード（Hero）を 2 枚以上置くこと。

## 8. 画面ごとの狙いのレイアウト

今の作り（2026-11-11 時点）を踏まえた目標。各画面の担当が詰めてよいが、幅クラスでの振る舞いはこれに合わせる。

**ホーム**（作り直し済み・見本）
- 見出し（Display）→ 主役のカード（左：計器の輪＋アダプター名＋状態、右：主の操作）→ 今後の機能の帯。
- Compact：主役のカードの中を積み、操作は全幅。Medium 以上：左右。Wide：中身は 1360 で中央。

**ライブ表示**（`WindowsLiveTelemetryView`）
- 上に計器帯：エンジン回転数・車速を MetricHero で大きく（Compact は 2 つを縦、Medium 以上は横に 2 つ、Expanded 以上は応答 PID 数・状態も同じ帯に）。
- 分類のチップは Compact・Medium で横スクロールの 1 行、Expanded 以上で折り返し。値の格子は `AdaptiveGrid.Tiles()`（Compact 2 列〜Wide 6 列程度）。
- 値は要素を保持して `UiUpdateThrottle(minInterval: 100ms)` で差し替え。分類の切り替えでも作り直さず、`KeyedChildren` で入れ替える。

**履歴・詳細・分析**（`WindowsConnectionHistoryView`・`WindowsConnectionSessionDetailView`・`WindowsSessionLogAnalysisView`）
- アーカイブ（車両別）：`AdaptiveGrid.Cards()`。進行中のセッションは上に横長のカード。
- セッション一覧と詳細：`AdaptiveSplit`（`SecondaryFirst`、一覧 360〜420、`CollapseMode=Single`）。Expanded 以上で左右、Compact・Medium は片方だけ（詳細を開くと一覧を隠し、戻るで一覧へ）。今の 1100 のしきい値は幅クラスに置き換える。一覧は `VirtualList`。
- 分析：要約は `AdaptiveGrid.Tiles()`、グラフは `WideCards()`（Wide で 2 列）。グラフは高さ 320 を Compact で 240 に。読込み中の進み具合は値だけ差し替える（全体を作り直さない）。

**ガレージ・車両編集・PID 設定**（`WindowsGarageView`・`WindowsVehicleEditorView`・`WindowsVehiclePIDSettingsDialog`）
- ガレージ：要約タイル（`Tiles()`）→ 車両カードの格子（`Cards()`、Wide で 4 列）。写真の高さは Compact で 140。
- 車両編集：`AdaptiveSplit`（副＝写真 320、`Stack`）。Medium 以下は写真を上に積む（今の 760 のしきい値を置き換える）。入力のカードは `AdaptiveGrid`（最小 240）。
- PID 設定のダイアログ：固定の幅 600・高さ 380 をやめ、ダイアログの最大幅に合わせて伸縮。一覧は `VirtualList`（100 件超）。

**整備**（`WindowsMaintenanceView`・`WindowsMaintenanceEditorView`）
- タイムラインと詳細：`AdaptiveSplit`（`SecondaryFirst`、タイムライン 340、`Single`）。Expanded 以上で左右、Compact・Medium は片方だけ（今は 2 列固定で、640 では詳細が狭すぎる）。
- 編集：入力は `AdaptiveGrid`（最小 240）、写真は `Tiles()`。ツールバー（削除・取消・保存）は Compact で記号だけにする。

**設定**（`WindowsSettingsView`）
- Windows 11 の設定アプリの形を保つ。中身の最大幅は 1000（読みやすさ優先。`PageScaffold(1000)`）。
- 行（`SettingsRow`）は Compact で操作を下の段へ（済み）。Wide でも 2 列にはしない。グループの再描画は行単位で（`KeyedChildren`）。

**ログイン**（`WindowsLoginView`）
- 今の「横に紹介とサインインのパネル、狭いと積む」を保ち、独自の scale 計算と SizeChanged を幅クラスに置き換える：Compact は積む（パネル全幅・紹介は小さく）、Medium は積むか左右（高さ 560 未満なら積む）、Expanded 以上は左右。
- 直書きの色（グラデーション・円）をトークン（`RevTorqueBrandTileBrush`・`RevTorqueSignalBrush` など）に置き換える。ログインは常に暗い外観のまま。
