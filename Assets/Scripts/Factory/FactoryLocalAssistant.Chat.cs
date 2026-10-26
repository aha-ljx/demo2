using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class FactoryLocalAssistant
{
    readonly List<FactoryDeepSeekClient.Message> chatHistory = new List<FactoryDeepSeekClient.Message>();
    readonly List<string> chatLines = new List<string>();
    RectTransform chatPanel;
    ScrollRect chatScroll;
    TMP_Text chatText;
    Button clearChatButton;
    TMP_Text questionPlaceholder;
    string pendingQuestion;

    void InitializeChat()
    {
        chatPanel = FactoryGuideUI.Box(card, "Conversation", new Vector2(0, 55), new Vector2(960, 360), FactoryGuideUI.CardColor);
        chatPanel.GetComponent<Image>().raycastTarget = true;
        chatPanel.gameObject.AddComponent<RectMask2D>();
        chatText = FactoryGuideUI.Text(chatPanel, "Transcript", "", Vector2.zero, new Vector2(928, 336), 22, FactoryGuideUI.Ink,
            TextAlignmentOptions.TopLeft);
        chatText.richText = false; chatText.overflowMode = TextOverflowModes.Overflow;
        var rect = chatText.rectTransform;
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.offsetMin = new Vector2(16, 0); rect.offsetMax = new Vector2(-16, -12);
        chatScroll = chatPanel.gameObject.AddComponent<ScrollRect>();
        chatScroll.viewport = chatPanel; chatScroll.content = rect; chatScroll.horizontal = false;
        chatScroll.movementType = ScrollRect.MovementType.Clamped; chatScroll.scrollSensitivity = 40;
        clearChatButton = FactoryGuideUI.Button(card, "ClearChat", "清空对话", new Vector2(170, 276), new Vector2(160, 44), FactoryGuideUI.CardColor, () => {
            chatHistory.Clear(); chatLines.Clear(); answer = lastRecommendedAnswer = null; ApplyButton.interactable = false; pendingQuestion = null; QuestionInput.text = "";
            AddChat("助手", "你好！可以问我操作、设备、产线堵塞或收益问题，也可以让我推荐并摆放方案。", false);
        });
        AddChat("助手", "可以问操作、分析产线，或说“帮我摆放最佳方案”。", false);
        questionPlaceholder = FactoryGuideUI.Text(QuestionInput.transform, "Placeholder", "输入问题，例如：机械臂为什么不动？", Vector2.zero, new Vector2(920, 86), 22, FactoryGuideUI.Muted);
        QuestionInput.placeholder = questionPlaceholder;
        QuestionInput.text = "";
    }
    void RefreshChatMode()
    {
        if (chatPanel == null) return;
        chatPanel.gameObject.SetActive(useDeepSeek); clearChatButton.gameObject.SetActive(useDeepSeek);
        ApplyButton.gameObject.SetActive(!useDeepSeek || ApplyButton.interactable);
        objective.gameObject.SetActive(!useDeepSeek); result.gameObject.SetActive(!useDeepSeek); layout.gameObject.SetActive(!useDeepSeek);
        var input = QuestionInput.GetComponent<RectTransform>();
        input.anchoredPosition = useDeepSeek ? new Vector2(0, -192) : new Vector2(0, 185);
        introduction.gameObject.SetActive(false);
        clearChatButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-400, 276);
        introduction.rectTransform.sizeDelta = useDeepSeek ? new Vector2(550, 64) : new Vector2(960, 64);
        introduction.rectTransform.anchoredPosition = useDeepSeek ? new Vector2(-205, 276) : new Vector2(0, 276);
        settingsButton.GetComponent<RectTransform>().anchoredPosition = useDeepSeek ? new Vector2(375, 276) : new Vector2(350, 99);
        settingsButton.GetComponent<RectTransform>().sizeDelta = useDeepSeek ? new Vector2(200, 44) : new Vector2(260, 44);
        CalculateButton.GetComponentInChildren<TMP_Text>().text = useDeepSeek ? "发送" : "计算方案";
        if (questionPlaceholder != null) questionPlaceholder.text = useDeepSeek ? "输入问题，例如：机械臂为什么不动？" : "例如：600秒内得到10件产物，成本最低";
        applyHint.rectTransform.anchoredPosition = useDeepSeek ? new Vector2(0, -275) : new Vector2(0, -258);
        applyHint.rectTransform.sizeDelta = useDeepSeek ? new Vector2(960, 52) : new Vector2(960, 70);
        applyHint.fontSize = useDeepSeek ? 18 : 20;
        applyHint.text = useDeepSeek ? ChatPhaseHint() : "规划阶段输入产量和时间，比较已验证候选。";
        RefreshTranscript();
    }
    string ChatPhaseHint() => level.IsEnded ? "已结算 · 仅咨询" : level.IsRunning ? "生产中 · 计时继续" : "规划中 · 支持自动摆放";
    void AddChat(string speaker, string text, bool remember)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (remember)
        {
            chatHistory.Add(new FactoryDeepSeekClient.Message { role = speaker == "玩家" ? "user" : "assistant", content = text });
            while (chatHistory.Count > 16) chatHistory.RemoveAt(0);
        }
        chatLines.Add(speaker + "：\n" + text);
        while (chatLines.Count > 24) chatLines.RemoveAt(0);
        RefreshTranscript();
    }
    void RefreshTranscript()
    {
        if (chatText == null || chatPanel == null) return;
        chatText.text = string.Join("\n\n", chatLines);
        float width = Mathf.Max(100, chatPanel.rect.width - 32);
        chatText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(chatPanel.rect.height - 24, chatText.GetPreferredValues(chatText.text, width, 0).y + 24));
        Canvas.ForceUpdateCanvases(); chatScroll.verticalNormalizedPosition = 0;
    }
    string BuildGameSnapshot()
    {
        var factory = FactoryProduction.Instance;
        var stock = FindObjectOfType<RawMaterialSupply>();
        var text = new StringBuilder("当前关卡：第一关；阶段：" + (level.IsEnded ? "已结算" : level.IsRunning ? "生产中" : "规划中"));
        text.AppendLine("；快照剩余秒数=" + level.RemainingSeconds.ToString("0.0") + "；库存原料=" + (stock == null ? "未知" : stock.Remaining.ToString()) +
            "；实际入库=" + factory.Stored + "；当前建设成本=" + placement.CurrentCost + "；已放设备=" + placement.PlacedCount + "；滑槽=" + factory.Rails.Count);
        text.AppendLine("机械臂移动/取放/旋转倍率=" + factory.armTravelMultiplier + "/" + factory.armPickupMultiplier + "/" + factory.armRotationMultiplier);
        if (answer?.best != null) text.AppendLine("上一方案目标：" + answer.quantity + "件/" + answer.deadline + "秒，成本=" + answer.cost);
        if (factory.Statistics != null)
        {
            text.AppendLine("入库速率=" + factory.Statistics.ProductRate.ToString("0.##") + "件/分钟；设备状态快照（最多64台）：");
            int count = 0;
            foreach (var module in factory.Statistics.Modules)
            {
                if (module.Removed || module.Root == null) continue;
                if (count++ >= 64) { text.AppendLine("其余设备省略"); break; }
                text.Append(module.Name + ":" + module.State + ";速率=" + (module.Kind == 3 ? module.MovementRate : module.Rate).ToString("0.##") + module.Unit);
                var station = module.Root.GetComponent<FactoryStation>();
                if (station != null) text.Append(";输入排队=" + station.Input.Count + ";输出缓存=" + station.OutputCount + "/" + station.OutputCapacity + ";待送入=" + station.Incoming);
                var arm = module.Root.GetComponent<FactoryArm>();
                if (arm != null)
                {
                    text.Append(";移动设定=" + arm.RailSpeed.ToString("0.##") + ";持件=" + arm.Holding + ";可达加工设备=");
                    int n = 0; foreach (var s in factory.Stations) { if (++n > 64) break; if (arm.CanReach(s.Ports.DockPosition, out _)) text.Append(s.name + "、"); }
                }
                text.AppendLine();
            }
        }
        text.AppendLine("注意：数据为提问瞬间快照，生产中稍后可能改变。可达加工停靠区不能保证出库停靠区可达；尚未证明具体死锁原因。");
        return text.ToString();
    }
}
