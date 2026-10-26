using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactorySharedBendingAudit
{
    const string Pending = "FactorySharedBendingAudit.Pending";
    static FactorySharedBendingAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Shared bending audit").AddComponent<FactorySharedBendingAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Shared Bending Dock Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }
}
