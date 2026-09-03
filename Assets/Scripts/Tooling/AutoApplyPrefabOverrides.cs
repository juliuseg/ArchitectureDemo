using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

[InitializeOnLoad]
public static class AutoApplyPrefabOverrides
{
    private static readonly string[] TargetSceneNames =
    {
        "_Main"
    };

    private static bool isReapplying = false;

    static AutoApplyPrefabOverrides()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (isReapplying)
            return;

        if (!TargetSceneNames.Contains(scene.name))
            return;

        bool appliedAnything = ApplyEnvironmentOverrides(scene);

        if (appliedAnything)
        {
            isReapplying = true;
            EditorApplication.delayCall += () =>
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                isReapplying = false;
            };
        }
    }

    private static bool ApplyEnvironmentOverrides(Scene scene)
    {
        bool applied = false;
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

                var mods = PrefabUtility.GetPropertyModifications(instanceRoot);
                bool hasAddedObjects = PrefabUtility.GetAddedGameObjects(instanceRoot).Count > 0;
                bool hasAddedComponents = PrefabUtility.GetAddedComponents(instanceRoot).Count > 0;

                if ((mods != null && mods.Length > 0) || hasAddedObjects || hasAddedComponents)
                {
                    PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);
                    applied = true;
                }
            }
        }

        return applied;
    }
}