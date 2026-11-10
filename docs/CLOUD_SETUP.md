# iCloud（CloudKit）と Sign in with Apple の準備（開発者向け）

Windows 版が iOS 版と同じデータ（車両・整備記録・接続セッション・設定・ログアウト通知）を同期するための準備です。
**この作業をしなくてもアプリは動きます**（設定がなければ、アカウントなしのローカルモードになり、同期しません）。

秘密の値（API トークンなど）はリポジトリに入れないでください。`appsettings.Local.json` は `.gitignore` 済みです。

## 1. 全体の流れ

Windows ではサインインが 2 回必要です（PROGRESS.md の「同期の設計メモ」）。

| 手順 | 何のため | 戻り先 |
|---|---|---|
| ① Sign in with Apple（Web） | iOS と同じユーザー ID（`sub`）を得る。データのレコード名はこの ID の SHA-256 から作る | `https://<あなたのドメイン>/.../apple-callback.html` → `revtorque-insight://auth/apple` |
| ② CloudKit の Web サインイン | private database を読み書きする `ckWebAuthToken` を得る（iPhone と同じ Apple ID で iCloud にサインイン） | `https://<あなたのドメイン>/.../cloudkit-callback.html` → `revtorque-insight://auth/cloudkit` |

どちらもブラウザーで行い、開発者の https のページから、カスタム URL スキーム `revtorque-insight` でアプリへ戻します。
アプリはプロトコルでの起動を受けたら `AuthorizationCallbackBroker.TryDeliver(uri)` を呼びます。

## 2. Sign in with Apple の Services ID を作る

