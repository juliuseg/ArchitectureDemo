using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScriptableObject), true)]
[CanEditMultipleObjects]
public class DirtyIndicatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        bool isDirty = EditorUtility.IsDirty(target);

        if (isDirty)
        {
            EditorGUILayout.HelpBox("Unsaved changes — not yet written to disk.", MessageType.Warning);
        }

        DrawDefaultInspector();
    }
}