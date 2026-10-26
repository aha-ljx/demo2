using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class FactoryStatistics
{
    Canvas statsCanvas;
    RawMaterialSupply statsSupply;
    GameObject detailOverlay, curvesOverlay;
    RectTransform detailCard, curvesCard;
    FactoryModuleStatistics selectedModule;
    TMP_Text detailName, detailState, detailRate, detailTotals, pageLabel, productLabel, noModules;
    readonly TMP_Text[] moduleTitles = new TMP_Text[4];
    readonly FactoryRateChart[] moduleCharts = new FactoryRateChart[4];
    FactoryRateChart productChart;
    Button feedAction, closeDetails, previousPage, nextPage;
    int curvePage, closedThroughFrame = -1;
    float refreshAt;
    CursorLockMode savedCursor;
    bool savedVisible;
    public Button StatisticsButton { get; private set; }
    public bool IsDetailOpen => detailOverlay != null && detailOverlay.activeSelf;
    public bool IsCurvesOpen => curvesOverlay != null && curvesOverlay.activeSelf;
    public static bool BlocksGameplay => Active != null &&
        (Active.IsDetailOpen || Active.IsCurvesOpen || Time.frameCount <= Active.closedThroughFrame);

    public void InitializeInterface(Canvas canvas, RawMaterialSupply supply)
    {
        statsCanvas = canvas; statsSupply = supply;
        StatisticsButton = FactoryGuideUI.Button(canvas.transform, "ProductionStatisticsButton", "生产统计", Vector2.zero,
            new Vector2(190, 56), FactoryGuideUI.CardColor, OpenCurves);
        var buttonRect = StatisticsButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(24, 24);
        detailOverlay = FactoryGuideUI.Overlay(canvas.transform, "EquipmentStatisticsOverlay", new Color(0, 0, 0, .45f)).gameObject;
        detailCard = FactoryGuideUI.Box(detailOverlay.transform, "EquipmentStatisticsCard", Vector2.zero, new Vector2(720, 480), FactoryGuideUI.PanelColor);
        detailCard.GetComponent<Image>().raycastTarget = true;
        detailName = FactoryGuideUI.Text(detailCard, "EquipmentName", "", new Vector2(0, 185), new Vector2(640, 60), 36, FactoryGuideUI.Mint, TextAlignmentOptions.Center);
        detailState = FactoryGuideUI.Text(detailCard, "CurrentState", "", new Vector2(0, 95), new Vector2(620, 110), 23, FactoryGuideUI.Ink);
        detailRate = FactoryGuideUI.Text(detailCard, "CurrentRate", "", new Vector2(0, -20), new Vector2(620, 100), 26, FactoryGuideUI.Ink);
        detailTotals = FactoryGuideUI.Text(detailCard, "Totals", "", new Vector2(0, -117), new Vector2(620, 48), 21, FactoryGuideUI.Muted);
        feedAction = FactoryGuideUI.Button(detailCard, "FeedPunch", "选择上料数量", new Vector2(-155, -182), new Vector2(280, 60), FactoryGuideUI.Mint, FeedSelected, true);
        closeDetails = FactoryGuideUI.Button(detailCard, "Close", "关闭", new Vector2(155, -182), new Vector2(280, 60), FactoryGuideUI.CardColor, CloseStatistics);
        detailOverlay.SetActive(false);

        curvesOverlay = FactoryGuideUI.Overlay(canvas.transform, "ProductionCurvesOverlay", new Color(0, 0, 0, .65f)).gameObject;
        curvesCard = FactoryGuideUI.Box(curvesOverlay.transform, "ProductionCurvesCard", Vector2.zero, new Vector2(1280, 900), FactoryGuideUI.PanelColor);
        curvesCard.GetComponent<Image>().raycastTarget = true;
        FactoryGuideUI.Text(curvesCard, "Title", "生产统计", new Vector2(0, 396), new Vector2(1140, 58), 32, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(curvesCard, "RateDefinition", "仿真时间", new Vector2(0, 344), new Vector2(1140, 42), 19, FactoryGuideUI.Muted);
        productLabel = FactoryGuideUI.Text(curvesCard, "ProductRateTitle", "", new Vector2(0, 292), new Vector2(1140, 40), 23, FactoryGuideUI.Mint);
        productChart = CreateChart(curvesCard, "Products", new Vector2(0, 175), new Vector2(1140, 190), FactoryGuideUI.Mint);
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -290 : 290, y = i < 2 ? -3 : -205;
            moduleTitles[i] = FactoryGuideUI.Text(curvesCard, "ModuleTitle" + i, "", new Vector2(x, y), new Vector2(550, 42), 21, FactoryGuideUI.Ink);
            moduleCharts[i] = CreateChart(curvesCard, "ModuleCurve" + i, new Vector2(x, y - 97), new Vector2(550, 150), i % 2 == 0 ? FactoryGuideUI.Mint : FactoryGuideUI.Gold);
        }
        noModules = FactoryGuideUI.Text(curvesCard, "NoModules", "放置加工设备或机械臂后，将在这里显示对应曲线。", new Vector2(0, -115), new Vector2(1100, 80), 26, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        previousPage = FactoryGuideUI.Button(curvesCard, "Previous", "上一页", new Vector2(-455, -411), new Vector2(190, 52), FactoryGuideUI.CardColor, () => { curvePage--; RefreshCurves(); });
        pageLabel = FactoryGuideUI.Text(curvesCard, "Page", "", new Vector2(-190, -411), new Vector2(300, 48), 20, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        nextPage = FactoryGuideUI.Button(curvesCard, "Next", "下一页", new Vector2(80, -411), new Vector2(190, 52), FactoryGuideUI.CardColor, () => { curvePage++; RefreshCurves(); });
        FactoryGuideUI.Button(curvesCard, "Close", "关闭统计", new Vector2(455, -411), new Vector2(220, 52), FactoryGuideUI.Mint, CloseStatistics, true);
        curvesOverlay.SetActive(false);
    }
    static FactoryRateChart CreateChart(Transform parent, string name, Vector2 position, Vector2 size, Color tint)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)); root.layer = parent.gameObject.layer; root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var chart = root.AddComponent<FactoryRateChart>(); chart.color = tint == FactoryGuideUI.Mint ? FactoryGuideUI.Ink : FactoryGuideUI.Gold; chart.Initialize(); return chart;
    }
    public bool OpenDetails(GameObject item)
    {
        var module = Find(item);
        if (module == null || module.Removed || detailOverlay == null || FactoryTutorial.BlocksGameplay ||
            FactoryLevelSession.BlocksInput || FactoryLocalAssistant.BlocksGameplay || RawMaterialSupply.BlocksGameplay || BlocksGameplay) return false;
        SaveCursor(); selectedModule = module; detailOverlay.SetActive(true); detailOverlay.transform.SetAsLastSibling();
        RefreshDetails(); return true;
    }
    public void OpenCurves()
    {
        if (curvesOverlay == null || FactoryTutorial.BlocksGameplay || RawMaterialSupply.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay || IsCurvesOpen) return;
        if (IsDetailOpen) CloseStatistics();
        SaveCursor(); curvesOverlay.SetActive(true); curvesOverlay.transform.SetAsLastSibling(); curvePage = 0; RefreshCurves();
    }
    void SaveCursor()
    {
        savedCursor = Cursor.lockState; savedVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    public void CloseStatistics()
    {
        if (!IsDetailOpen && !IsCurvesOpen) return;
        detailOverlay.SetActive(false); curvesOverlay.SetActive(false); selectedModule = null;
        closedThroughFrame = Time.frameCount + 1;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = FactoryLevelSession.HasEnded ? CursorLockMode.None : savedCursor;
        Cursor.visible = FactoryLevelSession.HasEnded || savedVisible;
    }
    void FeedSelected()
    {
        if (selectedModule == null || selectedModule.Root == null || selectedModule.Kind != 0) return;
        var station = selectedModule.Root.GetComponent<FactoryStation>();
        CloseStatistics(); statsSupply.OpenFeedDialog(station);
    }
    void RefreshDetails()
    {
        if (selectedModule == null || selectedModule.Root == null || selectedModule.Removed) { CloseStatistics(); return; }
        var module = selectedModule;
        detailName.text = module.Name;
        detailState.text = ShortState(module);
        feedAction.gameObject.SetActive(module.Kind == 0 && !FactoryLevelSession.HasEnded);
        closeDetails.GetComponent<RectTransform>().anchoredPosition = new Vector2(feedAction.gameObject.activeSelf ? 155 : 0, -182);
        float duration = Elapsed - module.CreatedAt;
        if (module.Kind == 3)
        {
            var arm = module.Root.GetComponent<FactoryArm>();
            detailRate.text = "移动速率  " + arm.RailSpeed.ToString("0.00") + " 单位/秒\n当前  " + module.MovementRate.ToString("0.00") + " 单位/秒";
            detailTotals.text = "";
        }
        else
        {
            var station = module.Root.GetComponent<FactoryStation>();
            detailRate.text = module.Rate.ToString("0.00") + " 件/分钟\n完成  " + module.Completed + " 件";
            detailTotals.text = "排队 " + station.Input.Count + "  ·  出料缓存 " + station.OutputCount + "/" + station.OutputCapacity;
        }
        FactoryGuideUI.Fit(detailCard, statsCanvas, new Vector2(720, 480), 32);
    }
    static string ShortState(FactoryModuleStatistics module)
    {
        if (FactoryLevelSession.HasEnded) return "已结束";
        if (FactoryLevelSession.Active != null && !FactoryLevelSession.Active.IsRunning) return "规划中";
        var station = module.Root.GetComponent<FactoryStation>();
        if (station != null)
        {
            if (!station.HasOutputSpace) return "出料缓存已满";
            if (station.Busy) return station.Kind == 0 ? "冲压中" : station.Kind == 1 ? "折弯中" : "加工 / 转运中";
            if (station.OutputCount > 0) return station.Kind == 1 ? "等待焊接" : "等待取件";
            return station.Incoming > 0 ? "送料中" : "待料";
        }
        var arm = module.Root.GetComponent<FactoryArm>();
        if (arm == null) return "待机";
        return arm.WaitingForWeld ? "等待焊接" : arm.WaitingForConveyor ? "等待放件" : arm.Holding ? "搬运中" : arm.Busy ? "执行任务" : "待机";
    }
    void RefreshCurves()
    {
        int pages = Mathf.Max(1, (Modules.Count + 3) / 4); curvePage = Mathf.Clamp(curvePage, 0, pages - 1);
        float from = Mathf.Max(0, Elapsed - 600), to = Mathf.Max(60, Elapsed);
        productLabel.text = "入库  " + ProductCount + " 件  ·  " + ProductRate.ToString("0.00") + " 件/分钟";
        productChart.Show(ProductHistory, from, to);
        for (int i = 0; i < 4; i++)
        {
            int index = curvePage * 4 + i; bool visible = index < Modules.Count;
            moduleTitles[i].gameObject.SetActive(visible); moduleCharts[i].gameObject.SetActive(visible);
            if (!visible) continue;
            var module = Modules[index];
            moduleTitles[i].text = module.Name + (module.Removed ? "（已撤回）" : "") + " · " + module.Unit;
            moduleCharts[i].Show(module.History, from, to);
        }
        noModules.gameObject.SetActive(Modules.Count == 0);
        pageLabel.text = (curvePage + 1) + " / " + pages + " 页  ·  " + Modules.Count + " 台设备";
        previousPage.interactable = curvePage > 0; nextPage.interactable = curvePage + 1 < pages;
        FactoryGuideUI.Fit(curvesCard, statsCanvas, new Vector2(1280, 900), 32);
    }
    void UpdateInterface()
    {
        if (statsCanvas == null) return;
        if (FactoryLevelSession.HasEnded && IsDetailOpen) CloseStatistics();
        // Keep the history available over the result screen as well.
        if (FactoryLevelSession.HasEnded && !IsCurvesOpen && !FactoryPlanLibrary.BlocksGameplay) StatisticsButton.transform.SetAsLastSibling();
        if ((IsDetailOpen || IsCurvesOpen) && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))) CloseStatistics();
        if (Time.unscaledTime < refreshAt) return;
        refreshAt = Time.unscaledTime + .2f;
        if (IsDetailOpen) RefreshDetails();
        if (IsCurvesOpen) RefreshCurves();
    }
    public static bool PointerOverStatisticsButton(Vector2 point)
    {
        if (Active == null || Active.StatisticsButton == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(Active.StatisticsButton.GetComponent<RectTransform>(), point,
            Active.statsCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Active.statsCanvas.worldCamera);
    }
    void DestroyInterface()
    {
        CloseStatistics();
        if (StatisticsButton != null) Destroy(StatisticsButton.gameObject);
        if (detailOverlay != null) Destroy(detailOverlay);
        if (curvesOverlay != null) Destroy(curvesOverlay);
    }
}



