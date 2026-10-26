using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class FactoryLocalAssistant : MonoBehaviour
{
    static FactoryLocalAssistant active;
    Canvas canvas;
    PlacementSystem placement;
    FactoryLevelSession level;
    FactoryAssistantCatalog catalog;
    FactoryAssistantAnswer answer;
    GameObject overlay;
    RectTransform card;
    TMP_Text result, objective, layout, applyHint;
    CursorLockMode savedCursor;
    bool savedVisible;
    int closedThroughFrame = -1;
    public Button AssistantButton { get; private set; }
    public Button CalculateButton { get; private set; }
    public Button ApplyButton { get; private set; }
    public TMP_InputField QuestionInput { get; private set; }
    public bool IsOpen => overlay != null && overlay.activeSelf;
    public FactoryAssistantAnswer Answer => answer;
    public static bool BlocksGameplay => active != null && (active.IsOpen || Time.frameCount <= active.closedThroughFrame);

    public void Initialize(Canvas target, PlacementSystem builder, FactoryLevelSession session)
    {
        active = this; canvas = target; placement = builder; level = session;
        AssistantButton = FactoryGuideUI.Button(canvas.transform, "LocalAssistantButton", "AI游戏助手", Vector2.zero,
            new Vector2(210, 56), FactoryGuideUI.CardColor, Open);
        var buttonRect = AssistantButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.zero; buttonRect.anchoredPosition = new Vector2(24, 94);
        overlay = FactoryGuideUI.Overlay(canvas.transform, "LocalAssistantOverlay", new Color(0, 0, 0, .65f)).gameObject;
        card = FactoryGuideUI.Box(overlay.transform, "LocalAssistantCard", Vector2.zero, new Vector2(1080, 800), FactoryGuideUI.PanelColor);
        card.GetComponent<Image>().raycastTarget = true;
        FactoryGuideUI.Text(card, "Title", "AI产线助手 · 本地求解", new Vector2(0, 340), new Vector2(960, 60), 34, FactoryGuideUI.Mint);
        FactoryGuideUI.Text(card, "Introduction", "输入产量和时间目标；助手比较真实生产验证过的布局，并选择候选中成本最低的可行方案。", new Vector2(0, 276), new Vector2(960, 64), 20, FactoryGuideUI.Muted);
        var input = FactoryGuideUI.Box(card, "QuestionInput", new Vector2(0, 185), new Vector2(960, 100), FactoryGuideUI.CardColor);
        input.GetComponent<Image>().raycastTarget = true;
        var inputText = FactoryGuideUI.Text(input, "Text", "", Vector2.zero, new Vector2(920, 86), 24, FactoryGuideUI.Ink);
        inputText.richText = false;
        QuestionInput = input.gameObject.AddComponent<TMP_InputField>();
        QuestionInput.textViewport = input; QuestionInput.textComponent = inputText;
        QuestionInput.fontAsset = inputText.font; QuestionInput.pointSize = 24; QuestionInput.characterLimit = 1000;
        QuestionInput.lineType = TMP_InputField.LineType.MultiLineSubmit; QuestionInput.richText = false;
        QuestionInput.customCaretColor = true; QuestionInput.caretColor = FactoryGuideUI.Ink; QuestionInput.selectionColor = new Color(.94f, .98f, .03f, .35f);
        objective = FactoryGuideUI.Text(card, "Objective", "", new Vector2(0, 99), new Vector2(960, 46), 22, FactoryGuideUI.Muted);
        result = FactoryGuideUI.Text(card, "Answer", "", new Vector2(0, -20), new Vector2(960, 180), 25, FactoryGuideUI.Ink);
        layout = FactoryGuideUI.Text(card, "Layout", "", new Vector2(0, -159), new Vector2(960, 90), 21, FactoryGuideUI.Muted);
        applyHint = FactoryGuideUI.Text(card, "ApplyHint", "", new Vector2(0, -258), new Vector2(960, 70), 20, FactoryGuideUI.Gold);
        CalculateButton = FactoryGuideUI.Button(card, "Calculate", "计算方案", new Vector2(-340, -340), new Vector2(260, 60), FactoryGuideUI.Mint, Calculate, true);
        ApplyButton = FactoryGuideUI.Button(card, "Apply", "采用方案", new Vector2(0, -340), new Vector2(300, 60), FactoryGuideUI.CardColor, Apply);
        FactoryGuideUI.Button(card, "Close", "关闭", new Vector2(350, -340), new Vector2(220, 60), FactoryGuideUI.CardColor, Close);
        QuestionInput.onValueChanged.AddListener(value => { if (useDeepSeek) return; answer = null; ApplyButton.interactable = false; result.text = "目标已改变，请点击“计算方案”。"; layout.text = objective.text = ""; });
        InitializeDeepSeek();
        InitializeChat(); RefreshProvider();
        overlay.SetActive(false);
        var tutorial = GetComponent<FactoryTutorial>(); if (tutorial != null && tutorial.IsOpen) tutorial.Overlay.transform.SetAsLastSibling();
    }
    public void Open()
    {
        if (IsOpen || FactoryTutorial.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay ||
            FactoryStatistics.BlocksGameplay || RawMaterialSupply.BlocksGameplay) return;
        savedCursor = Cursor.lockState; savedVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        overlay.SetActive(true); overlay.transform.SetAsLastSibling();
        QuestionInput.interactable = true;
        if (!useDeepSeek) QuestionInput.text = "能否在600秒内得到10件产物，并且成本最低？";
        answer = null; ApplyButton.interactable = false; CalculateButton.interactable = true;
        RefreshProvider();
        result.text = "点击“计算方案”，检查目标、比较成本并给出可摆放的方案。";
        applyHint.text = useDeepSeek ? ChatPhaseHint() : "时间从开始生产起计算，第一关仍为600秒；摆放不会自动开始生产。";
        FactoryGuideUI.Fit(card, canvas, new Vector2(1080, 800), 32);
        if (settingsPanel != null && settingsPanel.gameObject.activeSelf) FactoryGuideUI.Fit(settingsPanel, canvas, new Vector2(880, 650), 32);
    }
    public void Calculate()
    {
        if (!IsOpen || networkRoutine != null) return;
        LoadCatalog();
        if (useDeepSeek && ExecutePlacementCommand(QuestionInput.text)) return;
        answer = null; layout.text = objective.text = ""; ApplyButton.interactable = false;
        if (useDeepSeek) { BeginDeepSeek(); return; }
        if (level.IsRunning || level.IsEnded) { result.text = "离线方案计算仅在规划阶段使用；运行中请切换DeepSeek咨询。"; return; }
        if (!FactoryAssistantSolver.Parse(QuestionInput.text, out int quantity, out float seconds, out string error)) { result.text = error; return; }
        ShowVerifiedAnswer(quantity, seconds);
    }
    void LoadCatalog()
    {
        if (catalog == null)
        {
            var file = Resources.Load<TextAsset>("FactoryAssistant/VerifiedPlans");
            if (file != null) try { catalog = JsonUtility.FromJson<FactoryAssistantCatalog>(file.text); } catch (ArgumentException) { catalog = null; }
        }
    }
    void ShowVerifiedAnswer(int quantity, float seconds, bool sharedPunch = false, bool maximize = false)
    {
        string error;
        var factory = FactoryProduction.Instance;
        answer = sharedPunch && maximize ? FactoryAssistantSolver.SolveBestSharedPunch(seconds, catalog, placement.AssistantScale, factory.armTravelMultiplier, factory.armPickupMultiplier, factory.armRotationMultiplier) :
            FactoryAssistantSolver.Solve(quantity, seconds, catalog, placement.AssistantScale, factory.armTravelMultiplier, factory.armPickupMultiplier, factory.armRotationMultiplier, sharedPunch);
        quantity = answer.quantity;
        lastRecommendedAnswer = answer.best != null ? answer : null;
        objective.text = "目标：" + seconds.ToString("0.#") + " 仿真秒内入库 " + quantity + " 件";
        result.text = answer.message;
        if (answer.best == null) { applyHint.text = "没有经过验证的可摆放方案；助手不会据猜测修改场景。"; return; }
        result.text = "预计 " + answer.completion.ToString("0.0") + " 秒（含10秒余量）\n成本 " + answer.cost.ToString("N0") + " 元 · 已验证候选内最优";
        if (sharedPunch && maximize) result.text += "\n单冲床双轨 · 最高 " + quantity + " 件";
        int[] counts = new int[5]; foreach (var device in answer.best.plan.devices) counts[device.kind]++;
        var text = new StringBuilder("冲床 " + counts[0] + " · 折弯机 " + counts[1] + " · 焊接机 " + counts[2] + " · 机械臂 " + counts[3] + " · 滑槽 " + counts[4]);
        text.Append("\n上料分配：");
        for (int i = 0; i < answer.best.feed.Length; i++) text.Append((i > 0 ? "，" : "") + "冲床" + (i + 1) + "=" + answer.best.feed[i] + "块");
        layout.text = text.ToString();
        ApplyButton.interactable = placement.CanLoadPlan(out error);
        applyHint.text = ApplyButton.interactable ? "可采用方案 · 修改布局后需重新验证" : error;
    }
    public void Apply()
    {
        if (!IsOpen || !ApplyButton.interactable || answer?.best == null || level.IsRunning || level.IsEnded) return;
        if (!placement.ApplyAssistantPlan(answer.best, out string error)) { applyHint.text = error; if (useDeepSeek) AddChat("助手", "未摆放：" + error, true); return; }
        ApplyButton.interactable = false;
        if (!useDeepSeek) { CalculateButton.interactable = false; QuestionInput.interactable = false; }
        applyHint.text = "已摆放并上料 · 关闭后开始生产";
        if (useDeepSeek) AddChat("助手", "已摆放：" + answer.quantity + "件 / " + answer.deadline.ToString("0.#") + "秒方案\n成本 " + answer.cost.ToString("N0") + " 元\n" + layout.text + "\n关闭面板后开始生产。", true);
    }
    public void Close()
    {
        if (!IsOpen) return;
        CancelDeepSeek();
        if (settingsPanel != null) { settingsPanel.gameObject.SetActive(false); card.gameObject.SetActive(true); }
        overlay.SetActive(false); closedThroughFrame = Time.frameCount + 1;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = level != null && level.IsEnded ? CursorLockMode.None : savedCursor;
        Cursor.visible = level != null && level.IsEnded || savedVisible;
    }
    void Update()
    {
        if (level == null) return;
        AssistantButton.gameObject.SetActive(true);
        if (level.IsEnded && !IsOpen) AssistantButton.transform.SetAsLastSibling();
        if (!IsOpen) return;
        overlay.transform.SetAsLastSibling();
        ApplyButton.gameObject.SetActive(!useDeepSeek || ApplyButton.interactable);
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
        FactoryGuideUI.Fit(card, canvas, new Vector2(1080, 800), 32);
        if (settingsPanel != null && settingsPanel.gameObject.activeSelf) FactoryGuideUI.Fit(settingsPanel, canvas, new Vector2(880, 650), 32);
    }
    public static bool PointerOverButton(Vector2 point)
    {
        if (active == null || active.AssistantButton == null || !active.AssistantButton.isActiveAndEnabled) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(active.AssistantButton.GetComponent<RectTransform>(), point,
            active.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : active.canvas.worldCamera);
    }
    void OnDestroy()
    {
        CancelDeepSeek(); sessionKey = "";
        Close(); if (AssistantButton != null) Destroy(AssistantButton.gameObject); if (overlay != null) Destroy(overlay);
        if (active == this) active = null;
    }
}

