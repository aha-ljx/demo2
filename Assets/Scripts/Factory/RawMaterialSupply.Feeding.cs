using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class RawMaterialSupply
{
    static RawMaterialSupply activeFeedSupply;
    Canvas feedCanvas;
    GameObject feedOverlay;
    RectTransform feedCard;
    FactoryStation feedTarget;
    TextMeshProUGUI feedRange, feedAmount, feedTitle;
    CursorLockMode feedCursor;
    bool feedCursorVisible;
    int feedClosedThroughFrame = -1;
    public bool IsFeedDialogOpen => feedOverlay != null && feedOverlay.activeSelf;
    public Slider FeedSlider { get; private set; }
    public Button ConfirmFeedButton { get; private set; }
    public Button CancelFeedButton { get; private set; }
    public static bool BlocksGameplay => activeFeedSupply != null &&
        (activeFeedSupply.IsFeedDialogOpen || Time.frameCount <= activeFeedSupply.feedClosedThroughFrame);

    public void InitializeFeeding(Canvas canvas)
    {
        activeFeedSupply = this;
        feedCanvas = canvas;
        feedOverlay = FactoryGuideUI.Overlay(canvas.transform, "FeedQuantityOverlay", new Color(0, 0, 0, .3f)).gameObject;
        feedCard = FactoryGuideUI.Box(feedOverlay.transform, "FeedQuantityCard", Vector2.zero, new Vector2(620, 380), FactoryGuideUI.PanelColor);
        feedCard.GetComponent<Image>().raycastTarget = true;
        feedTitle = FactoryGuideUI.Text(feedCard, "Title", "上料", new Vector2(0, 132), new Vector2(560, 54), 30, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        feedRange = FactoryGuideUI.Text(feedCard, "Range", "", new Vector2(0, 76), new Vector2(560, 48), 20, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        feedAmount = FactoryGuideUI.Text(feedCard, "Amount", "", new Vector2(0, 18), new Vector2(560, 50), 30, FactoryGuideUI.Mint, TextAlignmentOptions.Center);
        var sliderRect = FactoryGuideUI.Box(feedCard, "QuantitySlider", new Vector2(0, -40), new Vector2(520, 44), Color.clear);
        sliderRect.GetComponent<Image>().raycastTarget = true;
        var track = FactoryGuideUI.Box(sliderRect, "Track", Vector2.zero, new Vector2(496, 8), FactoryGuideUI.CardColor);
        var fillArea = FactoryGuideUI.Box(sliderRect, "FillArea", Vector2.zero, new Vector2(496, 8), Color.clear);
        var fill = FactoryGuideUI.Box(fillArea, "Fill", Vector2.zero, Vector2.zero, FactoryGuideUI.Mint);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
        var handleArea = FactoryGuideUI.Box(sliderRect, "HandleArea", Vector2.zero, new Vector2(496, 44), Color.clear);
        var handle = FactoryGuideUI.Box(handleArea, "Handle", Vector2.zero, new Vector2(24, -8), FactoryGuideUI.Mint);
        handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.up;
        handle.GetComponent<Image>().raycastTarget = true;
        FeedSlider = sliderRect.gameObject.AddComponent<Slider>();
        FeedSlider.fillRect = fill; FeedSlider.handleRect = handle; FeedSlider.targetGraphic = handle.GetComponent<Image>();
        FeedSlider.minValue = 0; FeedSlider.wholeNumbers = true;
        FeedSlider.onValueChanged.AddListener(value => RefreshFeedAmount());
        var navigation = FeedSlider.navigation; navigation.mode = Navigation.Mode.None; FeedSlider.navigation = navigation;
        CancelFeedButton = FactoryGuideUI.Button(feedCard, "Cancel", "取消", new Vector2(-142, -124), new Vector2(240, 58), FactoryGuideUI.CardColor, CloseFeedDialog);
        ConfirmFeedButton = FactoryGuideUI.Button(feedCard, "Confirm", "确认上料", new Vector2(142, -124), new Vector2(240, 58), FactoryGuideUI.Mint, () => ConfirmFeed(), true);
        feedOverlay.SetActive(false);
    }

    public bool InspectStock()
    {
        if (FactoryTutorial.BlocksGameplay || FactoryLevelSession.BlocksInput || IsFeedDialogOpen || Held != null) return false;
        if (Remaining == 0) { production.Message("原料库存已取完"); return false; }
        production.Message("剩余原料 " + Remaining + " 块；直接点击需要上料的冲床，拖动滑块选择数量");
        return true;
    }

    public int FeedLimit(FactoryStation station)
    {
        if (station == null || station.Kind != 0) return 0;
        return Mathf.Min(Remaining, Mathf.Max(0, FactoryStation.RawInputCapacity - station.Input.Count - station.Incoming));
    }

    public bool OpenFeedDialog(FactoryStation station)
    {
        if (BlocksGameplay || Held != null || feedOverlay == null ||
            FactoryTutorial.BlocksGameplay || FactoryLevelSession.BlocksInput ||
            station == null || station.Kind != 0) return false;
        feedTarget = station;
        feedTitle.text = station.name + " · 上料";
        feedCursor = Cursor.lockState; feedCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        feedOverlay.SetActive(true); feedOverlay.transform.SetAsLastSibling();
        FeedSlider.minValue = 0; FeedSlider.maxValue = FeedLimit(station); FeedSlider.value = 0;
        RefreshFeedDialog();
        return true;
    }

    void Update()
    {
        if (!IsFeedDialogOpen) return;
        if (FactoryLevelSession.HasEnded || feedTarget == null)
        { CloseFeedDialog(); return; }
        RefreshFeedDialog();
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) CloseFeedDialog();
    }

    void RefreshFeedDialog()
    {
        FeedSlider.maxValue = FeedLimit(feedTarget);
        FeedSlider.value = Mathf.Clamp(FeedSlider.value, 0, FeedSlider.maxValue);
        feedRange.text = "剩余原料 " + Remaining + " 块  ·  冲床可再排队 " +
            Mathf.Max(0, FactoryStation.RawInputCapacity - feedTarget.Input.Count - feedTarget.Incoming) + " 块";
        RefreshFeedAmount();
        FactoryGuideUI.Fit(feedCard, feedCanvas, new Vector2(620, 380), 32);
    }

    void RefreshFeedAmount()
    {
        feedAmount.text = Mathf.RoundToInt(FeedSlider.value) + " / " + Mathf.RoundToInt(FeedSlider.maxValue) + " 块";
    }

    public int ConfirmFeed()
    {
        if (!IsFeedDialogOpen || FactoryLevelSession.BlocksInput || FactoryTutorial.BlocksGameplay) return 0;
        if (feedTarget == null)
        { CloseFeedDialog(); production.Message("冲床位置已改变，请重新选择上料目标"); return 0; }
        int count = Mathf.Clamp(Mathf.RoundToInt(FeedSlider.value), 0, FeedLimit(feedTarget));
        for (int i = 0; i < count; i++)
        {
            var part = stock[stock.Count - 1]; stock.RemoveAt(stock.Count - 1);
            part.SetGroundCollision(false); part.transform.rotation = Quaternion.identity;
            feedTarget.Enqueue(part);
        }
        RefreshStock(); Physics.SyncTransforms(); CloseFeedDialog();
        production.Message(count == 0 ? "本次上料数量为0，原料库存未改变" : "已向选中的冲床上料 " + count + " 块；剩余原料 " + Remaining + " 块");
        return count;
    }

    public void CloseFeedDialog()
    {
        if (!IsFeedDialogOpen) return;
        feedOverlay.SetActive(false); feedTarget = null;
        feedClosedThroughFrame = Time.frameCount + 1;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = FactoryLevelSession.HasEnded ? CursorLockMode.None : feedCursor;
        Cursor.visible = FactoryLevelSession.HasEnded || feedCursorVisible;
    }

    void OnDestroy()
    {
        CloseFeedDialog();
        if (activeFeedSupply == this) activeFeedSupply = null;
        if (feedOverlay != null) Destroy(feedOverlay);
    }
}

