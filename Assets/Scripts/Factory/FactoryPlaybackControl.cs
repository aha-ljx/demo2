using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Playback affects simulation time; camera/player input uses unscaled time.
public sealed class FactoryPlaybackControl : MonoBehaviour
{
    static readonly float[] Speeds = { 1f, 2f, 4f };
    static FactoryPlaybackControl active;
    int index;
    TextMeshProUGUI label;
    Canvas canvas;
    public Button FastForwardButton { get; private set; }
    public float Speed => Speeds[index];

    void OnEnable() { active = this; index = 0; Time.timeScale = 1f; Refresh(); }

    public void Initialize(Canvas target, TMP_FontAsset font)
    {
        canvas = target;
        var buttonObject = new GameObject("FastForwardButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(target.transform, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.right;
        rect.anchoredPosition = new Vector2(-24, 24); rect.sizeDelta = new Vector2(112, 56);
        FastForwardButton = buttonObject.GetComponent<Button>();
        FastForwardButton.targetGraphic = buttonObject.GetComponent<Image>();
        FastForwardButton.onClick.AddListener(CycleSpeed);
        var navigation = FastForwardButton.navigation; navigation.mode = Navigation.Mode.None;
        FastForwardButton.navigation = navigation;
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 5); textRect.offsetMax = new Vector2(-8, -5);
        label = textObject.GetComponent<TextMeshProUGUI>(); label.font = FactoryFonts.Ensure(font);
        label.fontSize = 23; label.color = FactoryGuideUI.Ink; label.alignment = TextAlignmentOptions.Center;
        label.color = FactoryGuideUI.Ink; label.raycastTarget = false;
        Refresh();
    }

    public void CycleSpeed()
    {
        if (FactoryTutorial.BlocksGameplay || FactoryLevelSession.BlocksInput || RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay) return;
        index = (index + 1) % Speeds.Length;
        Time.timeScale = Speed; Refresh();
    }

    void Refresh()
    {
        if (label != null) label.text = "× " + Speed.ToString("0");
        if (FastForwardButton != null)
            FastForwardButton.image.color = index == 0 ? FactoryGuideUI.PanelColor : FactoryGuideUI.Mint;
    }

    public static bool PointerOverButton(Vector2 point)
    {
        if (active == null || active.FastForwardButton == null || !active.FastForwardButton.isActiveAndEnabled) return false;
        Camera camera = active.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : active.canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(active.FastForwardButton.GetComponent<RectTransform>(), point, camera);
    }

    void OnDisable() { index = 0; Time.timeScale = 1f; Refresh(); if (active == this) active = null; }
    void OnDestroy() { if (FastForwardButton != null) Destroy(FastForwardButton.gameObject); }
}


