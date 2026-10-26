using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryOnboardingAudit
{
    const string Pending = "FactoryOnboardingAudit.Pending";
    static FactoryOnboardingAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var runner = new GameObject("Level selection and tutorial audit");
            Object.DontDestroyOnLoad(runner);
            runner.AddComponent<FactoryOnboardingAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run Level Selection And Tutorial Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
}
