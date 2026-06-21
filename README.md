# event — プログラミングイベント自律収集

Claude（`web_search` + 構造化出力）で、C# / .NET やプログラミング関連の直近イベントを収集し、
Markdown にまとめて差分を記録するプロジェクト。

`idea-summary` リポジトリから責務分離のため移行（origin: idea-summary #2 / commit `17d61a4`）。

## 構成

- `DESIGN.md` — プロトタイプ設計（スコープ・データ構造・処理フロー・TODO）
- `config/` — 収集テーマ（`themes.md`）・参加ログ（`participated.md`）
- `src/EventCollector/` — 収集スクリプト（C# / .NET 8）
- `events.md` / `data/events.json` / `runs/` — 実行で生成される出力

## 収集テーマのカスタマイズ

収集対象は `config/themes.md` を編集するだけで変えられます。
`- ` で始まる行が 1 テーマとして読み込まれ、自分の興味関心に合わせて自由に書き換えられます。

```markdown
# 収集テーマ

- Rust / WebAssembly 関連の勉強会
- 機械学習・LLM のカンファレンス（日本国内・オンライン）
- ゲーム開発・インディー開発のイベント
```

変更後に収集を実行すると、新しいテーマで `events.md` が更新されます。

## 実行

```bash
# リポジトリ直下から
ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector

# 差分発生時に Discord 通知したい場合は Webhook URL を併せて設定（任意）
DISCORD_WEBHOOK_URL=... ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector
```

- 出力先は `EVENTS_DIR` 環境変数で上書き可（既定はリポジトリ直下）。
- `DISCORD_WEBHOOK_URL` 未設定なら通知はスキップされる（収集は実行される）。

### Discord 通知の疎通確認（API 課金なし）

webhook が正しく届くかだけ確認したい場合は `--notify-test` を使う。
Claude API による収集をスキップするので `ANTHROPIC_API_KEY` は不要・課金も発生しない。

```bash
DISCORD_WEBHOOK_URL=... dotnet run --project src/EventCollector -- --notify-test
```

- 送信できたら「テスト通知を送信しました。」と表示して終了（exit 0）。
- `DISCORD_WEBHOOK_URL` 未設定なら送信せず exit 1。

### Google カレンダー登録（任意）

収集した未来イベントを Google カレンダーへ終日予定として登録できる（サービスアカウント認証）。
以下の環境変数が**両方**そろっていれば登録、未設定なら自動スキップ（収集は実行される）。

```bash
GOOGLE_CALENDAR_CREDENTIALS='{...サービスアカウントの鍵JSON...}' \
GOOGLE_CALENDAR_ID='xxxxx@group.calendar.google.com' \
ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector
```

| 変数 | 内容 |
|------|------|
| `GOOGLE_CALENDAR_CREDENTIALS` | サービスアカウントの鍵 JSON（中身そのもの） |
| `GOOGLE_CALENDAR_ID` | 登録先カレンダーの「カレンダー ID」 |

- イベントのキーから決定的な ID を作るため、毎回実行しても**重複登録されない**（無ければ作成・あれば更新）。
- 日付が `TBD` 等で解析できないイベントはスキップされる。
- GCP サービスアカウント作成・カレンダー共有・Secret 登録の手順は別途整備（設計は `research/google-calendar-sync.md`）。

## テスト

```bash
dotnet test tests/EventCollector.Tests
```

## 定期実行（GitHub Actions）

`.github/workflows/collect-events.yml` が週1回（月曜 00:00 UTC = 09:00 JST）収集を実行し、
生成物（`events.md` / `data/` / `runs/`）に差分があれば自動コミットする。
手動実行は Actions タブの「Run workflow」（`workflow_dispatch`）から可能。

事前に Secrets を登録する（値はリポジトリにもコードにも置かない）:

```bash
gh secret set ANTHROPIC_API_KEY --repo bubbleShaker/event       # 必須
gh secret set DISCORD_WEBHOOK_URL --repo bubbleShaker/event     # 任意（通知する場合）
```

※ scheduled run は実際に Claude API を呼ぶ（課金が発生する）。頻度は cron 行で調整する。

## 状態

コア収集 + **差分発生時の Discord 通知** + **GitHub Actions による定期実行＆自動コミット**まで実装済み。
Gmail 通知・テーマ自律拡張・`web_search` の pause_turn 継続ループは今後の Issue で対応する
（`DESIGN.md` の TODO 参照）。

## 免責

収集結果（`events.md` / `data/` / `runs/`）は Claude の Web 検索による自動生成であり、
開催日・場所・URL などの正確性・最新性は保証されません。重要な判断の前に必ず公式情報を確認してください。

## ライセンス

[MIT License](./LICENSE) © 2026 bubbleShaker
