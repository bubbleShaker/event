# 実装概要: GitHub Actions 定期実行＆自動コミット（#3）

収集を定期実行し、生成物に差分があれば自動コミットする CI を追加した。

## やったこと

- `.github/workflows/collect-events.yml` を追加。
  - `schedule`（毎日 00:00 UTC）+ `workflow_dispatch`（手動実行）
  - `actions/checkout` → `actions/setup-dotnet`(8.0.x) → `dotnet run`（収集→md/json生成→Discord通知）→ 自動コミット
- `events.md` / `data/` / `runs/` に差分がある時のみ `github-actions[bot]` がコミット＆push。

## 設計判断

- **Secrets で機密注入**: `ANTHROPIC_API_KEY`（必須）/ `DISCORD_WEBHOOK_URL`（任意）を Actions Secrets から渡す。リポジトリにもコードにも秘匿情報を置かない。
- **`permissions: contents: write`**: 既定の `GITHUB_TOKEN` で同一リポジトリへ push するための最小権限。
- **`concurrency`**: 多重起動を抑止し、自律ループの暴走・競合を防ぐ。
- **差分ガード**: `git diff --cached --quiet` で無駄なコミットを避ける。
- **ループ防止**: ワークフローは `schedule` / `dispatch` のみで起動し、自身の push では起動しない。

## レビュー観点

- セキュリティ: 秘匿情報は Secrets 管理、最小権限（contents: write）。
- 暴走対策: concurrency と差分ガード。実 API 課金が発生するため頻度は cron で調整可能。
- YAML 構文を検証済み。

## 運用メモ

- 初回はユーザーが `gh secret set ANTHROPIC_API_KEY` 等で Secrets を登録する必要がある（README 参照）。
- まず `workflow_dispatch`（手動）で1回動かし、収集・通知・自動コミットを確認してから cron 運用に乗せるのが安全。

## 残課題

Gmail 通知 / テーマ自律拡張 / pause_turn 継続ループ（`DESIGN.md` の TODO 参照）。
