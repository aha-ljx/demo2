using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryLocalAssistantAudit
{
    const string Pending = "FactoryLocalAssistantAudit.Pending";
    static FactoryLocalAssistantAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var root = new GameObject("Local assistant UI audit"); Object.DontDestroyOnLoad(root);
            root.AddComponent<FactoryLocalAssistantAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Local Assistant Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
