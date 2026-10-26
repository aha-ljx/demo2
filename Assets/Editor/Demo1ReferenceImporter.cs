using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Demo1ReferenceImporter
{
    const string Folder = "Assets/Resources/Demo1Reference";
    [MenuItem("Tools/Factory/Import Demo1 Reference")]
    public static void Import()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene("Assets/Demo1Reference/Scenes/SampleScene.unity");
        var controller = Object.FindObjectOfType<LoadingProcessController>();
        var root = new GameObject("Demo1AuthoritativeRig");
        foreach (var item in scene.GetRootGameObjects())
        {
            if (item == root) continue;
            if (item.GetComponentInChildren<Camera>() != null || item.GetComponentInChildren<Light>() != null) continue;
            item.transform.SetParent(root.transform, true);
        }
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != controller) Object.DestroyImmediate(behaviour);
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(node.gameObject);
        controller.ReplayMode = true; controller.ReplayScope = root.transform;
        Directory.CreateDirectory(Folder);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/ProductionRig.prefab");
        var definition = AssetDatabase.LoadAssetAtPath<Demo1RigDefinition>(Folder + "/Definition.asset");
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<Demo1RigDefinition>();
            AssetDatabase.CreateAsset(definition, Folder + "/Definition.asset");
        }
        definition.rigPrefab = prefab;
        int[] ids = { 1472, 268, 822 };
        for (int i = 0; i < 3; i++) definition.anchors[i] = FactoryMotion.Node(root.transform, ids[i]).localToWorldMatrix;
        definition.sourceHash = "2e49b01621b5be695955935242c47c8e3e8d36b1639dc07d34c6d921579e395a";
        EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("Demo1Calibrate", true);
        EditorApplication.isPlaying = true;
    }
}

[InitializeOnLoad]
public static class Demo1CalibrationStartup
{
    static Demo1CalibrationStartup()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Demo1Calibrate", false))
            {
                SessionState.SetBool("Demo1Calibrate", false);
                new GameObject("Demo1 Calibration").AddComponent<Demo1CalibrationRunner>();
            }
        };
    }
}
