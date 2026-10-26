using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public static bool TutorialRequested { get; private set; }
    static int requestedLevel;
    public static int ConsumeLevelRequest() { int level = requestedLevel; requestedLevel = 0; return level; }
    public static void RequestFirstLevel(bool tutorial = false) { requestedLevel = 1; TutorialRequested = tutorial; }
    public GameObject LevelSelection { get; private set; }
    public Button FirstLevelButton { get; private set; }
    public Button SecondLevelButton { get; private set; }
    Canvas canvas;
    RectTransform selectionCard;
    RectTransform home;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRequest() { TutorialRequested = false; requestedLevel = 0; }
    public static bool ConsumeTutorialRequest()
    { bool requested = TutorialRequested; TutorialRequested = false; return requested; }

    void Start()
    {
        Time.timeScale = 1;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        canvas = FindObjectOfType<Canvas>();
        foreach (var label in canvas.GetComponentsInChildren<TMP_Text>(true)) label.font = FactoryFonts.Ensure(label.font);
        foreach (Transform child in canvas.transform) child.gameObject.SetActive(false);
        var background = FactoryGuideUI.Overlay(canvas.transform, "Home", FactoryGuideUI.Background);
        home = FactoryGuideUI.Box(background, "HomeContent", Vector2.zero, new Vector2(1840, 1000), FactoryGuideUI.Background);
        FactoryGuideUI.Box(home, "YellowBand", new Vector2(0, 140), new Vector2(1840, 196), FactoryGuideUI.Mint);
        FactoryGuideUI.Text(home, "Wordmark", "FACTORY", new Vector2(340, 46), new Vector2(1120, 260), 176, new Color(.7f, .72f, .66f));
        FactoryGuideUI.Text(home, "Title", "产线规划", new Vector2(-516, 150), new Vector2(660, 150), 84, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(home, "Goal", "产量优先 · 成本最优", new Vector2(-565, -92), new Vector2(560, 44), 26, FactoryGuideUI.Muted);
        FactoryGuideUI.Button(home, "StartButton", "开始规划", new Vector2(-620, -220), new Vector2(440, 72), FactoryGuideUI.Mint, StartGame, true);
        FactoryGuideUI.Button(home, "QuitButton", "退出", new Vector2(-220, -220), new Vector2(280, 72), FactoryGuideUI.CardColor, () => Application.Quit());
        FactoryGuideUI.Fit(home, canvas, new Vector2(1840, 1000), 32);
    }

    public void StartGame()
    {
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (LevelSelection == null) BuildSelection();
        LevelSelection.SetActive(true);
        FactoryGuideUI.Fit(selectionCard, canvas, new Vector2(1160, 740));
    }
    public void BackToMenu() { if (LevelSelection != null) LevelSelection.SetActive(false); }
    public void PlayFirstLevel()
    {
        RequestFirstLevel(true);
        FirstLevelButton.interactable = false;
        SceneManager.LoadScene("GameScene");
    }
    void BuildSelection()
    {
        LevelSelection = FactoryGuideUI.Overlay(canvas.transform, "LevelSelection", FactoryGuideUI.Background).gameObject;
        selectionCard = FactoryGuideUI.Box(LevelSelection.transform, "SelectionCard", Vector2.zero,
            new Vector2(1160, 740), FactoryGuideUI.PanelColor);
        FactoryGuideUI.Text(selectionCard, "Title", "选择关卡", new Vector2(-108, 250), new Vector2(840, 70), 42, FactoryGuideUI.Ink);
        var first = FactoryGuideUI.Box(selectionCard, "FirstLevelCard", new Vector2(-264, -20), new Vector2(504, 320), FactoryGuideUI.CardColor);
        FactoryGuideUI.Text(first, "Number", "01", new Vector2(-150, 91), new Vector2(130, 80), 64, FactoryGuideUI.Mint);
        FactoryGuideUI.Text(first, "Name", "第一关 · 初识产线", new Vector2(0, 12), new Vector2(432, 56), 30, FactoryGuideUI.Ink);
        FactoryGuideUI.Text(first, "Description", "10分钟 · 10块原料\n产量优先，同产量比净收益", new Vector2(0, -65), new Vector2(432, 86), 24, FactoryGuideUI.Muted);
        FirstLevelButton = FactoryGuideUI.Button(selectionCard, "EnterFirstLevel", "进入第一关  >", new Vector2(-264, -242),
            new Vector2(504, 66), FactoryGuideUI.Mint, PlayFirstLevel, true);
        var second = FactoryGuideUI.Box(selectionCard, "SecondLevelCard", new Vector2(264, -20), new Vector2(504, 320), FactoryGuideUI.CardColor);
        FactoryGuideUI.Text(second, "Number", "02", new Vector2(-150, 91), new Vector2(130, 80), 64, FactoryGuideUI.Muted);
        FactoryGuideUI.Text(second, "Name", "第二关", new Vector2(0, 12), new Vector2(432, 56), 30, FactoryGuideUI.Muted);
        SecondLevelButton = FactoryGuideUI.Button(selectionCard, "SecondLevelUnavailable", "即将开放", new Vector2(264, -242),
            new Vector2(504, 66), FactoryGuideUI.CardColor, null);
        SecondLevelButton.interactable = false;
        second.gameObject.SetActive(false); SecondLevelButton.gameObject.SetActive(false);
        first.anchoredPosition = new Vector2(0, -20); first.sizeDelta = new Vector2(840, 320);
        first.Find("HeaderRule").GetComponent<RectTransform>().sizeDelta = new Vector2(840, 4);
        first.Find("Number").GetComponent<RectTransform>().anchoredPosition = new Vector2(-295, 91);
        first.Find("Name").GetComponent<RectTransform>().sizeDelta = new Vector2(720, 56);
        first.Find("Description").GetComponent<RectTransform>().sizeDelta = new Vector2(720, 86);
        FirstLevelButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -242);
        FirstLevelButton.GetComponent<RectTransform>().sizeDelta = new Vector2(840, 66);
        FactoryGuideUI.Button(selectionCard, "BackToMainMenu", "<  返回主菜单", new Vector2(-396, -326),
            new Vector2(240, 46), FactoryGuideUI.PanelColor, BackToMenu);
        selectionCard.Find("Title").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 250);
        selectionCard.Find("BackToMainMenu").GetComponent<RectTransform>().anchoredPosition = new Vector2(-300, -326);
    }
    void Update()
    {
        if (home != null) FactoryGuideUI.Fit(home, canvas, new Vector2(1840, 1000), 32);
        if (LevelSelection == null || !LevelSelection.activeSelf) return;
        FactoryGuideUI.Fit(selectionCard, canvas, new Vector2(1160, 740));
        if (Input.GetKeyDown(KeyCode.Escape)) BackToMenu();
    }
}
