using System.Globalization;

namespace EventCollector.Calendar;

/// <summary>
/// テーマグループ名を Google カレンダーのイベント色（colorId <c>"1"</c>〜<c>"11"</c>）へ
/// 決定的に割り当てるパレット。
/// </summary>
/// <remarks>
/// グループ名を序数ソートした並び順のインデックスで色を選ぶ。これにより <b>11 グループまでは必ず別色</b>
/// になり「テーマごとに色を分ける」目的を確実に満たせる。
/// 単純なハッシュ（グループ名 → 1〜11）だと、9 グループ程度でも約 99% の確率でどこかが衝突し
/// 同色になってしまうため採らない。
/// 母集合は <c>themes.md</c> の全グループ（固定の並び）を想定する。登録するイベント集合ではなく
/// テーマ定義そのものを母集合にすることで、ある回にそのグループのイベントが 0 件でも色がずれず、
/// 実行ごとに既存イベントの色がちらつくのを防ぐ。グループの追加・改名でソート順が変われば
/// 色はずれうるが、それは意図的な設定変更時に限られる（色の固定指定は行わない方針）。
/// </remarks>
public sealed class ThemeColorPalette
{
    // Google カレンダーのイベント色 ID は "1"〜"11" の 11 種類。
    private const int PaletteSize = 11;

    private readonly IReadOnlyDictionary<string, string> _colorIdByGroup;

    private ThemeColorPalette(IReadOnlyDictionary<string, string> colorIdByGroup) =>
        _colorIdByGroup = colorIdByGroup;

    /// <summary>
    /// テーマグループ名の集合（<c>themes.md</c> の <c>## 見出し</c>一覧）から決定的なパレットを作る。
    /// グループ名を序数ソートし、<c>i</c> 番目に <c>colorId = (i % 11) + 1</c> を割り当てる。
    /// 空・空白のグループ名は無視する。
    /// </summary>
    public static ThemeColorPalette FromGroupNames(IEnumerable<string?> groupNames)
    {
        Dictionary<string, string> map = groupNames
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(g => g, StringComparer.Ordinal)
            .Select((group, index) => (group, colorId: ToColorId(index)))
            .ToDictionary(x => x.group, x => x.colorId, StringComparer.Ordinal);

        return new ThemeColorPalette(map);
    }

    /// <summary>
    /// グループ名に対応する colorId を返す。未知・<c>null</c> のグループは <c>null</c>（＝カレンダー既定色）。
    /// </summary>
    public string? ColorIdFor(string? group) =>
        !string.IsNullOrWhiteSpace(group) && _colorIdByGroup.TryGetValue(group, out string? colorId)
            ? colorId
            : null;

    // 11 色を超えるグループ数では色を巡回させる（12 番目以降は先頭色へ戻る）。
    private static string ToColorId(int index) =>
        ((index % PaletteSize) + 1).ToString(CultureInfo.InvariantCulture);
}
