# Microsoft Store への公開（ベータ配布と自動リリース）

指定した人だけにベータ版を配るため、Microsoft Store の **プライベート対象ユーザー（Private audience）** で公開します。
最初の 1 回だけは Partner Center で手で提出し、2 回目からはタグを push すると GitHub Actions が自動で提出します。

- `.github/workflows/ci.yml`：main への push と pull request で、ビルドとテストを行います。
- `.github/workflows/release.yml`：タグ `v1.2.3` の push で、テスト → Store 用パッケージ（`.msixupload`、x64）の作成 → GitHub のリリース（pre-release）→ Store への提出を行います。
- `scripts/Set-PackageIdentity.ps1`：`Package.appxmanifest` のバージョンとパッケージ ID を書き換えます。

## 1. 提出の前に用意するもの（開発者の作業）

- [ ] **ロゴ**：`Assets/` の画像は、テンプレートの × 印のままです。Square150x150Logo・Square44x44Logo・Wide310x150Logo・StoreLogo・SplashScreen・LockScreenLogo を差し替えます（Visual Studio でマニフェストを開くと、「ビジュアル資産」から 1 枚の元画像で全サイズを作れます）。
- [ ] **プライバシーポリシーの URL**：マニフェストに `internetClient` があるため、提出に必須です。
- [ ] **ストアの掲載情報**：説明、スクリーンショット（1 枚以上）、年齢区分のアンケート。
- [ ] **テスターの Microsoft アカウント**（メールアドレス）。

## 2. Partner Center で一度だけ行うこと

1. Partner Center（アプリとゲーム）で **新しい製品 → MSIX または PWA アプリ** を作り、アプリ名を予約します。
2. 「製品管理 → 製品 ID」に出る次の値を、マニフェストに入れてコミットします。

   ```powershell
   ./scripts/Set-PackageIdentity.ps1 -IdentityName "<Package/Identity/Name>" -Publisher "<Package/Identity/Publisher（CN=...）>" -PublisherDisplayName "<Package/Properties/PublisherDisplayName>"
   ```

   Windows PowerShell 5.1 で実行ポリシーに止められるときは、`powershell -ExecutionPolicy Bypass -File scripts/Set-PackageIdentity.ps1 ...` で実行します。
   コミットしない場合は、GitHub の Variables の `STORE_IDENTITY_NAME`・`STORE_PUBLISHER`・`STORE_PUBLISHER_DISPLAY_NAME` に入れても構いません（リリースのときに書き換えます）。
   同じページの **Store ID（9N… の 12 文字）** は、後で `STORE_PRODUCT_ID` に入れます。
3. **既知のユーザー グループ** を作ります：アカウント設定 →「既知のユーザー グループ」（Customers → Known user groups）で、テスターのメールアドレスを登録します。
4. 最初のパッケージを作ります：GitHub の Actions →「Release」→「Run workflow」で、version に `1.0.0` を入れ、submit は外して実行します。終わったら、実行結果の Artifacts から `.msixupload` をダウンロードします。
5. 最初の提出を手で作ります：
   - **価格と使用可能状況 → 表示**：「プライベート対象ユーザー」を選び、3 のグループにチェックを入れます。
   - **プロパティ**：プライバシーポリシーの URL、カテゴリ。
   - **年齢区分**：アンケートに答えます。
   - **パッケージ**：4 の `.msixupload` をアップロードします。
   - **ストア掲載情報**：説明とスクリーンショット。
   - **提出オプション → 認定担当者向けのメモ**：例「OBD2 アダプターがなくても、設定画面でデモ用アダプターを選ぶと全画面を試せます。ログイン画面では『この PC で続ける』を選んでください。Apple のサインインは近日対応のため押せません。」
   - **制限付き機能（runFullTrust）の理由**：例「WinUI 3（Windows App SDK）のデスクトップアプリとして動くために必要です。」
6. 審査に通ると、テスターは `https://apps.microsoft.com/detail/<Store ID>` を開き、登録したアカウントで Store にサインインしていればインストールできます。

## 3. 自動提出の設定

1. Partner Center の **アカウント設定 → ユーザー管理 → Microsoft Entra アプリケーション** で、アプリを作成（または既存のものを追加）し、役割を **マネージャー** にします。キー（クライアント シークレット）を作ります。
2. 次の値を GitHub のリポジトリに登録します（Settings → Secrets and variables → Actions）。

   | 種類 | 名前 | 値 |
   |---|---|---|
   | Secret | `PARTNER_CENTER_TENANT_ID` | テナント ID |
   | Secret | `PARTNER_CENTER_SELLER_ID` | 販売者 ID（アカウント設定 → 法的情報 → 開発者） |
   | Secret | `PARTNER_CENTER_CLIENT_ID` | 1 のアプリのクライアント ID |
   | Secret | `PARTNER_CENTER_CLIENT_SECRET` | 1 のキー |
   | Variable | `STORE_PRODUCT_ID` | Store ID（9N…）。空のあいだは提出しない |
   | Variable | `STORE_FLIGHT_ID` | パッケージ フライトに出すときだけ。フライト ID |

   クライアント シークレットには有効期限があります。切れたら作り直して Secret を更新します。

## 4. リリースのしかた

```bash
git tag v1.0.1
git push origin v1.0.1
```

- パッケージのバージョンは `1.0.1.0` になります（Store の決まりで最後の桁は 0）。**提出のたびに前より大きいバージョンにします。**
- Store の API は、前回公開した提出を元に新しい提出を作り、パッケージだけを差し替えます。プライベート対象ユーザーの設定や掲載情報はそのまま引き継がれます。
- 審査の結果はワークフローでは待ちません。Partner Center で確かめます。
- 手で動かすときは Actions →「Release」→「Run workflow」（version を入れ、submit にチェック）。この場合、GitHub のリリースは作りません。

## 5. 一般公開とその後のベータ

- 一般公開：Partner Center で「表示」を「公開」に変えて提出します。テスターはそのまま正式版の更新を受け取ります。
- その後のベータは **パッケージ フライト** を使います。Partner Center でフライトを作ってグループを選び、フライト ID を `STORE_FLIGHT_ID` に入れると、タグの push がフライトへの提出になります。正式版を出すときは `STORE_FLIGHT_ID` を空に戻します。

## 注意

- **Release でもトリミングしません**（`PublishTrimmed=False`）。トリミングすると、起動直後に Microsoft.UI.Xaml.dll で落ちました（2026-11-10）。パッケージは約 80 MB（x64、.NET を同梱）です。
- 配布は今は **x64 だけ** です。ARM64 と x86 のビルドは確かめていません。
- `.msixupload` にシンボル（.appxsym）は入れていません。dotnet CLI では作れないためです（Store のクラッシュ解析でスタックが読みにくくなります）。
- `appsettings.Local.json` がリポジトリのルートにあるとパッケージに入るので、リリースのワークフローではエラーにしています。
