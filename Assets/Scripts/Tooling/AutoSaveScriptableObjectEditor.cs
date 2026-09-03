using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScriptableObject), true)]
[CanEditMultipleObjects]
public class AutoSaveScriptableObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}