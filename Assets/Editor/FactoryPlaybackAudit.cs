using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryPlaybackAudit
{
    const string Pending = "FactoryPlaybackAudit.Pending";
    static FactoryPlaybackAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var runner = new GameObject("Punch dwell and playback audit").AddComponent<FactoryPlaybackAuditRunner>();
            runner.SnapshotOnly = SessionState.GetBool("FactoryPlaybackAudit.Snapshot", false);
            SessionState.SetBool("FactoryPlaybackAudit.Snapshot", false);
        };
    }
    public static void Snapshot()
    {
        SessionState.SetBool("FactoryPlaybackAudit.Snapshot", true); Run();
    }
    [MenuItem("Tools/Factory/Run Punch Dwell And Playback Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
