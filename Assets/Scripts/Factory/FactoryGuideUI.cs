using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Shared, code-drawn UI: no external images or fonts are required by the guide.
public static class FactoryGuideUI
{
    public static readonly Color Background = new Color(.92f, .925f, .915f, 1f);
    public static readonly Color PanelColor = new Color(.975f, .975f, .96f);
    public static readonly Color CardColor = new Color(.87f, .88f, .86f);
    public static readonly Color Ink = new Color(.085f, .09f, .08f);
    public static readonly Color Muted = new Color(.36f, .38f, .35f);
    public static readonly Color Mint = new Color(.94f, .98f, .03f);
    public static readonly Color Gold = new Color(.42f, .43f, .12f);

    public static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = root.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        if (name.EndsWith("Card") && size.y >= 200)
        {
            var rule = Box(rect, "HeaderRule", new Vector2(0, size.y * .5f - 2), new Vector2(size.x, 4), Mint);
            rule.GetComponent<Image>().raycastTarget = false;
        }
        return rect;
    }
    public static RectTransform Overlay(Transform parent, string name, Color color)
    {
        var rect = Box(parent, name, Vector2.zero, Vector2.zero,
            name == "Home" || name == "LevelSelection" || name == "LevelOneResults" ? Background : new Color(.1f, .11f, .09f, .38f));
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.GetComponent<Image>().raycastTarget = true;
        rect.SetAsLastSibling(); return rect;
    }
    public static TextMeshProUGUI Text(Transform parent, string name, string content, Vector2 position,
        Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var label = root.GetComponent<TextMeshProUGUI>();
        label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = size;
        label.font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset);
        label.text = content; label.fontSize = fontSize; label.color = color == Mint ? Ink : color;
        if (fontSize >= 30) label.fontStyle = FontStyles.Bold;
        label.alignment = alignment; label.raycastTarget = false;
        label.enableWordWrapping = true; label.overflowMode = TextOverflowModes.Truncate;
        return label;
    }
    public static Button Button(Transform parent, string name, string content, Vector2 position,
        Vector2 size, Color color, UnityAction action, bool primary = false)
    {
        var rect = Box(parent, name, position, size, primary || color == Mint ? Mint : CardColor);
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(.94f, .94f, .91f);
        colors.pressedColor = new Color(.78f, .8f, .69f);
        colors.disabledColor = new Color(.72f, .73f, .7f, .55f); button.colors = colors;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        if (action != null) button.onClick.AddListener(action);
        Text(rect, "Label", content, Vector2.zero, size - new Vector2(20, 6), 24,
            Ink, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
        return button;
    }
    public static void Key(Transform parent, string key, Vector2 position, float width = 74, Color? accent = null)
    {
        var cap = Box(parent, "Key_" + key, position, new Vector2(width, 62), CardColor);
        Box(cap, "KeyEdge", new Vector2(0, -28), new Vector2(width, 3), accent ?? Mint);
        Text(cap, "KeyGlyph", key, Vector2.zero, new Vector2(width - 8, 58), key.Length > 5 ? 20 : 28,
            Ink, TextAlignmentOptions.Center);
    }
    public static void Fit(RectTransform rect, Canvas canvas, Vector2 preferred, float margin = 64)
    {
        Vector2 available = canvas.GetComponent<RectTransform>().rect.size - Vector2.one * margin;
        float factor = Mathf.Min(1, available.x / preferred.x, available.y / preferred.y);
        rect.localScale = Vector3.one * Mathf.Max(.1f, factor);
    }
}
