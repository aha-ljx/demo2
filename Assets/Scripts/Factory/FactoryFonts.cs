using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class FactoryFonts
{
    static TMP_FontAsset fallback;
    static readonly List<TMP_FontAsset> users = new List<TMP_FontAsset>();
    public static TMP_FontAsset Ensure(TMP_FontAsset font)
    {
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (fallback == null)
        {
            Font source = Resources.Load<Font>("NotoSansSC-UI");
            if (source != null) fallback = TMP_FontAsset.CreateFontAsset(source);
        }
        if (font != null && fallback != null && font != fallback)
        {
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
            if (!users.Contains(font)) users.Add(font);
        }
        return font;
    }
    public static void Release()
    {
        foreach (var font in users) if (font != null) font.fallbackFontAssetTable?.Remove(fallback);
        users.Clear();
        if (fallback != null)
        {
            foreach (var texture in fallback.atlasTextures) if (texture != null) Object.Destroy(texture);
            Object.Destroy(fallback.material); Object.Destroy(fallback);
        }
        fallback = null;
    }
}