1. [Apple Developer](https://developer.apple.com/account/resources/identifiers/list/serviceId) → Certificates, Identifiers & Profiles → Identifiers → 右上の絞り込みで **Services IDs** → ＋。
2. Description（例：RevTorque Insight Windows）、Identifier（例：`Ryokugyoku.ProjectZD8.web`）。これが `client_id`（設定の `Apple:ServicesId`）です。
3. 作った Services ID を開き、**Sign in with Apple** にチェック → Configure。
   - **Primary App ID**：iOS 版の App ID（`Ryokugyoku.ProjectZD8` の App ID）。**同じ Team・同じ主 App ID にしないと、iOS と違うユーザー ID になります。**
   - **Domains and Subdomains**：コールバックのページを置くドメイン（例：`example.com`）。
   - **Return URLs**：`https://example.com/revtorque/apple-callback.html` のような **https** の URL（localhost・IP アドレス・http は使えません）。これが設定の `Apple:RedirectUri` です。
4. Save → Continue → Save。

アプリはスコープ（名前・メールアドレス）を要求しません（iOS 版と同じ）。そのため Apple の規則で `response_mode=fragment` が使え、
戻り先は静的なページで足ります（スコープを要求すると `form_post` が必須になり、サーバーが要ります）。
`code` は使わず、`id_token` を Apple の公開鍵（`https://appleid.apple.com/auth/keys`）で検証して `sub` を取り出します（client secret は不要）。

## 3. コールバックのページを置く

`docs/web/apple-callback.html` と `docs/web/cloudkit-callback.html` を、上のドメインの https で公開します（GitHub Pages などの静的ホスティングで足ります）。

- `apple-callback.html`：Apple が付けた `#code=…&id_token=…&state=…` を `revtorque-insight://auth/apple?…` へ転送します。
  フラグメントはサーバーへ送られないので、ホスティングのログにも残りません。
- `cloudkit-callback.html`：CloudKit が付けた `?ckWebAuthToken=…` を `revtorque-insight://auth/cloudkit?…` へ転送します。
  **クエリーはホスティングのアクセスログに残ることがあります**。ログを残さない（または短期間で消す）ホスティングを選んでください。
- どちらのページも、アプリが開かないときのために「アプリを開く」ボタンを出します。

## 4. CloudKit の API トークンを作る

1. [CloudKit Console](https://icloud.developer.apple.com/) → コンテナー **iCloud.Ryokugyoku.ProjectZD8** → 環境（まず **Development**）→ **Tokens & Keys**（API Access）→ **API Tokens** → ＋（New API Token）。
2. Name：例 `windows`。
3. **Sign In Callback**：**URL Redirect** を選び、`https://example.com/revtorque/cloudkit-callback.html` を入れます。
   - CloudKit はサインインの後、この URL に `?ckWebAuthToken=<トークン>` を付けて戻します（Apple の資料）。
   - カスタム URL スキーム（`revtorque-insight://auth/cloudkit`）を直接入れられるなら、ページなしで戻れます（**未確認**。入れられなければ https のページを使ってください）。
   - 「Post Message」は Web ページ（CloudKit JS）用なので使いません。
4. Allowed Origins：既定のまま（Any Domain）か、上のドメイン。
5. 作ったトークンの値が設定の `CloudKit:ApiToken` です。**API トークンは環境ごとに別です**（Development と Production で別々に作ります）。

Web 認証トークン（ckWebAuthToken）について（Apple の資料より）：

- 既定で 30 分有効。サインイン画面で「サインインしたままにする」を選ぶと 2 週間。
- 1 往復ごとに新しいトークンが応答で返り、古いものは使えなくなります。アプリは応答の JSON の `ckWebAuthToken`／`ckSession` と同名のヘッダーを見て保存し直しますが、
  **実際にどこに入るかは資料になく、未確認**です。期限切れや入れ替えの失敗では 421 になり、アプリは再サインインを求めます。
- アプリはトークンを Windows の資格情報マネージャー（使えなければ DPAPI で暗号化したファイル）に保存します。

## 5. スキーマ（レコードの種類）を用意して本番に反映する

iOS 版がすでに使っている種類（VehicleCatalog、MaintenanceCatalog、ConnectionSessionMetadata、ConnectionSessionRawLog、
ConnectionSessionMacReceipt、ConnectionSessionDeletion）は、そのまま使います（`accountFingerprint` が **Queryable** であること）。

設定とログアウト通知を同期するため、次の 2 つを追加します（PROGRESS.md の「iOS 版の CloudKit 化の設計案」）。

| レコードの種類 | レコード名 | フィールド |
|---|---|---|
| `AccountSettings` | `account-settings-<SHA-256(ユーザー ID)>` | `settingsData`（Bytes。キーを並べ替えた JSON）、`updatedAt`（Date/Time） |
| `AccountSessionRevocation` | `session-revocation-<SHA-256(ユーザー ID)>` | `marker`（String。大文字の UUID）、`revokedAt`（Date/Time） |

- レコード名で直接読む（lookup）ので、インデックスは要りません。
- Development 環境では、最初の保存でスキーマが自動で作られます。CloudKit Console → Schema で確かめてください。
- **Production へ反映**：CloudKit Console → **Deploy Schema Changes…** で Development のスキーマを Production へ反映します。反映しないと、App Store／TestFlight の iOS 版と Windows 版（production）では保存に失敗します。
- iOS 版もこの 2 つのレコードを読み書きするように直す必要があります（ProjectZD8 リポジトリ側の作業。未着手）。それまでは、Windows と iOS の間で設定とログアウト通知は同期しません（運転データは同期します）。

## 6. 設定の置き場所

リポジトリのルートの `appsettings.Local.example.json` を `appsettings.Local.json` という名前でコピーし、値を入れます。

```json
{
  "Apple":    { "ServicesId": "Ryokugyoku.ProjectZD8.web", "RedirectUri": "https://example.com/revtorque/apple-callback.html" },
  "CloudKit": { "ContainerId": "iCloud.Ryokugyoku.ProjectZD8", "Environment": "development", "ApiToken": "<API トークン>" },
  "CallbackScheme": "revtorque-insight"
}
```

- 読み込みは `CloudServiceConfigurationLoader.Load(path)`。どのパスを渡すかはアプリの組み立てが決めます（実行ファイルと同じフォルダーを想定）。
- 環境変数でも渡せます（ファイルより優先）：`PROJECTZD8_APPLE_SERVICES_ID`、`PROJECTZD8_APPLE_REDIRECT_URI`、`PROJECTZD8_CALLBACK_SCHEME`、
  `PROJECTZD8_CLOUDKIT_CONTAINER_ID`、`PROJECTZD8_CLOUDKIT_ENVIRONMENT`、`PROJECTZD8_CLOUDKIT_API_TOKEN`。
- `Environment` は、同期したい iOS 版に合わせます。Xcode から実行した iOS 版は **development**、TestFlight／App Store の iOS 版は **production** です（データは環境ごとに別です）。
- Services ID・リダイレクト先・API トークンのどれかが欠けると、アカウントなしのローカルモードになります。

## 7. アプリ側の登録（組み立て・パッケージ）

- `Package.appxmanifest` にプロトコル `revtorque-insight` を登録します（`<uap:Extension Category="windows.protocol"><uap:Protocol Name="revtorque-insight" /></uap:Extension>`）。
- ブラウザーから戻ると新しいインスタンスが起動するので、単一インスタンスにして（`AppInstance.FindOrRegisterForKey` → `RedirectActivationToAsync`）、
  起動中のインスタンスで `AuthorizationCallbackBroker.TryDeliver(uri)` を呼びます。

## 8. 確かめられていないこと

実際の Apple / CloudKit にはつないでいません（テストは偽の HTTP 応答と、Swift の規則から手で書いた JSON で行っています）。最初につないだときに次を確かめてください。

- 回転した ckWebAuthToken が応答のどこに入るか（JSON のキーかヘッダーか）。
- CloudKit Console の Sign In Callback にカスタム URL スキームを入れられるか。
- `users/caller` を public database のパスで呼ぶこと、`records/modify` の `update`（指定したフィールドだけ更新）と `forceReplace` の動き。
- 資産のアップロード（`assets/upload` → 返った URL へ POST → `singleFile` をフィールドの値に入れる）が iOS の CKAsset と同じに読めること。
- Web の Sign in with Apple の `sub` が iOS の `credential.user` と一致すること（同じ Team・同じ主 App ID なら一致するはず）。
