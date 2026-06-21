# event — プログラミングイベント自律収集

Claude（`web_search` + 構造化出力）で、C# / .NET やプログラミング関連の直近イベントを収集し、
Markdown にまとめて差分を記録するプロジェクト。

`idea-summary` リポジトリから責務分離のため移行（origin: idea-summary #2 / commit `17d61a4`）。

## 構成

- `DESIGN.md` — プロトタイプ設計（スコープ・データ構造・処理フロー・TODO）
- `config/` — 収集テーマ（`themes.md`）・参加ログ（`participated.md`）
- `src/EventCollector/` — 収集スクリプト（C# / .NET 8）
- `events.md` / `data/events.json` / `runs/` — 実行で生成される出力

## 実行

```bash
# リポジトリ直下から
ANTHROPIC_API_KEY=... dotnet run --project src/EventCollector
```

出力先は `EVENTS_DIR` 環境変数で上書き可（既定はリポジトリ直下）。

## 状態

コア収集部分まで実装済み。通知（Discord/Gmail）・GitHub Actions（cron）・自動コミット・
テーマ自律拡張は今後の Issue で対応する（`DESIGN.md` の TODO 参照）。
