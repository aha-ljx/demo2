using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The level clock uses the same scaled seconds as module animations and transport.
[DefaultExecutionOrder(-100)]
public sealed class FactoryLevelSession : MonoBehaviour
{
    public const int RawCount = 10;
    public const float Duration = 600f;
    public static FactoryLevelSession Active { get; private set; }
    public static bool HasEnded => Active != null && Active.IsEnded;
    public static bool BlocksInput => Active != null && (Active.IsEnded || Time.frameCount <= Active.blockThroughFrame);
    public static bool CanSimulate => Active == null || Active.IsRunning && !FactoryTutorial.BlocksGameplay;
    public bool IsRunning { get; private set; }
    public bool IsEnded { get; private set; }
    public float RemainingSeconds { get; private set; } = Duration;
    public int ResultCount { get; private set; }
    public long ResultCost { get; private set; }
    public long ResultRevenue => (long)ResultCount * FinishedGoodsArea.PricePerProduct;
    public long ResultNet => ResultRevenue - ResultCost;
    public string ResultGrade => "生产完成";
    public Button StartButton { get; private set; }
    public GameObject Results { get; private set; }
    public RectTransform HUD { get; private set; }
    public RectTransform ResultCard { get; private set; }
    Canvas canvas;
    PlacementSystem placement;
    FactoryProduction factory;
    RawMaterialSupply supply;
    TextMeshProUGUI phase, cost, raw, clock, goal, startLabel, hint;
    readonly Image[] progress = new Image[RawCount];
    int blockThroughFrame = -1;
    string lastHint;
    float hintUntil;

    // Lexicographic comparison: no amount of profit can outweigh one delivered product.
    public static int CompareResults(int count, long net, int otherCount, long otherNet)
        => count != otherCount ? count.CompareTo(otherCount) : net.CompareTo(otherNet);

