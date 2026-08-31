# AtCoder 収集源が「0 件」でも異常ではない

`AtCoderContestSource`（Issue #39 / PR #40 で追加）が **0 件収集** でも、多くの場合バグでも失敗でもない。
理由と切り分け方をここに残す。

## 何が起きるか

収集ログに次のように出ることがある。

```
[AtCoder / 競技プログラミング] 2 件収集   ← web_search 源（config/themes.md 由来）
[AtCoder] 0 件収集                        ← API 源（AtCoderContestSource）
収集源 成功 7 / 失敗 0。
```

`成功 7 / 失敗 0` なら **例外は起きていない**（失敗分離でスキップされたわけではない）。
API 源は正常にフェッチ・パースし、その上で「対象 0 件」と判断している。

## なぜ 0 件になるか

AtCoder には公式 REST API が無いため、事実上の標準である
[Kenkoooo AtCoder Problems の `contests.json`](https://kenkoooo.com/atcoder/resources/contests.json) を使っている。
このデータは **公式に告知済みのコンテストしか含まない**。AtCoder が先の開催をまだ告知していない、
または Kenkoooo 側のデータが追いついていない期間は、「今日以降のコンテスト」が存在しないため
`AtCoderContestSource`（未来のみ・約3か月以内でフィルタ）は 0 件を返す。

`events.md` に AtCoder 系の行（例 `awtf2026algo`, `AtCoder Conference 2026`）が出ていても、
それは **web_search 源が拾ったもの**であり、API 源の出力とは限らない。
API 源の出力は Theme = `競技プログラミング（AtCoder）` で見分けられる。

## バグか正常かの切り分け

`contests.json` を直接叩き、「now 以降の開催」が本当に 0 件かを確認する。

```bash
curl -s https://kenkoooo.com/atcoder/resources/contests.json -o /tmp/contests.json
python3 - <<'PY'
import json, datetime
data = json.load(open('/tmp/contests.json'))
now = datetime.datetime.now(datetime.timezone.utc).timestamp()
fut = sorted((c for c in data if c['start_epoch_second'] > now),
             key=lambda c: c['start_epoch_second'])
print("未来コンテスト件数:", len(fut))
last = max(data, key=lambda c: c['start_epoch_second'])
ls = datetime.datetime.fromtimestamp(last['start_epoch_second'], datetime.timezone.utc) + datetime.timedelta(hours=9)
print("データ内で最も未来の開催:", f"{ls:%Y-%m-%d}", last['id'])
PY
```

- 「未来コンテスト件数: 0」なら、**API 源の 0 件は正常**（データ側に未来分が無いだけ）。
- 未来分があるのに API 源が 0 件なら、そこで初めてコード側（フィルタ・日付変換）を疑う。

## 設計上の含み

この挙動があるため、`config/themes.md` の AtCoder グループから**コンテスト行だけ**を外し、
勉強会・解説会は web_search に残してある（PR #40）。OMC のコンテストも同じ理由で
web_search から外し、専用源（`OmcContestSource`, Issue #61）が確定情報で拾う。確定データにまだ無い先のイベント
（AWTF 等）は web_search が補完し、告知済みコンテストは API 源が確定情報で拾う、という役割分担になっている。

## 実測ログ（2026-07-08 の手動実行 / run 28905445370）

実行時点で `contests.json` の最新開催が `2026-07-07`（`awc0107`）で、実行時刻（JST 2026-07-08 朝）には
既に過去だったため、未来コンテストは 0 件。API 源も 0 件で、挙動は一致していた。

## 公式サイト源の追加（Issue #43）

Kenkoooo のラグで JSON 源が 0 件になる期間を埋めるため、公式サイト
`https://atcoder.jp/contests/?lang=ja` の「予定されたコンテスト」表(`id="contest-table-upcoming"`)を
直接 fetch する `AtCoderOfficialContestSource` を **JSON 源と併用**で追加した（依存追加なし・正規表現パース）。
2026-07-08 時点で JSON 源が拾えない `awtf2026algo` `abc466〜469` `arc224〜226` `ahc068/069` を公式源は取得できた。

### 併用時に「公式源だけ 0 件が続く」ときの切り分け

JSON 源とは失敗要因が違う（HTML 構造変化・アクセス遮断）。次を確認する。

```bash
# 1) そもそも 200 で取れているか（UA 無しでも現状 200。CDN が弾き始めると 403/503）
curl -s -o /tmp/ac.html -w "HTTP %{http_code}\n" "https://atcoder.jp/contests/?lang=ja"
# 2) 予定表に行があるか（0 なら告知済み未来が本当に無いだけ＝正常）
grep -c 'fixtime-full' /tmp/ac.html
```

- HTTP が 200 でなければアクセス遮断側。`AtCoderOfficialContestSource` の User-Agent 明示で復旧するか試す。
- 200 かつ `fixtime-full` が出るのにパース 0 件なら、表の HTML 構造が変わった可能性。正規表現
  （`fixtime-full'>`・`href="/contests/..."`・`</tbody>` 前提）を疑う。

### JSON 源との重複はなぜ起きない（実測）

`EventItem.Key` は「タイトル完全一致 + 正規化日付」。公式源はアンカーテキストを `HtmlDecode` し連続空白を
1つに畳むため、Kenkoooo の `title` と表記が揃う。装飾付きを含む実コンテストで突き合わせ済み：

| slug | 公式（正規化後）＝ Kenkoooo |
|---|---|
| arc222 | `第七回日本最強プログラマー学生選手権-予選-（AtCoder Regular Contest 222）` |
| abc462 | `CodeQUEEN 2026 予選 (AtCoder Beginner Contest 462)` |
| abc464 | `第七回日本最強プログラマー学生選手権～Advance～ -予選- （AtCoder Beginner Contest 464）` |

`Program.cs` で Kenkoooo を先、公式を後に並べているため、両方にあるコンテストは JSON 側が採用される。

### 残存エッジ

公式の予定表が一時的に崩れた表記を出すことがある（2026-07-08 時点で `arc224` が
`AtCoder Regular Contest-- 224` と `--` 付きで表示。Kenkoooo 反映後の正規表記と食い違えば
そのコンテストだけ重複表示されうる）。差分通知は自己修復するため実害は軽微だが、
継続的に片方だけ重複が出るなら、この表記揺れを疑う。

## OMC 源の追加（Issue #61）

OMC は web_search では個別コンテストが拾えず、日付 `TBD` のプレースホルダにしかならなかった
（`Online Math Contest (OMC)` / `Online Math Contest（OMC）` の 2 件が居座っていた）。
公式サイト `https://onlinemathcontest.com/contests` の「予定されたコンテスト」節
(`id="upcoming_contests"`)を直接 fetch する `OmcContestSource` を追加し、確定情報で収集する。
取得〜行の反復〜期間フィルタは AtCoder 公式源と共通の `HtmlContestTableSource` に集約した。

### 「OMC 源だけ 0 件が続く」ときの切り分け

OMC は**平常時でも予定が 1 件前後**しか無いため、AtCoder より「本当に 0 件」と「壊れて 0 件」の
区別が付きにくい。構造変化は例外にして失敗ログへ出す設計にしてあるが、手で確かめるなら次の通り。

```bash
# 1) そもそも 200 で取れているか
curl -s -o /tmp/omc.html -w "HTTP %{http_code}\n" https://onlinemathcontest.com/contests
# 2) 予定節に行があるか（0 なら告知済みの予定が本当に無いだけ＝正常）
python3 - <<'EOF'
h = open('/tmp/omc.html', encoding='utf-8').read()
i = h.index('upcoming_contests'); j = h.index('<tbody>', i); k = h.index('</tbody>', j)
print('予定表の行数:', h[j:k].count('<tr'))
EOF
```

- 行があるのに 0 件なら `OmcContestSource` が例外を投げる（`予定表に行があるがどれも解析できなかった`）。
  そのときは表の HTML 構造が変わっている。`ContestRegex`（`/contests/<slug>` の a 要素）・
  `StartRegex`（`<td>yyyy-MM-dd HH:mm:ss</td>`）・`DurationRegex`（`80 分`）を疑う。
- 所要時間は開始1時間前まで「開始1時間前に公開」と伏せられる回がある。そのときは終了時刻を持たず、
  カレンダーには既定の 1 時間で載る（異常ではない）。
