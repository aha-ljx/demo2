using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryAssistantAudit
{
    const string Pending = "FactoryAssistantAudit.Pending";
    static FactoryAssistantAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var root = new GameObject("Local assistant verification"); Object.DontDestroyOnLoad(root);
            var runner = root.AddComponent<FactoryAssistantAuditRunner>();
            runner.SharedPunchOnly = SessionState.GetBool("FactoryAssistantAudit.SharedPunchOnly", false);
            runner.RefreshMotionCatalog = SessionState.GetBool("FactoryAssistantAudit.RefreshMotionCatalog", false);
            SessionState.SetBool("FactoryAssistantAudit.RefreshMotionCatalog", false);
            runner.ReverifyBest = SessionState.GetBool("FactoryAssistantAudit.ReverifyBest", false) || runner.SharedPunchOnly;
            SessionState.SetBool("FactoryAssistantAudit.ReverifyBest", false);
            SessionState.SetBool("FactoryAssistantAudit.SharedPunchOnly", false);
        };
    }
    [MenuItem("Tools/Factory/Build Verified Assistant Plans")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    [MenuItem("Tools/Factory/Reverify Best Assistant Plan")]
    public static void ReverifyBest()
    {
        SessionState.SetBool("FactoryAssistantAudit.ReverifyBest", true); Run();
    }
    [MenuItem("Tools/Factory/Reverify Shared Punch Plan")]
    public static void ReverifySharedPunch()
    {
        SessionState.SetBool("FactoryAssistantAudit.SharedPunchOnly", true); Run();
    }
    [MenuItem("Tools/Factory/Reverify Recommended Plans After Motion Changes")]
    public static void RefreshMotionCatalog()
    {
        FactorySmoothnessAudit.CheckPaths();
        SessionState.SetBool("FactoryAssistantAudit.RefreshMotionCatalog", true); Run();
    }
}
