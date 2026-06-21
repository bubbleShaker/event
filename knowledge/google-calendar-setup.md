# Google カレンダー連携 セットアップ手順（Workload Identity Federation・鍵レス）

収集したイベントを Google カレンダーへ自動登録するための初回セットアップ。
組織ポリシーでサービスアカウント鍵の発行が禁止されているため、**鍵を作らない**
Workload Identity Federation（WIF）で GitHub Actions から GCP 認証する。

> 仕組み: GitHub Actions は実行ごとに「自分は bubbleShaker/event の workflow だ」という
> 署名付きトークン（OIDC）を持つ。GCP 側で「このトークンを信頼してこのサービスアカウントとして
> 振る舞ってよい」と設定（＝WIF）すると、**鍵ファイルなし・その場限りの短期credentials**で書き込める。
> サービスアカウント自体は作るが、鍵はダウンロードしない。カレンダーへはそのサービスアカウントを
> **共有**して編集権限を渡す。

以下は `gcloud` で進める。先に `gcloud auth login` と対象プロジェクトの選択を済ませておく。

---

## 0. 変数を決める

```bash
PROJECT_ID="あなたのプロジェクトID"
PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"
POOL_ID="github-pool"
PROVIDER_ID="github-provider"
SA_NAME="event-writer"
SA_EMAIL="${SA_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
REPO="bubbleShaker/event"
```

## 1. API を有効化

```bash
gcloud services enable \
  calendar-json.googleapis.com \
  iamcredentials.googleapis.com \
  sts.googleapis.com \
  --project "$PROJECT_ID"
```

## 2. サービスアカウント作成（鍵は作らない）

```bash
gcloud iam service-accounts create "$SA_NAME" \
  --project "$PROJECT_ID" \
  --display-name "event collector calendar writer"
```

## 3. Workload Identity プール / プロバイダ作成

```bash
# プール
gcloud iam workload-identity-pools create "$POOL_ID" \
  --project "$PROJECT_ID" --location="global" \
  --display-name="GitHub Actions pool"

# GitHub OIDC プロバイダ。attribute-condition で「このリポジトリのみ」に限定（重要なセキュリティ境界）
gcloud iam workload-identity-pools providers create-oidc "$PROVIDER_ID" \
  --project "$PROJECT_ID" --location="global" \
  --workload-identity-pool="$POOL_ID" \
  --display-name="GitHub provider" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository=='${REPO}'"
```

> `attribute-condition` を付けないと**任意のリポジトリ**がこのサービスアカウントになりすませてしまう。
> 必ず自分のリポジトリに限定すること。

## 4. リポジトリにサービスアカウントの利用を許可（バインディング）

```bash
gcloud iam service-accounts add-iam-policy-binding "$SA_EMAIL" \
  --project "$PROJECT_ID" \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL_ID}/attribute.repository/${REPO}"
```

## 5. プロバイダのリソース名を控える（Secret に使う）

```bash
gcloud iam workload-identity-pools providers describe "$PROVIDER_ID" \
  --project "$PROJECT_ID" --location="global" \
  --workload-identity-pool="$POOL_ID" \
  --format='value(name)'
# => projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github-pool/providers/github-provider
```

## 6. 専用カレンダーを作成してサービスアカウントに共有

1. https://calendar.google.com/ で「他のカレンダー → ＋ → 新しいカレンダーを作成」（例 `収集イベント`）。
2. そのカレンダーの設定 → 「特定のユーザーやグループと共有する」→ **ユーザーを追加** に `$SA_EMAIL` を入力。
3. 権限は「**予定の変更権限**」。
4. 同設定の「カレンダーの統合 → カレンダー ID」を控える（`xxxx@group.calendar.google.com`）。

## 7. GitHub Secret を登録

```bash
# WIF プロバイダのリソース名（第5章の出力）
gcloud iam workload-identity-pools providers describe "$PROVIDER_ID" \
  --project "$PROJECT_ID" --location="global" \
  --workload-identity-pool="$POOL_ID" --format='value(name)' \
  | gh secret set GCP_WIF_PROVIDER --repo "$REPO"

# サービスアカウントのメール
printf '%s' "$SA_EMAIL" | gh secret set GCP_SERVICE_ACCOUNT --repo "$REPO"

# 登録先カレンダー ID（第6章で控えた値）。実行後に貼り付ける
gh secret set GOOGLE_CALENDAR_ID --repo "$REPO"
```

確認:

```bash
gh secret list --repo bubbleShaker/event
# GCP_WIF_PROVIDER / GCP_SERVICE_ACCOUNT / GOOGLE_CALENDAR_ID が並べばOK
```

## 8. 実機検証

```bash
gh workflow run collect-events --repo bubbleShaker/event
```

- 「Authenticate to Google Cloud」ステップが成功し、「Collect events」ログに
  `Google カレンダーに N 件登録しました。` が出れば成功。
- 専用カレンダーに未来イベントが終日予定で並ぶ。再実行しても重複しない（決定的 ID で更新）。

### うまくいかない時

| 症状 | 原因の候補 |
|------|-----------|
| auth ステップで `permission denied` / `unauthorized_client` | バインディング（第4章）の repo 指定ミス、または attribute-condition と不一致 |
| `... 未設定/対象なしのためスキップ` | `GOOGLE_CALENDAR_ID` 未登録、または対象が全て日付不明（TBD） |
| 403（カレンダー権限） | カレンダー共有が「予定の変更権限」になっていない／`$SA_EMAIL` の指定ミス |
| 404 | `GOOGLE_CALENDAR_ID` の値違い |

---

## Secret まとめ

| Secret | 取得元 |
|--------|--------|
| `GCP_WIF_PROVIDER` | 第5章のプロバイダリソース名 |
| `GCP_SERVICE_ACCOUNT` | サービスアカウントのメール（`$SA_EMAIL`） |
| `GOOGLE_CALENDAR_ID` | 第6章のカレンダー設定「カレンダー ID」 |

> ローカルで試す場合のみ、鍵 JSON を持っているなら `GOOGLE_CALENDAR_CREDENTIALS`（JSON 中身）＋
> `GOOGLE_CALENDAR_ID` でも動く（コードは鍵 JSON / ADC の両対応）。CI は鍵レスの WIF を使う。
