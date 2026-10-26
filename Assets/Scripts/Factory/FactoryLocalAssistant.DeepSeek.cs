using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class FactoryLocalAssistant
{
    readonly FactoryDeepSeekClient deepSeek = new FactoryDeepSeekClient();
    Coroutine networkRoutine;
    bool useDeepSeek = true;
    string sessionKey = "", model = FactoryDeepSeekClient.DefaultModel;
    Button providerButton, settingsButton;
    RectTransform settingsPanel;
    TMP_InputField keyInput, modelInput;
    TMP_Text title, introduction;

    void InitializeDeepSeek()
    {
        title = card.Find("Title").GetComponent<TMP_Text>();
        introduction = card.Find("Introduction").GetComponent<TMP_Text>();
        title.rectTransform.sizeDelta = new Vector2(650, 60); title.rectTransform.anchoredPosition = new Vector2(-155, 340);
        providerButton = FactoryGuideUI.Button(card, "Provider", "切换DeepSeek", new Vector2(350, 344), new Vector2(260, 48), FactoryGuideUI.CardColor,
            () => { useDeepSeek = !useDeepSeek; answer = null; ApplyButton.interactable = false; layout.text = objective.text = ""; QuestionInput.text = ""; RefreshProvider(); result.text = "已切换求解方式，请点击计算方案。"; });
        settingsButton = FactoryGuideUI.Button(card, "APISettings", "接入设置", new Vector2(350, 99), new Vector2(260, 44), FactoryGuideUI.CardColor, OpenSettings);
        objective.rectTransform.sizeDelta = new Vector2(650, 46); objective.rectTransform.anchoredPosition = new Vector2(-155, 99);
        settingsPanel = FactoryGuideUI.Box(overlay.transform, "DeepSeekSettings", Vector2.zero, new Vector2(880, 650), FactoryGuideUI.PanelColor);
        settingsPanel.GetComponent<Image>().raycastTarget = true;
        FactoryGuideUI.Text(settingsPanel, "Title", "连接 DeepSeek", new Vector2(0, 255), new Vector2(760, 60), 32, FactoryGuideUI.Mint);
        FactoryGuideUI.Text(settingsPanel, "KeyLabel", "API 密钥", new Vector2(0, 175), new Vector2(760, 42), 22, FactoryGuideUI.Ink);
        keyInput = SettingsInput("APIKey", new Vector2(0, 115), true);
        FactoryGuideUI.Text(settingsPanel, "ModelLabel", "模型名称", new Vector2(0, 45), new Vector2(760, 42), 22, FactoryGuideUI.Ink);
        modelInput = SettingsInput("Model", new Vector2(0, -15), false);
        FactoryGuideUI.Text(settingsPanel, "Help", "密钥仅用于本次游戏，不保存。\n发送内容包含对话与设备状态，调用将消耗 API 额度。", new Vector2(0, -135), new Vector2(760, 150), 20, FactoryGuideUI.Muted);
        FactoryGuideUI.Button(settingsPanel, "Save", "使用DeepSeek", new Vector2(-190, -265), new Vector2(320, 56), FactoryGuideUI.Mint, () => {
            sessionKey = keyInput.text.Trim(); model = string.IsNullOrWhiteSpace(modelInput.text) ? FactoryDeepSeekClient.DefaultModel : modelInput.text.Trim();
            keyInput.text = ""; useDeepSeek = true; answer = null; ApplyButton.interactable = false; layout.text = objective.text = "";
            settingsPanel.gameObject.SetActive(false); card.gameObject.SetActive(true); RefreshProvider(); result.text = "已启用DeepSeek，可以开始对话。";
        }, true);
        FactoryGuideUI.Button(settingsPanel, "Back", "返回", new Vector2(210, -265), new Vector2(240, 56), FactoryGuideUI.CardColor,
            () => { keyInput.text = ""; settingsPanel.gameObject.SetActive(false); card.gameObject.SetActive(true); });
        settingsPanel.gameObject.SetActive(false);
        RefreshProvider();
    }
    TMP_InputField SettingsInput(string name, Vector2 position, bool secret)
    {
        var box = FactoryGuideUI.Box(settingsPanel, name, position, new Vector2(760, 62), FactoryGuideUI.CardColor);
        box.GetComponent<Image>().raycastTarget = true;
        var text = FactoryGuideUI.Text(box, "Text", "", Vector2.zero, new Vector2(730, 54), 22, FactoryGuideUI.Ink); text.richText = false;
        var input = box.gameObject.AddComponent<TMP_InputField>(); input.textViewport = box; input.textComponent = text; input.fontAsset = text.font;
        input.customCaretColor = true; input.caretColor = FactoryGuideUI.Ink; input.selectionColor = new Color(.94f, .98f, .03f, .35f); input.pointSize = 22; input.characterLimit = secret ? 512 : 100; input.richText = false;
        input.contentType = secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        return input;
    }
    void OpenSettings()
    {
        if (networkRoutine != null) return;
        keyInput.text = sessionKey; modelInput.text = model;
        card.gameObject.SetActive(false); settingsPanel.gameObject.SetActive(true);
        FactoryGuideUI.Fit(settingsPanel, canvas, new Vector2(880, 650), 32);
    }
    void RefreshProvider()
    {
        title.text = useDeepSeek ? "AI 助手" : "方案规划";
        introduction.text = useDeepSeek ? "咨询操作、分析当前产线、连续追问，或让我推荐方案并帮助摆放。" : "输入产量和时间目标；助手比较真实生产验证过的布局，并选择候选中成本最低的可行方案。";
        providerButton.GetComponentInChildren<TMP_Text>().text = useDeepSeek ? "离线规划" : "AI 对话";
        RefreshChatMode();
    }
    void BeginDeepSeek()
    {
        if (string.IsNullOrWhiteSpace(QuestionInput.text)) { applyHint.text = "请输入问题，例如“怎么上料？”或“推荐最佳方案”。"; return; }
        string key = string.IsNullOrWhiteSpace(sessionKey) ? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") : sessionKey;
        if (string.IsNullOrWhiteSpace(key)) { AddChat("系统", "请点击“接入设置”输入DeepSeek密钥，密钥只保留在本次运行中。", false); return; }
        if (key.Contains("\r") || key.Contains("\n")) { AddChat("系统", "密钥中包含换行，请重新输入。", false); return; }
        var evidence = new StringBuilder(BuildGameSnapshot());
        var factory = FactoryProduction.Instance;
        evidence.AppendLine("当前机械臂移动/取放/旋转倍率：" + factory.armTravelMultiplier + "/" + factory.armPickupMultiplier + "/" + factory.armRotationMultiplier);
        if (catalog?.profiles != null) foreach (var p in catalog.profiles)
        {
            if (p?.plan?.devices == null) continue;
            long cost = 0; int[] counts = new int[5];
            foreach (var d in p.plan.devices) if (d.kind >= 0 && d.kind < 5) { cost += PlacementSystem.EquipmentPrice(d.kind); counts[d.kind]++; }
            evidence.AppendLine(p.key + ":设备[冲床,折弯,焊接,机械臂,滑槽]=" + string.Join(",", counts) + ";成本=" + cost + ";入库秒=" +
                (p.receivedAt == null ? "无" : string.Join(",", Array.ConvertAll(p.receivedAt, x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))));
            if (p.sharedUsage != null && p.sharedUsage.Length > 0) evidence.AppendLine("多轨共用实测：" + string.Join("；", p.sharedUsage));
            if (p.punchCompletedAt != null && p.punchCompletedAt.Length > 1)
                evidence.AppendLine("单冲床出料：首件=" + p.punchCompletedAt[0].ToString("0.##") + "秒；第" + p.punchCompletedAt.Length + "件=" + p.punchCompletedAt[p.punchCompletedAt.Length - 1].ToString("0.##") + "秒，尚不算最终入库。");
        }
        pendingQuestion = QuestionInput.text.Trim(); AddChat("玩家", pendingQuestion, false);
        CalculateButton.interactable = QuestionInput.interactable = providerButton.interactable = settingsButton.interactable = clearChatButton.interactable = false;
        result.text = "DeepSeek正在分析目标与候选方案…\n可点击关闭取消等待。";
        applyHint.text = "DeepSeek正在回答… 可关闭取消。" + (level.IsRunning ? "生产仍继续计时。" : "");
        networkRoutine = StartCoroutine(RequestDeepSeek(key, evidence.ToString()));
    }
    IEnumerator RequestDeepSeek(string key, string evidence)
    {
        // Ensure even immediate transport errors do not leave a stale Coroutine handle.
        yield return null;
        yield return deepSeek.Solve(key, model, pendingQuestion, evidence, HandleDeepSeekAnswer, chatHistory);
        networkRoutine = null;
        CalculateButton.interactable = QuestionInput.interactable = providerButton.interactable = settingsButton.interactable = clearChatButton.interactable = true;
    }
    void HandleDeepSeekAnswer(FactoryDeepSeekClient.Intent intent, string error)
    {
        if (!IsOpen) return;
        answer = null; ApplyButton.interactable = false; layout.text = objective.text = "";
        if (intent == null) { AddChat("系统", error, false); pendingQuestion = null; applyHint.text = "请求失败未修改场景；可重试或切换离线规划。"; return; }
        string text = intent.reply;
        if (intent.action == "plan")
        {
            bool sharedPunch = intent.sharedPunch || IsSharedPunchRequest(pendingQuestion);
            bool hasCount = System.Text.RegularExpressions.Regex.IsMatch(pendingQuestion ?? "", "[0-9零一二三四五六七八九十百两.]+\\s*(件|个)");
            ShowVerifiedAnswer(intent.quantity, intent.seconds, sharedPunch, intent.maximize || sharedPunch && !hasCount);
            text += "\n\n游戏复核：\n" + objective.text + "\n" + result.text;
            if (answer?.best != null) text += "\n" + layout.text;
            if (answer?.best?.sharedUsage != null && answer.best.sharedUsage.Length > 0) text += "\n共用设备实测：" + string.Join("；", answer.best.sharedUsage);
            text += "\n" + applyHint.text;
        }
        else applyHint.text = ChatPhaseHint();
        if (!string.IsNullOrWhiteSpace(pendingQuestion)) chatHistory.Add(new FactoryDeepSeekClient.Message { role = "user", content = pendingQuestion });
        pendingQuestion = null;
        AddChat("助手", text, true); QuestionInput.text = "";
    }
    void CancelDeepSeek()
    {
        deepSeek.Cancel();
        if (networkRoutine != null) { StopCoroutine(networkRoutine); networkRoutine = null; AddChat("系统", "已取消等待；没有修改场景。", false); }
        pendingQuestion = null;
        if (providerButton != null) providerButton.interactable = settingsButton.interactable = true;
        if (clearChatButton != null) clearChatButton.interactable = true;
        if (keyInput != null) keyInput.text = "";
    }
}


