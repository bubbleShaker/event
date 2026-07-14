using System.Globalization;
using EventCollector.Models;

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
/// 割り当てはグループ名の集合が同じである限り決定的（毎回同じ色）だが、集合や名前が変われば
/// ソート順の変化に伴い色がずれうる点は許容する（色の固定指定は行わない方針）。
/// </remarks>
public sealed class ThemeColorPalette
{
    // Google カレンダーのイベント色 ID は "1"〜"11" の 11 種類。
    private const int PaletteSize = 11;

    private readonly IReadOnlyDictionary<string, string> _colorIdByGroup;

    private ThemeColorPalette(IReadOnlyDictionary<string, string> colorIdByGroup) =>
        _colorIdByGroup = colorIdByGroup;

    /// <summary>
    /// イベント群に現れるグループ名から決定的なパレットを作る。
    /// グループ名を序数ソートし、<c>i</c> 番目に <c>colorId = (i % 11) + 1</c> を割り当てる。
    /// <see cref="EventItem.Group"/> を持たないイベントは既定色にするため対象外。
    /// </summary>
    public static ThemeColorPalette FromEvents(IEnumerable<EventItem> events)
    {
        Dictionary<string, string> map = events
            .Select(e => e.Group)
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
        group is not null && _colorIdByGroup.TryGetValue(group, out string? colorId) ? colorId : null;

    // 11 色を超えるグループ数では色を巡回させる（12 番目以降は先頭色へ戻る）。
    private static string ToColorId(int index) =>
        ((index % PaletteSize) + 1).ToString(CultureInfo.InvariantCulture);
}
