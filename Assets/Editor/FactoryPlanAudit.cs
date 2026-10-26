using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryPlanAudit
{
    const string Pending = "FactoryPlanAudit.Pending";
    static FactoryPlanAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var root = new GameObject("Saved plan audit"); Object.DontDestroyOnLoad(root);
            root.AddComponent<FactoryPlanAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Saved Plan Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
