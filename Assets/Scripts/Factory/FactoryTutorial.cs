using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(-50)]
public sealed class FactoryTutorial : MonoBehaviour
{
    static FactoryTutorial active;
    public static bool BlocksGameplay => active != null && (active.IsOpen || Time.frameCount <= active.resumeAfterFrame);
    public bool IsOpen { get; private set; }
    public int PageIndex { get; private set; }
    public const int PageCount = 5;
    public Button NextButton { get; private set; }
    public Button PreviousButton { get; private set; }
    public Button HelpButton { get; private set; }
    public RectTransform Card { get; private set; }
    public GameObject Overlay { get; private set; }
    readonly GameObject[] pages = new GameObject[PageCount];
    readonly Image[] steps = new Image[PageCount];
    readonly TextMeshProUGUI[] stepLabels = new TextMeshProUGUI[PageCount];
    readonly string[] titles = { "移动与视角", "摆放设备", "鸟瞰模式", "连接产线", "目标与成本" };
    readonly string[] summaries = { "移动角色，切换速度，再选择适合你的观察视角。", "选模型、调朝向、确认放置，搭建产线从这里开始。",
        "切换到鸟瞰视角，查看布局并继续摆放设备。", "按照加工顺序连接模块，让机械臂沿滑槽搬运工件。", "关注成本、时间和产量，规划更高效的工厂。" };
    readonly string[] hints = { "Shift 切换快慢，无需一直按住。", "规划时按 B 或鸟瞰右键撤回；生产开始后不能撤回已放置设备。",
        "鸟瞰移动的是视角，角色会留在原位。", "滑槽边缘接触指定的黄色停靠区域即可对接。", "" };
    Canvas canvas;
    TextMeshProUGUI title, summary, counter, hint, nextLabel;
    float savedSpeed;
    CursorLockMode savedCursor;
    bool savedVisible;
    int resumeAfterFrame = -1;

