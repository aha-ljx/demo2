using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryWorkflowInputAudit
{
    const string Pending = "FactoryWorkflowInputAudit.Pending";
    static FactoryWorkflowInputAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Storage and bird input audit").AddComponent<FactoryWorkflowInputAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Storage Edges And Bird Input Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
