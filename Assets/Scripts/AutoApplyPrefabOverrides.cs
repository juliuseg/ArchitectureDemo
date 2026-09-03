using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AutoApplyPrefabOverrides
{
    static AutoApplyPrefabOverrides()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (GameObject root in roots)
        {
            AutoApplyEnvironment[] markers = root.GetComponentsInChildren<AutoApplyEnvironment>(true);

            foreach (AutoApplyEnvironment marker in markers)
            {
                GameObject go = marker.gameObject;

                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    continue;

                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (instanceRoot == null)
                    continue;

                PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);
            }
        }
    }
}