    public void Initialize(Canvas target, bool showOnEntry)
    {
        active = this; canvas = target;
        HelpButton = FactoryGuideUI.Button(canvas.transform, "TutorialHelp", "操作教程  [H]", Vector2.zero,
            new Vector2(200, 56), FactoryGuideUI.CardColor, Open);
        var helpRect = HelpButton.GetComponent<RectTransform>();
        helpRect.anchorMin = helpRect.anchorMax = helpRect.pivot = Vector2.one;
        helpRect.anchoredPosition = new Vector2(-24, -24);
        Overlay = FactoryGuideUI.Overlay(canvas.transform, "TutorialOverlay", new Color(.015f, .025f, .035f, .82f)).gameObject;
        Card = FactoryGuideUI.Box(Overlay.transform, "TutorialCard", Vector2.zero, new Vector2(1180, 760), FactoryGuideUI.PanelColor);
        FactoryGuideUI.Box(Card, "Sidebar", new Vector2(-455, 0), new Vector2(270, 760), FactoryGuideUI.Background);
        FactoryGuideUI.Text(Card, "GuideBrand", "操作教程", new Vector2(-455, 310), new Vector2(220, 50), 28, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(Card, "GuideSubtitle", "", new Vector2(-455, 264), new Vector2(220, 34), 20, FactoryGuideUI.Mint);
        string[] sections = { "移动与视角", "模块摆放", "鸟瞰规划", "产线连接", "成本与目标" };
        for (int i = 0; i < PageCount; i++)
        {
            var step = FactoryGuideUI.Box(Card, "Step" + i, new Vector2(-455, 156 - i * 80), new Vector2(222, 66), FactoryGuideUI.CardColor);
            steps[i] = step.GetComponent<Image>();
            stepLabels[i] = FactoryGuideUI.Text(step, "StepLabel", "0" + (i + 1) + "   " + sections[i], Vector2.zero,
                new Vector2(196, 66), 23, FactoryGuideUI.Muted);
        }
        FactoryGuideUI.Text(Card, "ReadAtYourPace", "", new Vector2(-455, -295), new Vector2(222, 80), 20, FactoryGuideUI.Muted);
        title = FactoryGuideUI.Text(Card, "PageTitle", "", new Vector2(145, 270), new Vector2(810, 68), 38, FactoryGuideUI.Ink);
        summary = FactoryGuideUI.Text(Card, "PageSummary", "", new Vector2(145, 208), new Vector2(810, 56), 24, FactoryGuideUI.Muted);
        FactoryGuideUI.Button(Card, "SkipTutorial", "跳过教程", new Vector2(470, 337), new Vector2(152, 50), FactoryGuideUI.PanelColor, Close);
        for (int i = 0; i < PageCount; i++)
            pages[i] = FactoryGuideUI.Box(Card, "TutorialPage" + i, new Vector2(145, -8), new Vector2(810, 340), FactoryGuideUI.CardColor).gameObject;
        BuildMovement(pages[0].transform);
        BuildPlacement(pages[1].transform);
        BuildBird(pages[2].transform);
        BuildProductionLine(pages[3].transform);
        BuildObjectives(pages[4].transform);
        hint = FactoryGuideUI.Text(Card, "PageHint", "", new Vector2(145, -220), new Vector2(810, 50), 21, FactoryGuideUI.Muted);
        PreviousButton = FactoryGuideUI.Button(Card, "PreviousPage", "<  上一页", new Vector2(-155, -309), new Vector2(206, 64), FactoryGuideUI.CardColor, Previous);
        counter = FactoryGuideUI.Text(Card, "PageCounter", "", new Vector2(140, -309), new Vector2(200, 60), 23, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        NextButton = FactoryGuideUI.Button(Card, "NextPage", "下一页  >", new Vector2(420, -309), new Vector2(250, 64), FactoryGuideUI.Mint, Next, true);
        nextLabel = NextButton.GetComponentInChildren<TextMeshProUGUI>();
        FactoryGuideUI.Text(Card, "KeyboardHint", "", new Vector2(145, -364), new Vector2(810, 28), 17,
            FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        Overlay.SetActive(false);
        if (showOnEntry) Open();
    }
    void Wasd(Transform parent, Vector2 center)
    {
        FactoryGuideUI.Key(parent, "W", center + Vector2.up * 72);
        FactoryGuideUI.Key(parent, "A", center + Vector2.left * 82);
        FactoryGuideUI.Key(parent, "S", center);
        FactoryGuideUI.Key(parent, "D", center + Vector2.right * 82);
    }
    void BuildMovement(Transform page)
    {
        Wasd(page, new Vector2(-238, -4));
        FactoryGuideUI.Text(page, "Movement", "W 前进 · S 后退\nA 向左 · D 向右", new Vector2(-238, -103), new Vector2(300, 82), 23, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        FactoryGuideUI.Key(page, "Shift", new Vector2(55, 67), 130);
        FactoryGuideUI.Text(page, "Speed", "切换冲刺 / 慢走", new Vector2(244, 67), new Vector2(226, 76), 25, FactoryGuideUI.Ink);
        FactoryGuideUI.Key(page, "CapsLk", new Vector2(55, -52), 130);
        FactoryGuideUI.Text(page, "View", "切换视图模式", new Vector2(244, -52), new Vector2(226, 76), 25, FactoryGuideUI.Ink);
    }
    void BuildPlacement(Transform page)
    {
        string[] keys = { "1 - 5", "R", "鼠标左键", "鼠标右键" };
        string[] actions = { "按数字键选择并显示模型", "旋转模型，调整放置朝向", "确认放置当前模型", "撤回当前模型 / 取消放置" };
        for (int i = 0; i < keys.Length; i++)
        {
            float y = 114 - i * 76;
            FactoryGuideUI.Key(page, keys[i], new Vector2(-264, y), 180);
            FactoryGuideUI.Text(page, "PlacementAction" + i, actions[i], new Vector2(112, y), new Vector2(500, 64), 25, FactoryGuideUI.Ink);
        }
    }
    void BuildBird(Transform page)
    {
        FactoryGuideUI.Key(page, "CapsLk", new Vector2(-238, 95), 180);
        FactoryGuideUI.Text(page, "BirdSwitch", "点按后进入鸟瞰模式", new Vector2(112, 95), new Vector2(500, 66), 25, FactoryGuideUI.Ink);
        Wasd(page, new Vector2(-238, -82));
        FactoryGuideUI.Text(page, "BirdPan", "WASD 上下左右移动视角\n\n数字键选模型、R 旋转\n左键放置，右键撤回当前模型", new Vector2(112, -47), new Vector2(500, 188), 25, FactoryGuideUI.Ink);
    }
    void BuildProductionLine(Transform page)
    {
        string[] modules = { "冲床", "折弯", "焊接", "最终区域" };
        for (int i = 0; i < modules.Length; i++)
        {
            var module = FactoryGuideUI.Box(page, "LineModule" + i, new Vector2(-300 + i * 200, 100),
                new Vector2(158, 66), FactoryGuideUI.PanelColor);
            FactoryGuideUI.Text(module, "ModuleName", modules[i], Vector2.zero, new Vector2(146, 60), 26,
                i == modules.Length - 1 ? FactoryGuideUI.Gold : FactoryGuideUI.Mint, TextAlignmentOptions.Center);
            if (i < modules.Length - 1)
                FactoryGuideUI.Text(page, "FlowArrow" + i, "→", new Vector2(-200 + i * 200, 100), new Vector2(36, 60),
                    28, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        }
        FactoryGuideUI.Text(page, "RailConnection", "用滑槽连接每个模块，在滑槽上放置机械臂。",
            new Vector2(0, 12), new Vector2(750, 54), 25, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(page, "RailTurns", "滑槽可以拐弯，需接触模块指定的停靠区域。",
            new Vector2(0, -49), new Vector2(750, 54), 25, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(page, "OneArmPerRail", "每条独立滑槽只能放置一个机械臂。",
            new Vector2(0, -112), new Vector2(750, 54), 25, FactoryGuideUI.Gold);
    }
    void BuildObjectives(Transform page)
    {
        string[] names = { "模块成本", "关卡时间", "生产目标" };
        string[] values = { "每个模块\n都有成本", "第一关\n10分钟", "尽量多入库\n同产量比净收益" };
        for (int i = 0; i < 3; i++)
        {
            var tile = FactoryGuideUI.Box(page, "Objective" + i, new Vector2(-258 + i * 258, 40), new Vector2(236, 204), FactoryGuideUI.PanelColor);
            FactoryGuideUI.Text(tile, "MetricName", names[i], new Vector2(0, 62), new Vector2(206, 42), 22, FactoryGuideUI.Gold, TextAlignmentOptions.Center);
            FactoryGuideUI.Text(tile, "MetricValue", values[i], new Vector2(0, -18), new Vector2(216, 100), 25, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        }
        FactoryGuideUI.Text(page, "Goal", "点击设备查看统计；冲床详情可选择上料数量。\n每件2000元；产量优先，同产量比净收益；开始后不能撤回。", new Vector2(0, -112), new Vector2(790, 86), 23, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
    }
    public void Open()
    {
        if (IsOpen || Overlay == null || FactoryLevelSession.HasEnded || RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay) return;
        savedSpeed = Time.timeScale; savedCursor = Cursor.lockState; savedVisible = Cursor.visible;
        PageIndex = 0; IsOpen = true; Overlay.SetActive(true); Overlay.transform.SetAsLastSibling();
        Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Refresh();
    }
    public void Next() { if (!IsOpen) return; if (PageIndex == PageCount - 1) Close(); else { PageIndex++; Refresh(); } }
    public void Previous() { if (IsOpen && PageIndex > 0) { PageIndex--; Refresh(); } }
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false; Overlay.SetActive(false); resumeAfterFrame = Time.frameCount + 1;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Time.timeScale = savedSpeed; Cursor.lockState = savedCursor; Cursor.visible = savedVisible;
    }
    void Refresh()
    {
        // Enter is handled here, rather than also submitting a previously clicked button.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        title.text = titles[PageIndex]; summary.text = ""; hint.text = hints[PageIndex];
        counter.text = (PageIndex + 1).ToString("00") + " / " + PageCount.ToString("00");
        PreviousButton.interactable = PageIndex > 0;
        PreviousButton.GetComponentInChildren<TextMeshProUGUI>().color = PageIndex > 0 ? FactoryGuideUI.Ink : new Color(.35f, .43f, .46f);
        nextLabel.text = PageIndex == PageCount - 1 ? "开始规划  >" : "下一页  >";
        for (int i = 0; i < PageCount; i++)
        {
            pages[i].SetActive(i == PageIndex);
            steps[i].color = i == PageIndex ? FactoryGuideUI.Mint : FactoryGuideUI.CardColor;
            stepLabels[i].color = i == PageIndex ? FactoryGuideUI.Ink : FactoryGuideUI.Muted;
        }
        FactoryGuideUI.Fit(Card, canvas, new Vector2(1180, 760));
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H) && !IsOpen) Open();
        if (!IsOpen) return;
        FactoryGuideUI.Fit(Card, canvas, new Vector2(1180, 760));
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Next();
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) Previous();
    }
    public static bool PointerOverHelp(Vector2 point)
    {
        if (active == null || active.HelpButton == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(active.HelpButton.GetComponent<RectTransform>(), point,
            active.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : active.canvas.worldCamera);
    }
    void OnDestroy()
    {
        if (IsOpen) { Time.timeScale = savedSpeed; Cursor.lockState = savedCursor; Cursor.visible = savedVisible; }
        if (Overlay != null) Destroy(Overlay);
        if (HelpButton != null) Destroy(HelpButton.gameObject);
        if (active == this) active = null;
    }
}


