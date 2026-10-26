using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FactoryDeepSeekAudit
{
    const string Pending = "FactoryDeepSeekAudit.Pending";
    static FactoryDeepSeekAudit()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var root = new GameObject("DeepSeek integration audit"); UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<FactoryDeepSeekAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run DeepSeek Integration Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}

public sealed class FactoryDeepSeekAuditRunner : MonoBehaviour
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder(); bool done;
    void OnEnable() { Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) { report.AppendLine(message); Finish(false); } }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    IEnumerator Start()
    {
        Check(FactoryLocalAssistant.IsPlacementCommand("请帮我摆放最佳方案") && FactoryLocalAssistant.IsPlacementCommand("采用刚才的方案") &&
            FactoryLocalAssistant.IsPlacementCommand("推荐一个方案并直接摆出来") && !FactoryLocalAssistant.IsPlacementCommand("怎么摆放设备？") &&
            !FactoryLocalAssistant.IsPlacementCommand("不要帮我摆放") && !FactoryLocalAssistant.IsPlacementCommand("只推荐方案，不要自动摆放") &&
            !FactoryLocalAssistant.IsPlacementCommand("分析如何采用这个方案"), "explicit player commands authorize placement while questions and negative instructions remain read-only");
        Check(FactoryLocalAssistant.IsSharedPunchRequest("单冲床双轨最优方案") && FactoryLocalAssistant.IsSharedPunchRequest("一台冲床接两条滑槽") &&
            !FactoryLocalAssistant.IsSharedPunchRequest("两台冲床并行") && !FactoryLocalAssistant.IsSharedPunchRequest("不要共用冲床"),
            "shared-punch constraint does not misclassify two independent punches or a negative request");
        string body = FactoryDeepSeekClient.BuildRequest(FactoryDeepSeekClient.DefaultModel, "600秒10件\n\"json\"", "真实记录");
        Check(body.Contains("json_object") && body.Contains("thinking") && !body.Contains("Bearer") && body.Contains("\\n"), "request uses official JSON and thinking format, escapes user input and excludes credentials from body");
        Check(body.Contains("\"type\":\"disabled\"") && body.Contains("\"reasoning_effort\":\"none\"") && body.Contains("\"max_tokens\":8192") && body.Contains("黄线"),
            "ordinary chat avoids hidden thinking-budget exhaustion and includes the real yellow-boundary rule");
        string content = "{\"action\":\"plan\",\"reply\":\"我会比较600秒10件的已验证候选。共用设备可能降低成本，但须验证排队。\",\"quantity\":10,\"seconds\":600}";
        string response = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":" + Quote(content) + "}}]}";
        Check(FactoryDeepSeekClient.TryRead(response, out var intent, out _) && intent.quantity == 10 && intent.seconds == 600, "valid DeepSeek response becomes a typed production objective");
        string chatResponse = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":" + Quote("{\"action\":\"chat\",\"reply\":\"规划阶段直接点击冲床，在详情里选择上料数量，不需要先点击原料堆。\"}") + "}}]}";
        Check(FactoryDeepSeekClient.TryRead(chatResponse, out var chat, out _) && chat.quantity == 0 && chat.action == "chat", "ordinary gameplay questions accept full answers without quantity or deadline fields");
        Check(FactoryDeepSeekClient.TryRead(Envelope("```json\n{\"action\":\"chat\",\"reply\":\"黄线是原料放置边界。\"}\n```"), out var fenced, out _) && fenced.action == "chat" &&
            FactoryDeepSeekClient.TryRead(Envelope("黄线是原料放置边界，原料应留在原料堆一侧。"), out var plain, out _) && plain.action == "chat" &&
            FactoryDeepSeekClient.TryRead(Envelope("{\"answer\":\"原料不得跨黄线。\"}"), out var alias, out _) && alias.action == "chat",
            "complete fenced JSON, plain-language answers and harmless answer aliases display as read-only chat");
        Check(FactoryDeepSeekClient.TryRead(Envelope("{\"action\":\"place_all\",\"reply\":\"请先检查方案。\",\"quantity\":10,\"seconds\":600}"), out var unknown, out _) && unknown.action == "chat" &&
            !FactoryDeepSeekClient.TryRead(Envelope("{\"action\":\"plan\",\"reply\":\"摆放\",\"quantity\":10,"), out _, out _) &&
            !FactoryDeepSeekClient.TryRead(Envelope("", "length"), out _, out string lengthError) && lengthError.Contains("输出上限"),
            "unknown actions never grant placement and truncated plans remain rejected with an explicit reason");
        int attempts = 0; FactoryDeepSeekClient.Intent recovered = null; string retryError = null;
        var recovery = new FactoryDeepSeekClient();
        Func<string, string, Action<string, string>, IEnumerator> recoverTransport = (key, payload, done) => {
            attempts++; return Fixture(attempts == 1 ? Envelope("") : chatResponse, null, done);
        };
        recovery.GetType().GetField("TransportOverride", Hidden).SetValue(recovery, recoverTransport);
        yield return recovery.Solve("test-only-not-a-real-key", FactoryDeepSeekClient.DefaultModel, "黄线是什么意思？", "规则", (i, e) => { recovered = i; retryError = e; });
        Check(attempts == 2 && recovered?.action == "chat" && retryError == null && recovery.LastDiagnostic.Contains("attempt=2") && !recovery.LastDiagnostic.Contains("test-only"),
            "an empty completion is automatically retried once and diagnostics exclude credentials and conversation content");
        attempts = 0; recovered = null;
        Func<string, string, Action<string, string>, IEnumerator> truncatedTransport = (key, payload, done) => { attempts++; return Fixture(Envelope("", "length"), null, done); };
        recovery.GetType().GetField("TransportOverride", Hidden).SetValue(recovery, truncatedTransport);
        yield return recovery.Solve("test-only", FactoryDeepSeekClient.DefaultModel, "问题", "规则", (i, e) => { recovered = i; retryError = e; });
        Check(attempts == 2 && recovered == null && retryError.Contains("输出上限") && retryError.Contains("已自动重试一次"), "repeated truncation stops after exactly one retry with an actionable explanation");
        attempts = 0;
        Func<string, string, Action<string, string>, IEnumerator> authTransport = (key, payload, done) => { attempts++; return Fixture(null, "密钥无效", done); };
        recovery.GetType().GetField("TransportOverride", Hidden).SetValue(recovery, authTransport);
        yield return recovery.Solve("test-only", FactoryDeepSeekClient.DefaultModel, "问题", "规则", (i, e) => retryError = e);
        Check(attempts == 1 && retryError == "密钥无效", "authentication and transport failures are not silently retried");
        var memory = new List<FactoryDeepSeekClient.Message> { new FactoryDeepSeekClient.Message { role = "user", content = "我想600秒10件" }, new FactoryDeepSeekClient.Message { role = "assistant", content = "已给出方案" } };
        string followup = FactoryDeepSeekClient.BuildRequest(FactoryDeepSeekClient.DefaultModel, "刚才那个为什么需要两台折弯机？", "当前实际入库3件", memory);
        Check(followup.Contains("我想600秒10件") && followup.Contains("已给出方案") && followup.Contains("当前实际入库3件") && followup.Contains("刚才那个"), "follow-up API requests carry previous user and assistant turns plus a fresh game snapshot");
        Check(!FactoryDeepSeekClient.TryRead(response.Replace("\"stop\"", "\"length\""), out _, out _) &&
            !FactoryDeepSeekClient.TryRead("{}", out _, out _) && !FactoryDeepSeekClient.TryRead("not json", out _, out _) &&
            !FactoryDeepSeekClient.TryRead(response.Replace("600", "601"), out _, out _), "truncated, malformed, empty and out-of-level responses cannot become plans");
        bool missing = false; yield return new FactoryDeepSeekClient().Solve("", FactoryDeepSeekClient.DefaultModel, "10件", "", (i, e) => missing = i == null && !string.IsNullOrEmpty(e));
        Check(missing, "missing API key fails locally without a paid network request");
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>(); menu.StartGame(); menu.FirstLevelButton.onClick.Invoke();
        yield return null; yield return null; yield return null;
        FindObjectOfType<FactoryTutorial>().Close(); yield return null; yield return null;
        var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        var assistant = builder.Assistant; assistant.Open();
        Check(Field<bool>(assistant, "useDeepSeek") && Field<RectTransform>(assistant, "chatPanel").gameObject.activeSelf, "assistant defaults to a general conversation interface");
        Call(assistant, "OpenSettings");
        var secret = Field<TMP_InputField>(assistant, "keyInput");
        Check(secret.contentType == TMP_InputField.ContentType.Password && FactoryLocalAssistant.BlocksGameplay, "settings mask the API key and keep gameplay blocked");
        Capture("settings", 1280, 720);
        secret.text = "test-only-not-a-real-key";
        Field<RectTransform>(assistant, "settingsPanel").Find("Save").GetComponent<Button>().onClick.Invoke();
        Check(Field<bool>(assistant, "useDeepSeek") && secret.text == "", "settings enable DeepSeek and clear secret text from the hidden input");
        Call(assistant, "LoadCatalog");
        Reply(assistant, "怎么给冲床上料？", chat);
        Check(assistant.Answer == null && !assistant.ApplyButton.interactable && Field<TMP_Text>(assistant, "chatText").text.Contains("不需要先点击原料堆"), "ordinary question is answered directly without invoking the production solver or placement");
        Capture("conversation", 1280, 720);
        Reply(assistant, "推荐最佳方案", intent);
        var verifiedCatalog = JsonUtility.FromJson<FactoryAssistantCatalog>(Resources.Load<TextAsset>("FactoryAssistant/VerifiedPlans").text);
        var actualFactory = FactoryProduction.Instance;
        var reference = FactoryAssistantSolver.Solve(10, 600, verifiedCatalog, builder.AssistantScale, actualFactory.armTravelMultiplier, actualFactory.armPickupMultiplier, actualFactory.armRotationMultiplier);
        var sharedPunchBest = FactoryAssistantSolver.SolveBestSharedPunch(600, verifiedCatalog, builder.AssistantScale, actualFactory.armTravelMultiplier, actualFactory.armPickupMultiplier, actualFactory.armRotationMultiplier);
        Check(sharedPunchBest.best != null && sharedPunchBest.sharedPunchRequired && sharedPunchBest.maximizeQuantity &&
            Array.FindAll(sharedPunchBest.best.plan.devices, d => d.kind == 0).Length == 1 && Array.Exists(sharedPunchBest.best.sharedUsage, x => x.StartsWith("冲床", StringComparison.Ordinal)),
            "shared-punch optimization requires actual two-arm punch usage and prioritizes maximum quantity before cost");
        Check(FactoryAssistantSolver.Solve(10, 600, verifiedCatalog, builder.AssistantScale, 3, 3, 2, true).best == null && reference.best != null,
            "an infeasible ten-piece single-punch query never silently substitutes the two-punch maximum-output plan");
        Check(assistant.Answer?.best != null && assistant.Answer.cost == reference.cost && assistant.ApplyButton.interactable && builder.PlacedCount == 0,
            "model intent is re-solved against verified timing and current prices, with no automatic scene mutation");
        Capture("answer", 1280, 720);
        Capture("answer-4x3", 1024, 768);
        Reply(assistant, "600秒11件能完成吗？", new FactoryDeepSeekClient.Intent { action = "plan", quantity = 11, seconds = 600, reply = "我来检查" });
        Check(assistant.Answer.provenImpossible && !assistant.ApplyButton.interactable, "model advice cannot override the actual raw-material limit");
        assistant.QuestionInput.text = "采用刚才的方案"; assistant.Calculate();
        Check(builder.PlacedCount == 0 && assistant.Answer == null && !assistant.ApplyButton.interactable && Field<Coroutine>(assistant, "networkRoutine") == null,
            "adopting a nonexistent or rejected recommendation never falls back to another topology");
        int remembered = Field<List<FactoryDeepSeekClient.Message>>(assistant, "chatHistory").Count;
        assistant.Close(); assistant.Open();
        Check(assistant.CalculateButton.interactable && assistant.QuestionInput.interactable && !assistant.ApplyButton.interactable,
            "closing and reopening resets results and restores controls");
        Check(Field<List<FactoryDeepSeekClient.Message>>(assistant, "chatHistory").Count == remembered, "conversation memory survives closing and reopening the assistant");
        assistant.QuestionInput.text = "为什么机械臂不动？";
        assistant.Calculate(); assistant.Close(); yield return null;
        Check(Field<Coroutine>(assistant, "networkRoutine") == null && builder.PlacedCount == 0, "closing cancels an in-flight request and leaves the scene unchanged");
        assistant.Open();
        Field<Button>(assistant, "providerButton").onClick.Invoke(); assistant.QuestionInput.text = "600秒10件"; assistant.Calculate();
        Check(assistant.Answer?.best != null && assistant.ApplyButton.interactable, "offline solver remains available through the mode switch");
        Field<Button>(assistant, "providerButton").onClick.Invoke();
        assistant.QuestionInput.text = "帮我摆放600秒11件的方案"; assistant.Calculate();
        Check(builder.PlacedCount == 0 && assistant.Answer.provenImpossible && Field<Coroutine>(assistant, "networkRoutine") == null,
            "infeasible automatic-placement commands do not place anything or require an API request");
        Reply(assistant, "推荐590秒10件的方案", new FactoryDeepSeekClient.Intent { action = "plan", reply = "我来比较", quantity = 10, seconds = 590 });
        Check(builder.PlacedCount == 0, "merely requesting a recommendation does not automatically place it");
        assistant.GetType().GetField("sessionKey", Hidden).SetValue(assistant, "");
        assistant.Close(); assistant.Open();
        assistant.QuestionInput.text = "采用刚才的方案"; assistant.Calculate();
        Check(builder.PlacedCount == assistant.Answer.best.plan.devices.Length && assistant.CalculateButton.interactable && assistant.QuestionInput.interactable && !assistant.ApplyButton.interactable,
            "sending an adoption command places all devices without a button click and leaves conversation available");
        Check(assistant.Answer.deadline == 590 && Field<Coroutine>(assistant, "networkRoutine") == null && !FactoryLevelSession.Active.IsRunning,
            "placement inherits the last recommended objective across reopening, needs no API key and does not start production");
        Capture("auto-placed", 1280, 720);
        CaptureLayout();
        if (assistant.Answer.best.sharedUsage != null && Array.Exists(assistant.Answer.best.sharedUsage, x => x.StartsWith("折弯机", StringComparison.Ordinal)))
        {
            var bend = FactoryProduction.Instance.Stations.Find(s => s.Kind == 1);
            var connected = FactoryProduction.Instance.Arms.FindAll(a => a.CanReach(bend.Ports.DockPosition, out _));
            Check(connected.Count >= 2 && !FactoryProduction.Instance.Graph.Same(connected[0].Rail, connected[1].Rail), "the auto-placed shared folding machine is reachable from two independent arm networks");
        }
        int placedOnce = builder.PlacedCount;
        assistant.QuestionInput.text = "帮我摆放最佳方案"; assistant.Calculate();
        Check(builder.PlacedCount == placedOnce && FindObjectOfType<RawMaterialSupply>().Remaining == 0 && !assistant.ApplyButton.interactable,
            "repeated commands do not duplicate devices, consume more stock or overwrite the current scene");
        string snapshot = (string)assistant.GetType().GetMethod("BuildGameSnapshot", Hidden).Invoke(assistant, null);
        Check(snapshot.Contains("冲床1") && snapshot.Contains("输出缓存") && snapshot.Contains("持件=") && snapshot.Contains("库存原料=0"), "context includes real named machines, buffer counts, arm holding states and remaining raw materials");
        assistant.Close(); yield return null; yield return null; yield return null;
        var level = FactoryLevelSession.Active; level.BeginProduction(); yield return null; yield return null;
        assistant.Open(); float before = level.RemainingSeconds; yield return null; yield return null;
        Check(assistant.IsOpen && assistant.AssistantButton.gameObject.activeSelf && level.RemainingSeconds < before && FactoryLevelSession.CanSimulate,
            "assistant opens during production and does not pause or reset the simulation clock");
        Reply(assistant, "现在冲床为什么等待？", new FactoryDeepSeekClient.Intent { action = "chat", reply = "请看冲床1的输出缓存和机械臂状态；缓存满时会等待取件。该原因仍需结合当时的连接与状态检查。" });
        Check(assistant.Answer == null && !assistant.ApplyButton.interactable, "runtime troubleshooting does not create an actionable layout");
        Reply(assistant, "现在帮我换最佳方案", intent);
        int devices = builder.PlacedCount; assistant.Apply();
        Check(!assistant.ApplyButton.interactable && builder.PlacedCount == devices && level.IsRunning, "plan requests during production can be discussed but cannot replace placed devices");
        assistant.QuestionInput.text = "自动摆放最佳方案"; assistant.Calculate();
        Check(builder.PlacedCount == devices && level.IsRunning && Field<Coroutine>(assistant, "networkRoutine") == null,
            "actual automatic-placement commands during production are rejected locally without changing equipment");
        Capture("running-conversation", 1280, 720);
        level.GetType().GetMethod("Advance", Hidden | BindingFlags.Public).Invoke(level, new object[] { 600f }); yield return null;
        Check(assistant.IsOpen && level.IsEnded, "conversation remains accessible when production reaches settlement");
        assistant.Close(); yield return null; assistant.Open();
        Check(assistant.IsOpen, "assistant can reopen for post-run questions above the settlement interface");
        for (int i = 0; i < 12; i++) Reply(assistant, "追问" + i, chat);
        Check(Field<List<FactoryDeepSeekClient.Message>>(assistant, "chatHistory").Count <= 16 && Field<TMP_Text>(assistant, "chatText").rectTransform.rect.height > Field<RectTransform>(assistant, "chatPanel").rect.height,
            "bounded multi-turn memory and a scrollable transcript handle long conversations");
        var scroll = Field<ScrollRect>(assistant, "chatScroll"); scroll.verticalNormalizedPosition = 1; Vector2 top = scroll.content.anchoredPosition;
        scroll.verticalNormalizedPosition = 0;
        Check(Vector2.Distance(top, scroll.content.anchoredPosition) > 100, "conversation scroll control can navigate from the oldest visible reply to the newest");
        Field<Button>(assistant, "clearChatButton").onClick.Invoke();
        Check(Field<List<FactoryDeepSeekClient.Message>>(assistant, "chatHistory").Count == 0 && !assistant.ApplyButton.interactable, "clear conversation removes memory and any old actionable plan");
        MainMenuController.RequestFirstLevel(); UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        yield return null; yield return null; yield return null;
        FindObjectOfType<FactoryTutorial>().Close(); yield return null; yield return null;
        builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        assistant = builder.Assistant; assistant.Open(); assistant.QuestionInput.text = "帮我摆放单冲床双轨最优方案"; assistant.Calculate();
        Check(assistant.Answer?.best != null && assistant.Answer.quantity == sharedPunchBest.quantity && assistant.Answer.cost == sharedPunchBest.cost &&
            assistant.Answer.sharedPunchRequired && FactoryProduction.Instance.Stations.FindAll(s => s.Kind == 0).Count == 1 && Field<Coroutine>(assistant, "networkRoutine") == null,
            "single-punch dual-track placement command selects its constrained maximum-output plan without needing an API key");
        var sharedSource = FactoryProduction.Instance.Stations.Find(s => s.Kind == 0);
        var sourceArms = FactoryProduction.Instance.Arms.FindAll(a => a.CanReach(sharedSource.Ports.DockPosition, out _));
        Check(sourceArms.Count >= 2 && !FactoryProduction.Instance.Graph.Same(sourceArms[0].Rail, sourceArms[1].Rail) && sharedSource.Input.Count == 10,
            "one real loaded punch is connected to two independent arm networks in the automatically placed layout");
        Capture("auto-shared-punch", 1280, 720); CaptureLayout("shared-punch-layout");
        report.AppendLine("NOTE responses are simulated; no real API key was supplied and no paid completion was performed.");
        Finish(true);
    }
    [Serializable] sealed class Wrapper { public string value; }
    static string Envelope(string text, string finish = "stop") => "{\"choices\":[{\"finish_reason\":\"" + finish + "\",\"message\":{\"content\":" + Quote(text) + "}}]}";
    static IEnumerator Fixture(string body, string failure, Action<string, string> done) { yield return null; done(body, failure); }
    void Reply(FactoryLocalAssistant assistant, string question, FactoryDeepSeekClient.Intent intent)
    {
        assistant.GetType().GetField("pendingQuestion", Hidden).SetValue(assistant, question);
        Call(assistant, "AddChat", "玩家", question, false);
        Call(assistant, "HandleDeepSeekAnswer", intent, null);
    }
    static string Quote(string value) { string json = JsonUtility.ToJson(new Wrapper { value = value }); return json.Substring(9, json.Length - 10); }
    void Capture(string name, int width, int height)
    {
        var canvas = Field<Canvas>(FindObjectOfType<PlacementSystem>().Assistant, "canvas");
        // Render the complete game UI, including the active settings modal.
        var root = new GameObject("DeepSeek capture camera"); var camera = root.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var rt = new RenderTexture(width, height, 24); var mode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldPlane = canvas.planeDistance;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10; camera.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        var assistant = FindObjectOfType<PlacementSystem>().Assistant;
        var rect = Field<RectTransform>(assistant, "settingsPanel");
        if (rect.gameObject.activeSelf) FactoryGuideUI.Fit(rect, canvas, new Vector2(880, 650), 32);
        FactoryGuideUI.Fit(Field<RectTransform>(assistant, "card"), canvas, new Vector2(1080, 800), 32);
        Canvas.ForceUpdateCanvases();
        foreach (var label in Field<GameObject>(assistant, "overlay").GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            if (label != Field<TMP_Text>(assistant, "chatText"))
                Check(!label.isTextOverflowing && !label.isTextTruncated, "text fits: " + label.name + " at " + width + "x" + height);
        }
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        Directory.CreateDirectory("Logs/deepseek"); File.WriteAllBytes("Logs/deepseek/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
        Destroy(image); Destroy(rt); Destroy(root); Canvas.ForceUpdateCanvases();
    }
    void CaptureLayout(string name = "shared-layout")
    {
        var factory = FactoryProduction.Instance;
        Bounds bounds = FindObjectOfType<RawMaterialSupply>().StockBounds;
        bounds.Encapsulate(factory.FinishedArea.Footprint);
        foreach (var s in factory.Stations) bounds.Encapsulate(s.GetComponent<EquipmentGrid>().Footprint);
        foreach (var r in factory.Rails) bounds.Encapsulate(r.GetComponent<EquipmentGrid>().Footprint);
        var hidden = FindObjectsOfType<FactoryPortLabel>(); foreach (var p in hidden) p.gameObject.SetActive(false);
        var root = new GameObject("Verified layout overview"); var camera = root.AddComponent<Camera>();
        camera.transform.position = bounds.center + Vector3.up * 100; camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.size.z * .5f + 6, (bounds.size.x * .5f + 6) / 1.6f);
        camera.cullingMask = ~(1 << 5); camera.nearClipPlane = .1f; camera.farClipPlane = 200;
        var labels = new System.Collections.Generic.List<GameObject>();
        foreach (var module in factory.Statistics.Modules)
        {
            if (module.Root == null || module.Removed) continue;
            var label = new GameObject(module.Name + " overview label").AddComponent<TextMeshPro>(); labels.Add(label.gameObject);
            Bounds visual = EquipmentGrid.VisualBounds(module.Root);
            label.transform.position = new Vector3(visual.center.x, visual.max.y + 2, visual.center.z);
            label.transform.rotation = camera.transform.rotation; label.font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset);
            label.fontSize = 3; label.text = module.Name; label.alignment = TextAlignmentOptions.Center; label.color = module.Kind == 1 ? Color.yellow : Color.white;
            label.rectTransform.sizeDelta = new Vector2(12, 4);
        }
        var target = new RenderTexture(1600, 1000, 24); camera.targetTexture = target; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
        Directory.CreateDirectory("Logs/deepseek"); File.WriteAllBytes("Logs/deepseek/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        foreach (var label in labels) Destroy(label); foreach (var p in hidden) if (p != null) p.gameObject.SetActive(true);
        Destroy(image); Destroy(target); Destroy(root);
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-deepseek-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
