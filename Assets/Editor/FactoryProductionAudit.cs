using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryProductionAudit
{
    static FactoryProductionAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("FactoryBatchAll", false))
            {
                SessionState.SetBool("FactoryBatchAll", false);
                EditorApplication.delayCall += RunGameplay;
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FactoryBatchAudit", false))
            {
                SessionState.SetBool("FactoryBatchAudit", false);
                new GameObject("Batch and slot audit").AddComponent<FactoryBatchAuditRunner>();
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FactoryInteractionAudit", false))
            {
                SessionState.SetBool("FactoryInteractionAudit", false);
                new GameObject("Factory interaction audit").AddComponent<FactoryInteractionAuditRunner>();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Demo1AuditAll", false))
            {
                SessionState.SetBool("Demo1AuditAll", false);
                EditorApplication.delayCall += RunGameplay;
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FactoryAudit", false))
            {
                SessionState.SetBool("FactoryAudit", false);
                new GameObject("Factory audit").AddComponent<FactoryProductionAuditRunner>();
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FactoryGameplayAudit", false))
            {
                SessionState.SetBool("FactoryGameplayAudit", false);
                new GameObject("Factory gameplay audit").AddComponent<Demo1GameplayAuditRunner>();
            }
        };
    }
    public static void RunAll() { SessionState.SetBool("Demo1AuditAll", true); Run(); }
    [MenuItem("Tools/Factory/Run Rotated Rail Welding Audit")]
    public static void RunTurns()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        FactoryRailTurnAudit.AssertTopology();
        SessionState.SetBool("FactoryGameplayTurns", true); RunGameplay();
    }
    [MenuItem("Tools/Factory/Run Automatic Final Storage Audit")]
    public static void RunStorage()
    {
        SessionState.SetBool("FactoryGameplayStorage", true); RunGameplay();
    }
    public static void RunBatchAll() { SessionState.SetBool("FactoryBatchAll", true); RunBatch(); }
    [MenuItem("Tools/Factory/Run Buffered Output Transport Audit")]
    public static void RunBufferedGameplay()
    {
        SessionState.SetBool("FactoryGameplayBuffered", true); RunGameplay();
    }
    [MenuItem("Tools/Factory/Run Slot And Punch Batch Audit")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool("FactoryBatchAudit", true); EditorApplication.isPlaying = true;
    }
    [MenuItem("Tools/Factory/Run Collision And Material Audit")]
    public static void RunInteraction()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("FactoryInteractionAudit", true); EditorApplication.isPlaying = true;
    }
    [MenuItem("Tools/Factory/Run Production Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("FactoryAudit", true);
        EditorApplication.isPlaying = true;
    }
    [MenuItem("Tools/Factory/Run Demo1 Gameplay Audit")]
    public static void RunGameplay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("FactoryGameplayAudit", true); EditorApplication.isPlaying = true;
    }
}
