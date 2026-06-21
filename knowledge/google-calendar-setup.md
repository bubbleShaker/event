# Google カレンダー連携 セットアップ手順

収集したイベントを Google カレンダーへ自動登録するための、初回セットアップ手順。
コードは実装済み（#25）。ここでは **GCP の準備 → カレンダー共有 → Secret 登録 → 検証** を行う。

> 仕組みのおさらい: 「サービスアカウント」というロボット用 Google アカウントを作り、その鍵（JSON）で
> 認証して書き込む。個人のカレンダーへ書くには、対象カレンダーをそのサービスアカウントに
> **共有**して編集権限を渡す。無人実行（GitHub Actions）でも失効しないのが利点。

---

## 1. GCP プロジェクトと Calendar API

1. https://console.cloud.google.com/ を開く（Google アカウントでログイン）。
2. 画面上部のプロジェクト選択 → **新しいプロジェクト** を作成（名前は任意、例 `event-calendar`）。
3. 作成したプロジェクトを選択した状態で「**APIとサービス** → **ライブラリ**」へ。
4. `Google Calendar API` を検索して開き、**有効にする** を押す。

## 2. サービスアカウントと鍵 JSON

1. 「**APIとサービス** → **認証情報**」、または「**IAMと管理** → **サービス アカウント**」へ。
2. **サービス アカウントを作成**。
   - 名前は任意（例 `event-writer`）。**ロールは付けなくてよい**（カレンダー権限は次章の共有で渡すため）。
   - 「完了」で作成。
3. 作成したサービスアカウントを開き、**「キー」タブ → 鍵を追加 → 新しい鍵を作成 → JSON** を選ぶ。
   - JSON ファイルがダウンロードされる。**これが鍵。再発行はできても再ダウンロードは不可なので大切に保管**。
4. JSON 内の `client_email`（例 `event-writer@event-calendar.iam.gserviceaccount.com`）を控える。次章で使う。

> ⚠️ この JSON は秘密情報。リポジトリにコミットしない。後で GitHub Secret に入れる。

## 3. 専用カレンダーを作成して共有

1. https://calendar.google.com/ を開く。
2. 左の「**他のカレンダー** → ＋ → **新しいカレンダーを作成**」で専用カレンダーを作る（例 `収集イベント`）。
3. 作成したカレンダーの **設定** を開く。
4. 「**特定のユーザーやグループと共有する**」→ **ユーザーを追加** → 第2章で控えた `client_email` を入力。
   - 権限は「**予定の変更権限**」を選ぶ（作成・更新が必要なため）。
5. 同じ設定画面の「**カレンダーの統合**」にある「**カレンダー ID**」を控える。
   - 専用カレンダーだと `xxxxxxxx@group.calendar.google.com` の形式。

## 4. GitHub Secret に登録

リポジトリ直下で `gh` を使って登録する（値はリポジトリにもコードにも残さない）。

```bash
# カレンダー ID（第3章で控えた値）。実行すると入力を求められるので貼り付ける
gh secret set GOOGLE_CALENDAR_ID --repo bubbleShaker/event

# 鍵 JSON はファイルから流し込む（ダウンロードした JSON のパスを指定）
gh secret set GOOGLE_CALENDAR_CREDENTIALS --repo bubbleShaker/event < /path/to/service-account-key.json
```

登録確認:

```bash
gh secret list --repo bubbleShaker/event
# GOOGLE_CALENDAR_ID と GOOGLE_CALENDAR_CREDENTIALS が並べばOK
```

## 5. 実機検証

ワークフローを手動実行して、専用カレンダーに登録されるか確認する。

```bash
gh workflow run collect-events --repo bubbleShaker/event
```

- ジョブの「Collect events」ログに `Google カレンダーに N 件登録しました。` が出れば成功。
- Google カレンダーの専用カレンダーに、未来イベントが終日予定として並ぶ。
- もう一度実行しても**重複しない**（決定的 ID で更新されるため）。

### うまくいかない時

| 症状 | 原因の候補 |
|------|-----------|
| `... 未設定/対象なしのためスキップ` | Secret 未登録、または対象イベントの日付が全て不明（TBD） |
| 403 / 権限エラー | カレンダー共有の権限が「予定の変更権限」になっていない／`client_email` の指定ミス |
| 404（カレンダー無し） | `GOOGLE_CALENDAR_ID` の値が違う |
| 認証エラー | 鍵 JSON が壊れている／別プロジェクトの鍵 |

---

## 環境変数まとめ

| 変数 | 取得元 |
|------|--------|
| `GOOGLE_CALENDAR_CREDENTIALS` | 第2章でダウンロードした鍵 JSON の中身 |
| `GOOGLE_CALENDAR_ID` | 第3章のカレンダー設定「カレンダー ID」 |

ローカルで試す場合も同じ2変数を環境変数に入れて `dotnet run` すればよい（README 参照）。
