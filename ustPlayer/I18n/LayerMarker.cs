namespace UstPlayer.I18n;

/// <summary>
/// 国际化层占位类型。Phase 3 在此落地 <c>Translator</c> 与 <c>TsCatalog</c>：
/// 直接解析 1.1.x 的 <c>i18n/*.ts</c>（XML），复用既有三语共 161 条翻译，
/// 避免重译；<c>pyside6-lupdate</c> 继续用于抽取新串。
/// </summary>
internal static class LayerMarker
{
}
