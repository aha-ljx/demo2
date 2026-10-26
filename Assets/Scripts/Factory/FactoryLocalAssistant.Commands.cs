using System.Text.RegularExpressions;

public sealed partial class FactoryLocalAssistant
{
    FactoryAssistantAnswer lastRecommendedAnswer;
    public static bool IsSharedPunchRequest(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || Regex.IsMatch(question, "两台冲床|2台冲床|双冲床|(?:不要|不用|不需).{0,8}(?:共用|共享).{0,6}冲床")) return false;
        return Regex.IsMatch(question, "(?:共用|共享|双轨).{0,8}冲床|冲床.{0,8}(?:共用|共享|双轨|两条|并行)|单冲床|一台冲床");
    }

    // Read the player's actual command; model prose alone cannot authorize scene changes.
    public static bool IsPlacementCommand(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;
        foreach (string clause in Regex.Split(question, "[，。！？；,!?;\\n]+"))
        {
            string text = Regex.Replace(clause, @"\s+", "");
            if (Regex.IsMatch(text, "怎么|如何|怎样|为什么|分析|解释|区别|教程")) continue;
            if (Regex.IsMatch(text, "(?:不要|不用|不需要|先别|暂不|别|不许|禁止).{0,16}(?:摆|放置|布置|搭建|采用|应用|执行)")) continue;
            if (Regex.IsMatch(text, "(?:帮我|替我|给我|请|直接|自动|立即).{0,16}(?:摆放|布置|搭建|放置|摆出来|摆好|摆上|摆一下)")) return true;
            if (Regex.IsMatch(text, "(?:采用|应用|使用|执行).{0,12}(?:方案|这个|那个|刚才|推荐)")) return true;
            if (Regex.IsMatch(text, "^(?:开始)?(?:摆放|布置|搭建)(?:吧|一下|这个方案|方案)?$")) return true;
        }
        return false;
    }

    bool ExecutePlacementCommand(string question)
    {
        if (!IsPlacementCommand(question)) return false;
        answer = null; ApplyButton.interactable = false;
        AddChat("玩家", question.Trim(), true);
        if (lastRecommendedAnswer == null && Regex.IsMatch(question, "刚才|这个|那个|上个|上面|上述"))
        {
            AddChat("助手", "未摆放：目前没有可采用的已验证方案。请先请求推荐方案，或明确说“帮我摆放单冲床双轨最优方案”。", true);
            QuestionInput.text = ""; return true;
        }
        if (!placement.CanLoadPlan(out string error))
        {
            applyHint.text = "未摆放：" + error;
            AddChat("助手", applyHint.text + "。没有修改当前产线。", true);
            QuestionInput.text = ""; return true;
        }
        int fallbackCount = lastRecommendedAnswer != null ? lastRecommendedAnswer.quantity : 10;
        float fallbackTime = lastRecommendedAnswer != null ? lastRecommendedAnswer.deadline : FactoryLevelSession.Duration;
        bool hasCount = Regex.IsMatch(question, "[0-9零一二三四五六七八九十百两.]+\\s*(件|个)");
        bool sharedPunch = IsSharedPunchRequest(question) || lastRecommendedAnswer != null && lastRecommendedAnswer.sharedPunchRequired &&
            !Regex.IsMatch(question, "(?:不要|不用|不需).{0,8}(?:共用|共享).{0,6}冲床|两台冲床|双冲床");
        string target = hasCount ? question : question + " " + fallbackCount + "件";
        if (!FactoryAssistantSolver.Parse(target, out int quantity, out float seconds, out error))
        {
            AddChat("助手", "未摆放：" + error, true); QuestionInput.text = ""; return true;
        }
        if (!Regex.IsMatch(question, "秒|分钟|小时|[0-9零一二三四五六七八九十百两]分|[0-9]\\s*(?:s|min|h)", RegexOptions.IgnoreCase)) seconds = fallbackTime;
        answer = null; ApplyButton.interactable = false;
        ShowVerifiedAnswer(quantity, seconds, sharedPunch, sharedPunch && !hasCount);
        if (answer?.best == null)
        {
            AddChat("助手", "未摆放：" + result.text, true); QuestionInput.text = ""; return true;
        }
        Apply();
        QuestionInput.text = "";
        return true;
    }
}
