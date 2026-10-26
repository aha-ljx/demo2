using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class FactoryBufferAudit
{
    static FactoryBufferAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("FactoryBufferAudit.Pending", false)) return;
            SessionState.SetBool("FactoryBufferAudit.Pending", false);
            new GameObject("Buffer capacity audit").AddComponent<FactoryBufferCapacityAuditRunner>();
        };
    }
    public static void RunConveyor()
    {
        SessionState.SetBool("FactoryGameplayBuffers", true); FactoryProductionAudit.RunGameplay();
    }
    [MenuItem("Tools/Factory/Run Buffer Capacity And Backpressure Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("FactoryBufferAudit.Pending", true); EditorApplication.isPlaying = true;
    }
}
