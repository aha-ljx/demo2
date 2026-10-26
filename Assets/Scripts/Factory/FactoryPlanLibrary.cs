using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class FactoryPlanLibrary : MonoBehaviour
{
    static FactoryPlanLibrary active;
    Canvas canvas;
    PlacementSystem placement;
    FactoryLevelSession level;
    GameObject saveOverlay, listOverlay;
    RectTransform saveCard, listCard;
    TMP_Text saveMessage, listMessage, pageLabel, summary;
    readonly Button[] rows = new Button[4];
    readonly Button[] deleteButtons = new Button[4];
    readonly TMP_Text[] names = new TMP_Text[4], descriptions = new TMP_Text[4];
    Button previous, next, cancelSave;
    List<FactorySavedPlan> plans = new List<FactorySavedPlan>();
    int page, closeThroughFrame = -1;
    bool savedThisDialog;
    float deleteReadyAt;
    CursorLockMode savedCursor;
    bool savedVisible;
    public Button PlanningButton { get; private set; }
    public Button ConfirmSaveButton { get; private set; }
    public TMP_InputField NameInput { get; private set; }
    public bool IsSaveOpen => saveOverlay != null && saveOverlay.activeSelf;
    public bool IsListOpen => listOverlay != null && listOverlay.activeSelf;
    public static bool BlocksGameplay => active != null &&
        (active.IsSaveOpen || active.IsListOpen || Time.frameCount <= active.closeThroughFrame);

    public void Initialize(Canvas target, PlacementSystem builder, FactoryLevelSession session)
    {
        active = this; canvas = target; placement = builder; level = session;
        PlanningButton = FactoryGuideUI.Button(canvas.transform, "SavedPlansButton", "方案库", Vector2.zero,
            new Vector2(210, 56), FactoryGuideUI.CardColor, OpenList);
        var buttonRect = PlanningButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(238, 24);

        saveOverlay = FactoryGuideUI.Overlay(canvas.transform, "SavePlanOverlay", new Color(0, 0, 0, .65f)).gameObject;
        saveCard = FactoryGuideUI.Box(saveOverlay.transform, "SavePlanCard", Vector2.zero, new Vector2(820, 520), FactoryGuideUI.PanelColor);
        saveCard.GetComponent<Image>().raycastTarget = true;
        FactoryGuideUI.Text(saveCard, "Title", "保存当前方案", new Vector2(0, 200), new Vector2(740, 56), 34, FactoryGuideUI.Mint, TextAlignmentOptions.Center);
        FactoryGuideUI.Text(saveCard, "Description", "保存布局，不保存生产进度。", new Vector2(0, 122), new Vector2(720, 66), 22, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        var inputBox = FactoryGuideUI.Box(saveCard, "PlanNameInput", new Vector2(0, 38), new Vector2(700, 60), FactoryGuideUI.CardColor);
        inputBox.GetComponent<Image>().raycastTarget = true;
        var inputText = FactoryGuideUI.Text(inputBox, "Text", "", Vector2.zero, new Vector2(660, 52), 25, FactoryGuideUI.Ink);
        inputText.enableWordWrapping = false; inputText.richText = false;
        var placeholder = FactoryGuideUI.Text(inputBox, "Placeholder", "输入方案名称", Vector2.zero, new Vector2(660, 52), 25, FactoryGuideUI.Muted);
        NameInput = inputBox.gameObject.AddComponent<TMP_InputField>();
        NameInput.textViewport = inputBox; NameInput.textComponent = inputText; NameInput.placeholder = placeholder;
        NameInput.fontAsset = inputText.font; NameInput.pointSize = 25; NameInput.characterLimit = 24;
        NameInput.lineType = TMP_InputField.LineType.SingleLine; NameInput.richText = false;
        NameInput.customCaretColor = true; NameInput.caretColor = FactoryGuideUI.Ink; NameInput.selectionColor = new Color(.94f, .98f, .03f, .35f);
        summary = FactoryGuideUI.Text(saveCard, "Summary", "", new Vector2(0, -31), new Vector2(720, 42), 21, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        saveMessage = FactoryGuideUI.Text(saveCard, "Message", "", new Vector2(0, -99), new Vector2(720, 72), 22, FactoryGuideUI.Ink, TextAlignmentOptions.Center);
        NameInput.onValueChanged.AddListener(value => { if (!savedThisDialog) saveMessage.text = ""; });
        cancelSave = FactoryGuideUI.Button(saveCard, "Cancel", "取消", new Vector2(-170, -193), new Vector2(280, 60), FactoryGuideUI.CardColor, Close);
        ConfirmSaveButton = FactoryGuideUI.Button(saveCard, "ConfirmSave", "保存方案", new Vector2(170, -193), new Vector2(280, 60), FactoryGuideUI.Mint, SaveFromDialog, true);
        saveOverlay.SetActive(false);

        listOverlay = FactoryGuideUI.Overlay(canvas.transform, "SavedPlansOverlay", new Color(0, 0, 0, .6f)).gameObject;
        listCard = FactoryGuideUI.Box(listOverlay.transform, "SavedPlansCard", Vector2.zero, new Vector2(960, 800), FactoryGuideUI.PanelColor);
        listCard.GetComponent<Image>().raycastTarget = true;
        FactoryGuideUI.Text(listCard, "Title", "方案库", new Vector2(0, 343), new Vector2(840, 58), 34, FactoryGuideUI.Mint);
        FactoryGuideUI.Text(listCard, "Description", "仅可载入空白场景", new Vector2(0, 286), new Vector2(840, 48), 23, FactoryGuideUI.Muted);
        for (int i = 0; i < rows.Length; i++)
        {
            int index = i;
            rows[i] = FactoryGuideUI.Button(listCard, "PlanRow" + i, "", new Vector2(0, 180 - i * 116), new Vector2(840, 112), FactoryGuideUI.CardColor,
                () => LoadRow(index));
            var row = rows[i].transform;
            names[i] = FactoryGuideUI.Text(row, "PlanName", "", new Vector2(-95, 29), new Vector2(580, 40), 24, FactoryGuideUI.Ink);
            names[i].richText = false; names[i].enableWordWrapping = false;
            descriptions[i] = FactoryGuideUI.Text(row, "PlanSummary", "", new Vector2(-95, -23), new Vector2(580, 62), 18, FactoryGuideUI.Muted);
            FactoryGuideUI.Text(row, "LoadLabel", "载入", new Vector2(315, 27), new Vector2(160, 40), 23, FactoryGuideUI.Mint, TextAlignmentOptions.Center);
            deleteButtons[i] = FactoryGuideUI.Button(row, "DeletePlan", "删除", new Vector2(315, -27), new Vector2(150, 40),
                new Color(.38f, .17f, .18f), () => DeleteRow(index));
            deleteButtons[i].GetComponentInChildren<TMP_Text>().fontSize = 21;
        }
        listMessage = FactoryGuideUI.Text(listCard, "Message", "", new Vector2(0, -267), new Vector2(840, 54), 21, FactoryGuideUI.Gold, TextAlignmentOptions.Center);
        previous = FactoryGuideUI.Button(listCard, "Previous", "上一页", new Vector2(-320, -346), new Vector2(160, 54), FactoryGuideUI.CardColor, () => { page--; RefreshList(); });
        pageLabel = FactoryGuideUI.Text(listCard, "Page", "", new Vector2(-120, -346), new Vector2(180, 48), 21, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        next = FactoryGuideUI.Button(listCard, "Next", "下一页", new Vector2(80, -346), new Vector2(160, 54), FactoryGuideUI.CardColor, () => { page++; RefreshList(); });
        FactoryGuideUI.Button(listCard, "Close", "关闭", new Vector2(320, -346), new Vector2(200, 54), FactoryGuideUI.Mint, Close, true);
        listOverlay.SetActive(false);
        var tutorial = GetComponent<FactoryTutorial>();
        if (tutorial != null && tutorial.IsOpen) tutorial.Overlay.transform.SetAsLastSibling();
    }
    public void OpenSave()
    {
        if (!level.IsEnded || IsSaveOpen || IsListOpen || FactoryLocalAssistant.BlocksGameplay || FactoryStatistics.BlocksGameplay) return;
        CaptureCursor(); savedThisDialog = false; saveOverlay.SetActive(true); saveOverlay.transform.SetAsLastSibling();
        NameInput.interactable = true;
        NameInput.text = "方案 " + DateTime.Now.ToString("MM-dd HH:mm:ss");
        summary.text = placement.PlacedCount + " 件设备与滑槽  ·  本局产量 " + level.ResultCount + " 件";
        saveMessage.text = placement.PlacedCount == 0 ? "当前没有已摆放的设备，无法保存方案" : "可修改上方名称；同名保存会保留为另一份方案。";
        ConfirmSaveButton.interactable = placement.PlacedCount > 0;
        cancelSave.GetComponentInChildren<TMP_Text>().text = "取消";
        FactoryGuideUI.Fit(saveCard, canvas, new Vector2(820, 520), 32);
    }
    void SaveFromDialog()
    {
        if (savedThisDialog || !IsSaveOpen || !level.IsEnded || placement.PlacedCount == 0) return;
        string name = NameInput.text.Trim();
        if (name.Length == 0) { saveMessage.text = "请先输入方案名称"; return; }
        if (!FactoryPlanStorage.Save(placement.CapturePlan(name, level), out string error)) { saveMessage.text = error; return; }
        savedThisDialog = true; ConfirmSaveButton.interactable = false;
        NameInput.interactable = false;
        saveMessage.text = "已保存";
        cancelSave.GetComponentInChildren<TMP_Text>().text = "关闭";
    }
    public void OpenList()
    {
        if (level.IsRunning || level.IsEnded || IsSaveOpen || IsListOpen || FactoryTutorial.BlocksGameplay ||
            RawMaterialSupply.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay || FactoryStatistics.BlocksGameplay) return;
        plans = FactoryPlanStorage.ReadAll(out string message); page = 0;
        CaptureCursor(); listOverlay.SetActive(true); listOverlay.transform.SetAsLastSibling();
        listMessage.text = message ?? (plans.Count == 0 ? "暂无方案" : "");
        RefreshList();
    }
    void RefreshList()
    {
        int pages = Mathf.Max(1, (plans.Count + 3) / 4); page = Mathf.Clamp(page, 0, pages - 1);
        bool canLoad = placement.CanLoadPlan(out string reason);
        if (!canLoad) listMessage.text = reason;
        for (int i = 0; i < rows.Length; i++)
        {
            int index = page * 4 + i; bool visible = index < plans.Count; rows[i].gameObject.SetActive(visible);
            if (!visible) continue;
            var plan = plans[index]; names[i].text = plan.name;
            descriptions[i].text = plan.resultCount + " 件  ·  成本 " + plan.cost.ToString("N0") + " 元";
            rows[i].interactable = canLoad;
            deleteButtons[i].interactable = Time.unscaledTime >= deleteReadyAt;
        }
        previous.interactable = page > 0; next.interactable = page + 1 < pages;
        pageLabel.text = (page + 1) + " / " + pages + " 页";
        FactoryGuideUI.Fit(listCard, canvas, new Vector2(960, 800), 32);
    }
    void LoadRow(int row)
    {
        int index = page * 4 + row;
        if (!IsListOpen || index < 0 || index >= plans.Count) return;
        var plan = plans[index];
        if (!placement.TryLoadPlan(plan, out string error)) { listMessage.text = error; return; }
        Close(); FactoryProduction.Instance.Message("已载入方案“" + plan.name + "”，可继续调整设备和上料；点击开始生产运行新一局");
    }
    void DeleteRow(int row)
    {
        int index = page * 4 + row;
        if (!IsListOpen || level.IsRunning || level.IsEnded || index < 0 || index >= plans.Count || Time.unscaledTime < deleteReadyAt) return;
        // A double click must not delete the next plan after the rows move up.
        deleteReadyAt = Time.unscaledTime + .4f;
        bool deleted = FactoryPlanStorage.Delete(plans[index], out string error);
        plans = FactoryPlanStorage.ReadAll(out _);
        RefreshList();
        listMessage.text = deleted ? (plans.Count == 0 ? "方案已删除；暂无已保存方案。" : "方案已删除") : error;
    }
    void CaptureCursor()
    {
        savedCursor = Cursor.lockState; savedVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    public void Close()
    {
        if (!IsSaveOpen && !IsListOpen) return;
        saveOverlay.SetActive(false); listOverlay.SetActive(false); closeThroughFrame = Time.frameCount + 1;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = level != null && level.IsEnded ? CursorLockMode.None : savedCursor;
        Cursor.visible = level != null && level.IsEnded || savedVisible;
    }
    void Update()
    {
        if (level == null) return;
        PlanningButton.gameObject.SetActive(!level.IsRunning && !level.IsEnded);
        if ((IsListOpen || IsSaveOpen) && Input.GetKeyDown(KeyCode.Escape)) Close();
        if (IsSaveOpen) FactoryGuideUI.Fit(saveCard, canvas, new Vector2(820, 520), 32);
        if (IsListOpen)
        {
            FactoryGuideUI.Fit(listCard, canvas, new Vector2(960, 800), 32);
            foreach (var button in deleteButtons) button.interactable = Time.unscaledTime >= deleteReadyAt;
        }
    }
    public static bool PointerOverButton(Vector2 point)
    {
        if (active == null || active.PlanningButton == null || !active.PlanningButton.isActiveAndEnabled) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(active.PlanningButton.GetComponent<RectTransform>(), point,
            active.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : active.canvas.worldCamera);
    }
    void OnDestroy()
    {
        Close();
        if (PlanningButton != null) Destroy(PlanningButton.gameObject);
        if (saveOverlay != null) Destroy(saveOverlay);
        if (listOverlay != null) Destroy(listOverlay);
        if (active == this) active = null;
    }
}