    public void Initialize(Canvas target, PlacementSystem builder, FactoryProduction production, RawMaterialSupply materials)
    {
        Active = this; canvas = target; placement = builder; factory = production; supply = materials;
        // Replace the older overlapping status panels with one level header.
        foreach (string name in new[] { "ProductionStatus", "EquipmentCost" })
        {
            var panel = canvas.transform.Find(name);
            if (panel != null) panel.gameObject.SetActive(false);
        }
        HUD = FactoryGuideUI.Box(canvas.transform, "LevelOneHUD", Vector2.zero, new Vector2(1840, 176), FactoryGuideUI.Background);
        HUD.anchorMin = HUD.anchorMax = new Vector2(.5f, 1);
        HUD.pivot = new Vector2(.5f, 1); HUD.anchoredPosition = new Vector2(0, -16);
        HUD.GetComponent<Image>().raycastTarget = true;
        // One coordinate space for status and actions: everything scales together.
        phase = FactoryGuideUI.Text(HUD, "Phase", "", new Vector2(-660, 52), new Vector2(460, 36), 22, FactoryGuideUI.Mint);
        cost = FactoryGuideUI.Text(HUD, "Cost", "", new Vector2(-660, 8), new Vector2(460, 40), 25, FactoryGuideUI.Ink);
        raw = FactoryGuideUI.Text(HUD, "Raw", "", new Vector2(-660, -28), new Vector2(460, 28), 18, FactoryGuideUI.Muted);
        clock = FactoryGuideUI.Text(HUD, "Clock", "", new Vector2(-40, 44), new Vector2(660, 52), 34, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        goal = FactoryGuideUI.Text(HUD, "Goal", "", new Vector2(-40, -4), new Vector2(660, 34), 22, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        FactoryGuideUI.Box(HUD, "HintDivider", new Vector2(0, -44), new Vector2(1780, 1), FactoryGuideUI.CardColor);
        var hintBar = FactoryGuideUI.Box(HUD, "InteractionHint", new Vector2(0, -65), new Vector2(1780, 28), Color.clear);
        hint = FactoryGuideUI.Text(hintBar, "Hint", "", Vector2.zero, new Vector2(1760, 28), 19, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        var equipmentInfo = canvas.transform.Find("EquipmentInfo") as RectTransform;
        if (equipmentInfo != null) equipmentInfo.anchoredPosition = new Vector2(-24, -220);
        for (int i = 0; i < progress.Length; i++)
            progress[i] = FactoryGuideUI.Box(HUD, "ProductProgress" + i, new Vector2(-310 + i * 60, -32), new Vector2(54, 5), FactoryGuideUI.CardColor).GetComponent<Image>();
        StartButton = FactoryGuideUI.Button(HUD, "StartProduction", "开始生产", new Vector2(750, 42), new Vector2(260, 48), FactoryGuideUI.Mint, BeginProduction, true);
        startLabel = StartButton.GetComponentInChildren<TextMeshProUGUI>();
        var tutorial = GetComponent<FactoryTutorial>();
        if (tutorial != null)
        {
            PlaceHeaderAction(tutorial.HelpButton, new Vector2(820, -15), new Vector2(140, 46));
            var helpLabel = tutorial.HelpButton.GetComponentInChildren<TextMeshProUGUI>();
            helpLabel.text = "教程 [H]"; helpLabel.fontSize = 21;
            if (tutorial.IsOpen) tutorial.Overlay.transform.SetAsLastSibling();
        }
        factory.Message("点击设备查看统计，冲床详情可上料；产量优先，同产量比较净收益；规划不计时");
        placement.InitializePlans(canvas, this);
        HUD.sizeDelta = new Vector2(1840, 112);
        phase.rectTransform.anchoredPosition = new Vector2(-660, 24);
        cost.rectTransform.anchoredPosition = new Vector2(-660, -19);
        raw.rectTransform.anchoredPosition = new Vector2(-330, -19);
        raw.rectTransform.sizeDelta = new Vector2(220, 30);
        clock.rectTransform.anchoredPosition = new Vector2(0, 2);
        goal.rectTransform.anchoredPosition = new Vector2(330, 2);
        goal.rectTransform.sizeDelta = new Vector2(300, 42);
        StartButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(750, 0);
        if (tutorial != null) tutorial.HelpButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-820, -76);
        HUD.Find("HintDivider").gameObject.SetActive(false);
        HUD.Find("InteractionHint").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -76);
        hint.rectTransform.sizeDelta = new Vector2(1320, 28);
        foreach (var item in progress) item.gameObject.SetActive(false);
        Refresh();
    }

    void PlaceHeaderAction(Button button, Vector2 position, Vector2 size)
    {
        var rect = button.GetComponent<RectTransform>();
        rect.SetParent(HUD, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.rectTransform.sizeDelta = size - new Vector2(16, 6);
    }

    public void BeginProduction()
    {
        if (IsRunning || IsEnded || FactoryTutorial.BlocksGameplay || RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay) return;
        IsRunning = true; blockThroughFrame = Time.frameCount + 1;
        // The click that starts the clock must not also place a model in the world.
        factory.Message("生产已开始：10分钟内尽量多入库，同产量比较净收益；生产期间不能撤回设备");
        Refresh();
    }

    void Update()
    {
        if (canvas == null) return;
        if (IsRunning && !FactoryTutorial.BlocksGameplay && !FactorySimulation.Running)
            Advance(Time.deltaTime);
        Refresh();
        if (IsEnded)
        {
            Time.timeScale = 0;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            FactoryGuideUI.Fit(ResultCard, canvas, new Vector2(1000, 560));
        }
    }

    internal void Advance(float simulationSeconds)
    {
        if (!IsRunning || simulationSeconds <= 0) return;
        RemainingSeconds = Mathf.Max(0, RemainingSeconds - simulationSeconds);
        if (RemainingSeconds <= 0) Finish();
    }
    internal void SimulationAdvance(long tick)
    {
        if (!IsRunning) return;
        RemainingSeconds = Mathf.Max(0, Duration - (float)(tick / 60d));
        if (RemainingSeconds <= 0) Finish();
    }

    void Finish()
    {
        if (IsEnded) return;
        IsRunning = false; IsEnded = true;
        supply.CloseFeedDialog();
        ResultCount = factory.Stored; ResultCost = placement.CurrentCost;
        Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Results = FactoryGuideUI.Overlay(canvas.transform, "LevelOneResults", new Color(.015f, .025f, .035f, .94f)).gameObject;
        ResultCard = FactoryGuideUI.Box(Results.transform, "ResultCard", Vector2.zero, new Vector2(1000, 560), FactoryGuideUI.PanelColor);
        FactoryGuideUI.Text(ResultCard, "Eyebrow", "第一关 · 结算", new Vector2(0, 220), new Vector2(880, 46), 23, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        FactoryGuideUI.Text(ResultCard, "Grade", "入库 " + ResultCount + " / 10", new Vector2(0, 135), new Vector2(880, 100), 62, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        string[] names = { "收入", "建设成本", "净收益" };
        string[] values = { ResultRevenue.ToString("N0") + " 元", ResultCost.ToString("N0") + " 元", ResultNet.ToString("N0") + " 元" };
        for (int i = 0; i < 3; i++)
        {
            var tile = FactoryGuideUI.Box(ResultCard, "Metric" + i, new Vector2(-294 + i * 294, 0), new Vector2(276, 124), FactoryGuideUI.CardColor);
            FactoryGuideUI.Text(tile, "Name", names[i], new Vector2(0, 31), new Vector2(254, 44), 21, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
            FactoryGuideUI.Text(tile, "Value", values[i], new Vector2(0, -22), new Vector2(254, 58), 30, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        }
        FactoryGuideUI.Button(ResultCard, "Retry", "重新规划", new Vector2(-300, -205), new Vector2(280, 64), FactoryGuideUI.Mint, Retry, true);
        FactoryGuideUI.Button(ResultCard, "SavePlan", "保存方案", new Vector2(0, -205), new Vector2(280, 64), FactoryGuideUI.CardColor, placement.Plans.OpenSave);
        FactoryGuideUI.Button(ResultCard, "ReturnMenu", "主菜单", new Vector2(300, -205), new Vector2(280, 64), FactoryGuideUI.CardColor, ReturnMenu);
        var tutorial = GetComponent<FactoryTutorial>();
        if (tutorial != null) tutorial.HelpButton.interactable = false;
        FactoryGuideUI.Fit(ResultCard, canvas, new Vector2(1000, 560));
    }

    void Refresh()
    {
        if (HUD == null) return;
        phase.text = IsEnded ? "第一关 · 已结算" : IsRunning ? "第一关 · 生产中" : "第一关 · 规划";
        cost.text = "成本  " + placement.CurrentCost.ToString("N0") + " 元";
        raw.text = "原料  " + supply.Remaining;
        if (lastHint != factory.Hint) { lastHint = factory.Hint; hintUntil = Time.unscaledTime + 6; }
        hint.text = Time.unscaledTime < hintUntil && lastHint != null && !lastHint.StartsWith("点击设备查看统计") && !lastHint.StartsWith("生产已开始") ? lastHint : "";
        int seconds = Mathf.CeilToInt(RemainingSeconds);
        clock.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        clock.color = IsRunning && seconds <= 60 ? FactoryGuideUI.Gold : FactoryGuideUI.Ink;
        goal.text = "入库  " + factory.Stored + " / 10";
        for (int i = 0; i < progress.Length; i++) progress[i].color = i < factory.Stored ? FactoryGuideUI.Mint : FactoryGuideUI.CardColor;
        StartButton.interactable = !IsRunning && !IsEnded;
        startLabel.text = IsEnded ? "已结束" : IsRunning ? "生产中" : "开始生产";
        FactoryGuideUI.Fit(HUD, canvas, new Vector2(1840, 112), 32);
    }

    public static bool PointerOverHUD(Vector2 point)
    {
        if (Active == null || Active.HUD == null) return false;
        var camera = Active.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Active.canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(Active.HUD, point, camera);
    }
    public void Retry() { MainMenuController.RequestFirstLevel(); Time.timeScale = 1; SceneManager.LoadScene("GameScene"); }
    public void ReturnMenu() { Time.timeScale = 1; SceneManager.LoadScene("MainMenu"); }
    void OnDestroy()
    {
        if (Active == this) { Active = null; Time.timeScale = 1; }
        if (HUD != null) Destroy(HUD.gameObject);
        if (Results != null) Destroy(Results);
    }
}

