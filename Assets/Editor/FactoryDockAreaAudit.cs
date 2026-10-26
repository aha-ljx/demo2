using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryDockAreaAudit
{
    const string Pending = "FactoryDockAreaAudit.Pending";
    static FactoryDockAreaAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Dock area audit").AddComponent<FactoryDockAreaAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Dock Area Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        FactoryRailTurnAudit.AssertTopology();
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
