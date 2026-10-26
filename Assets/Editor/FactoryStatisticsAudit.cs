using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryStatisticsAudit
{
    const string Pending = "FactoryStatisticsAudit.Pending";
    static FactoryStatisticsAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var root = new GameObject("Production statistics audit"); Object.DontDestroyOnLoad(root);
            root.AddComponent<FactoryStatisticsAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Production Statistics Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
