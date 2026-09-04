using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Draws a coloured dot next to GameObjects in the Hierarchy window to show
/// unsaved / overridden state:
///
///   Orange = a change that will NOT be silently auto-applied on save
///            (a plain scene-object edit, or an override on a prefab instance
///             that is not governed by <see cref="AutoApplyPrefab"/>).
///   Blue   = a prefab override that IS governed by <see cref="AutoApplyPrefab"/>
///            and will be applied to the prefab asset on save by
///            <see cref="AutoApplyPrefabOverrides"/>.
///
/// Change detection is done by diffing each GameObject against a snapshot of
/// its last-saved serialized state, because Unity exposes no API for "does this
/// object currently differ from what is saved on disk":
///   * EditorUtility.IsDirty only tracks "modified since last save". It is set
///     on any edit and is cleared only by a save - never by undo (undo itself
///     re-dirties) and never by manually restoring the original values. That is
///     why the previous implementation's orange dots got stuck.
///   * PrefabUtility.HasPrefabInstanceAnyOverrides(_, includeDefaultOverrides:false)
///     deliberately ignores the root Transform's position/rotation because they
///     are "default overrides". That is why moving a non-governed prefab
///     instance root never turned it orange. Passing true instead is not an
///     option - it reports true for essentially every prefab instance.
///
/// The snapshot diff sidesteps both problems: an object is marked iff its
/// current serialized state differs from the last save, so undo / revert clears
/// the mark and a root move is detected like any other change.
/// </summary>
[InitializeOnLoad]
public static class HierarchyDirtyIndicator
{
    private enum MarkType
    {
        None,
        Orange,
        Blue
    }

    private static readonly Color RegularOverrideColor = new Color(1f, 0.6f, 0f);
    private static readonly Color AutoApplyOverrideColor = new Color(0.3f, 0.7f, 1f);

    // What the scene YAML holds for one GameObject as of the last save. For a
    // plain object that is its full serialized state; for a prefab-instance
    // object it is the set of overrides that target it (effective values are the
    // prefab's job, not the scene's). Both are captured always so the plain <->
    // instance transition has something to compare against.
    private sealed class Snapshot
    {
        public ulong PlainState;       // transform + name/active/layer/tag/flags + component values
        public ulong DefaultOverrides; // root pos/rot/name/order overrides - stay in the scene
        public ulong RealOverrides;    // every other override - AutoApply pushes these to the prefab
        public ulong Children;         // set of direct child instanceIDs (add / remove, not order)
        public ulong Identity;         // which prefab this instances, and root-vs-part
        public bool WasPrefabRoot;     // was this the prefab instance root at snapshot time
    }

    // instanceID -> serialized state as of the last save / scene open.
    // A GameObject with no entry here did not exist at the last save.
    private static readonly Dictionary<int, Snapshot> Saved = new Dictionary<int, Snapshot>();

    // Scene keys we have already captured a baseline for.
    private static readonly HashSet<string> BaselinedScenes = new HashSet<string>();

    // Per-recompute memoisation, rebuilt (throttled) when StateVersion moves on.
    private static readonly Dictionary<int, MarkType> MarkMemo = new Dictionary<int, MarkType>();
    private static readonly Dictionary<int, bool> TagMemo = new Dictionary<int, bool>();

    private static int stateVersion = 1;
    private static int memoVersion;
    private static double lastComputeTime;

    private const double RecomputeThrottleSeconds = 0.1;
    private const string SnapshotSessionKey = "HierarchyDirtyIndicator.Snapshot.v5";
    private const string ScenesSessionKey = "HierarchyDirtyIndicator.Scenes.v1";

    static HierarchyDirtyIndicator()
    {
        Restore();

        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        EditorApplication.update += OnUpdate;
        EditorApplication.hierarchyChanged += Bump;
        Undo.undoRedoPerformed += Bump;
        ObjectChangeEvents.changesPublished += OnObjectChanges;

        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorSceneManager.sceneOpened += (scene, mode) => SnapshotScene(scene);
        EditorSceneManager.sceneClosed += OnSceneClosed;

        PrefabStage.prefabStageOpened += stage => SnapshotScene(stage.scene);
        PrefabStage.prefabStageClosing += stage => BaselinedScenes.Remove(SceneKey(stage.scene));
        PrefabStage.prefabSaved += root => SnapshotScene(root.scene);

        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Leaving play mode reloads the scene from its pre-play state,
                // so every cached instanceID / baseline is now meaningless.
                Saved.Clear();
                BaselinedScenes.Clear();
                Bump();
            }
        };

        AssemblyReloadEvents.beforeAssemblyReload += Persist;
        EditorApplication.quitting += () =>
        {
            SessionState.EraseString(SnapshotSessionKey);
            SessionState.EraseString(ScenesSessionKey);
        };
    }

    // ---- change signalling ------------------------------------------------

    private static void Bump()
    {
        stateVersion++;
    }

    private static void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        if (stream.length > 0)
            Bump();
    }

    private static void OnUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && !BaselinedScenes.Contains(SceneKey(scene)))
                SnapshotScene(scene);
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && !BaselinedScenes.Contains(SceneKey(prefabStage.scene)))
            SnapshotScene(prefabStage.scene);

        if (memoVersion != stateVersion)
            EditorApplication.RepaintHierarchyWindow();
    }

    // ---- snapshotting ---------------------------------------------------

    private static void SnapshotScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
            SnapshotRecursive(root);

        BaselinedScenes.Add(SceneKey(scene));
        PruneDeadEntries();
        Bump();
    }

    private static void SnapshotRecursive(GameObject go)
    {
        Saved[go.GetInstanceID()] = Capture(go);

        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SnapshotRecursive(t.GetChild(i).gameObject);
    }

    private static Snapshot Capture(GameObject go)
    {
        OverrideSignatures(go, out string defaultOverrides, out string realOverrides);
        return new Snapshot
        {
            PlainState = Fnv1a(PlainStateSignature(go)),
            DefaultOverrides = Fnv1a(defaultOverrides),
            RealOverrides = Fnv1a(realOverrides),
            Children = Fnv1a(ChildSetSignature(go)),
            Identity = Fnv1a(PrefabIdentity(go)),
            WasPrefabRoot = PrefabUtility.IsAnyPrefabInstanceRoot(go),
        };
    }

    // Full serialized state of the object - the scene YAML for a plain (non
    // prefab-instance) object. For a prefab-instance object this is not the scene
    // YAML (the prefab supplies the values), but it is still captured so the
    // plain <-> instance transition has a baseline.
    private static string PlainStateSignature(GameObject go)
    {
        return TransformSignature(go.transform) + "\n" + RestSignature(go);
    }

    // Local position / rotation / scale (+ RectTransform layout).
    private static string TransformSignature(Transform t)
    {
        var sb = new StringBuilder(160);
        sb.Append(t.localPosition.ToString("F5")).Append('|')
          .Append(t.localRotation.ToString("F6")).Append('|')
          .Append(t.localScale.ToString("F5"));

        if (t is RectTransform rt)
        {
            sb.Append('|').Append(rt.anchorMin.ToString("F5"))
              .Append(rt.anchorMax.ToString("F5"))
              .Append(rt.pivot.ToString("F5"))
              .Append(rt.sizeDelta.ToString("F5"))
              .Append(rt.anchoredPosition3D.ToString("F5"));
        }

        return sb.ToString();
    }

    // Sorted set of direct child instanceIDs - detects a child added or removed
    // (including a child deleted), not a reorder.
    private static string ChildSetSignature(GameObject go)
    {
        Transform t = go.transform;
        if (t.childCount == 0)
            return "";

        var ids = new int[t.childCount];
        for (int i = 0; i < t.childCount; i++)
            ids[i] = t.GetChild(i).gameObject.GetInstanceID();
        System.Array.Sort(ids);

        var sb = new StringBuilder(t.childCount * 8);
        foreach (int id in ids)
            sb.Append(id).Append(',');
        return sb.ToString();
    }

    // The object's own values: name / active / layer / tag / static-flags, plus
    // every non-Transform component. Deliberately NOT EditorJsonUtility.ToJson on
    // the GameObject itself - that could fold in prefab-linkage fields, which
    // would make every child of a freshly-created prefab look "changed".
    private static string RestSignature(GameObject go)
    {
        var sb = new StringBuilder(512);
        sb.Append("go:").Append(go.name)
          .Append('|').Append(go.activeSelf ? 1 : 0)
          .Append('|').Append(go.layer)
          .Append('|').Append(go.tag)
          .Append('|').Append((int)GameObjectUtility.GetStaticEditorFlags(go));

        foreach (Component component in go.GetComponents<Component>())
        {
            sb.Append('\n');

            if (component == null)
            {
                sb.Append("<missing-script>");
                continue;
            }

            if (component is Transform)
                continue;

            sb.Append(component.GetType().FullName).Append('=').Append(SafeJson(component));
        }

        return sb.ToString();
    }

    // Which prefab this object instances, and whether it is the instance root
    // here. Flips when the object is made into / unpacked from / reconnected to a
    // prefab. A flip is only meaningful at the instance root - a "part:" child is
    // just carried along - so it is filtered in PrefabBoundaryChanged.
    private static string PrefabIdentity(GameObject go)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(go))
            return "none";

        Object source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        string path = source != null ? AssetDatabase.GetAssetPath(source) : "?";
        return (PrefabUtility.IsAnyPrefabInstanceRoot(go) ? "root:" : "part:") + path;
    }

    // The overrides in the scene YAML that target this object, split by whether
    // AutoApplyPrefabOverrides (ApplyPrefabInstance) would push them into the
    // prefab asset:
    //   defaultSig - the root's position/rotation/name/sibling-order. Unity keeps
    //                these as "default overrides" that stay in the scene.
    //   realSig    - every other property override, plus added/removed components
    //                and added GameObjects. These get applied to the prefab.
    private static void OverrideSignatures(GameObject go, out string defaultSig, out string realSig)
    {
        defaultSig = string.Empty;
        realSig = string.Empty;

        if (!PrefabUtility.IsPartOfPrefabInstance(go))
            return;

        GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
        if (root == null)
            return;

        GameObject goSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
        GameObject rootSource = PrefabUtility.GetCorrespondingObjectFromSource(root);

        var defaults = new List<string>();
        var reals = new List<string>();

        PropertyModification[] mods = PrefabUtility.GetPropertyModifications(root);
        if (mods != null && goSource != null)
        {
            foreach (PropertyModification mod in mods)
            {
                if (mod == null)
                    continue;

                GameObject target = TargetGameObject(mod.target);
                if (target == null || target != goSource)
                    continue;

                string reference = "0";
                if (mod.objectReference != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(mod.objectReference);
                    reference = assetPath.Length > 0 ? assetPath : "iid:" + mod.objectReference.GetInstanceID();
                }

                string entry = mod.propertyPath + "=" + mod.value + "@" + reference;

                if (IsDefaultOverride(mod, target == rootSource))
                    defaults.Add(entry);
                else
                    reals.Add(entry);
            }
        }

        foreach (var added in PrefabUtility.GetAddedComponents(root))
            if (added.instanceComponent != null && added.instanceComponent.gameObject == go)
                reals.Add("+comp:" + added.instanceComponent.GetType().FullName);

        foreach (var removed in PrefabUtility.GetRemovedComponents(root))
            if (removed.containingInstanceGameObject == go)
                reals.Add("-comp:" + (removed.assetComponent != null ? removed.assetComponent.GetType().FullName : "?"));

        foreach (var added in PrefabUtility.GetAddedGameObjects(root))
            if (added.instanceGameObject == go)
                reals.Add("+go");

        defaults.Sort();
        reals.Sort();
        defaultSig = string.Join(";", defaults);
        realSig = string.Join(";", reals);
    }

    private static GameObject TargetGameObject(Object target)
    {
        if (target is Component c) return c.gameObject;
        if (target is GameObject g) return g;
        return null;
    }

    // Unity's "default overrides": the prefab instance root's transform
    // position/rotation, its name, and its sibling order. They live in the scene
    // and are not pushed to the prefab by an apply.
    private static bool IsDefaultOverride(PropertyModification mod, bool targetsRoot)
    {
        if (!targetsRoot)
            return false;

        string p = mod.propertyPath ?? string.Empty;
        if (mod.target is Transform)
        {
            return p.StartsWith("m_LocalPosition")
                || p.StartsWith("m_LocalRotation")
                || p.StartsWith("m_LocalEulerAnglesHint");
        }

        return p == "m_Name" || p.StartsWith("m_RootOrder");
    }

    // EditorJsonUtility.ToJson emits every Object's prefab-linkage fields
    // (m_CorrespondingSourceObject / m_PrefabInstance / m_PrefabAsset) and hide
    // flags. Those all flip when a plain object is turned into a prefab, which
    // would make every component of every child look "changed". Strip them so the
    // signature reflects only the component's own values.
    private static readonly System.Text.RegularExpressions.Regex VolatileJsonFields =
        new System.Text.RegularExpressions.Regex(
            "\"m_(CorrespondingSourceObject|PrefabInstance|PrefabAsset|PrefabInternal|ObjectHideFlags)\"\\s*:\\s*(\\{[^{}]*\\}|-?\\d+),?",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string SafeJson(Object obj)
    {
        try
        {
            return VolatileJsonFields.Replace(EditorJsonUtility.ToJson(obj), string.Empty);
        }
        catch
        {
            return obj.GetType().FullName + ":<unserializable>";
        }
    }

    private static ulong Fnv1a(string s)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static void PruneDeadEntries()
    {
        List<int> dead = null;
        foreach (int id in Saved.Keys)
        {
            if (EditorUtility.InstanceIDToObject(id) == null)
                (dead ?? (dead = new List<int>())).Add(id);
        }

        if (dead == null)
            return;

        foreach (int id in dead)
            Saved.Remove(id);
    }

    // Defer the post-save baseline by one tick: AutoApplyPrefabOverrides handles
    // the same sceneSaved event by applying overrides to prefabs and re-saving.
    // Snapshotting after that settles keeps those applied overrides out of the
    // "something changed since save" diff (they would otherwise flash a mark
    // until the re-save).
    private static void OnSceneSaved(Scene scene)
    {
        EditorApplication.delayCall += () => SnapshotScene(scene);
    }

    private static void OnSceneClosed(Scene scene)
    {
        BaselinedScenes.Remove(SceneKey(scene));
        PruneDeadEntries();
        Bump();
    }

    private static string SceneKey(Scene scene)
    {
        return string.IsNullOrEmpty(scene.path)
            ? "::" + scene.name + "::" + scene.handle
            : scene.path;
    }

    // ---- persistence across domain reload ------------------------------

    // The snapshot represents "last saved state"; Unity keeps unsaved scene
    // edits across a script recompile, so the baseline has to survive it too or
    // every mark would vanish on every recompile. SessionState is cleared on
    // editor restart, which is correct: a cold start reloads scenes from disk.
    private static void Persist()
    {
        var sb = new StringBuilder(4096);
        foreach (KeyValuePair<int, Snapshot> kv in Saved)
        {
            if (EditorUtility.InstanceIDToObject(kv.Key) == null)
                continue;

            sb.Append(kv.Key).Append(',')
              .Append(kv.Value.PlainState).Append(',')
              .Append(kv.Value.DefaultOverrides).Append(',')
              .Append(kv.Value.RealOverrides).Append(',')
              .Append(kv.Value.Children).Append(',')
              .Append(kv.Value.Identity).Append(',')
              .Append(kv.Value.WasPrefabRoot ? '1' : '0').Append(';');
        }

        SessionState.SetString(SnapshotSessionKey, sb.ToString());
        SessionState.SetString(ScenesSessionKey, string.Join("\n", BaselinedScenes));
    }

    private static void Restore()
    {
        string scenes = SessionState.GetString(ScenesSessionKey, string.Empty);
        if (!string.IsNullOrEmpty(scenes))
        {
            foreach (string key in scenes.Split('\n'))
            {
                if (key.Length > 0)
                    BaselinedScenes.Add(key);
            }
        }

        string data = SessionState.GetString(SnapshotSessionKey, string.Empty);
        if (string.IsNullOrEmpty(data))
            return;

        foreach (string part in data.Split(';'))
        {
            if (part.Length == 0)
                continue;

            string[] fields = part.Split(',');
            if (fields.Length != 7)
                continue;

            if (int.TryParse(fields[0], out int id)
                && ulong.TryParse(fields[1], out ulong plainState)
                && ulong.TryParse(fields[2], out ulong defaultOverrides)
                && ulong.TryParse(fields[3], out ulong realOverrides)
                && ulong.TryParse(fields[4], out ulong children)
                && ulong.TryParse(fields[5], out ulong identity))
            {
                Saved[id] = new Snapshot
                {
                    PlainState = plainState,
                    DefaultOverrides = defaultOverrides,
                    RealOverrides = realOverrides,
                    Children = children,
                    Identity = identity,
                    WasPrefabRoot = fields[6] == "1",
                };
            }
        }

        DropStaleBaselines();
    }

    // Scene-object instanceIDs survive a domain reload but not an editor restart
    // or a scene reload. If a "baselined" scene has almost no live objects that
    // match a restored entry, the ids are stale - drop the scene so OnUpdate
    // captures a fresh baseline instead of marking everything as changed.
    private static void DropStaleBaselines()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            string key = SceneKey(scene);
            if (!BaselinedScenes.Contains(key))
                continue;

            int total = 0;
            int matched = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                CountBaselineMatches(root, ref total, ref matched);

            if (total > 0 && matched * 2 < total)
                BaselinedScenes.Remove(key);
        }
    }

    private static void CountBaselineMatches(GameObject go, ref int total, ref int matched)
    {
        total++;
        if (Saved.ContainsKey(go.GetInstanceID()))
            matched++;

        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            CountBaselineMatches(t.GetChild(i).gameObject, ref total, ref matched);
    }

    // ---- hierarchy drawing --------------------------------------------

    private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        if (memoVersion != stateVersion
            && EditorApplication.timeSinceStartup - lastComputeTime > RecomputeThrottleSeconds)
        {
            MarkMemo.Clear();
            TagMemo.Clear();
            memoVersion = stateVersion;
            lastComputeTime = EditorApplication.timeSinceStartup;
        }

        GameObject go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (go == null)
            return;

        MarkType mark = GetSubtreeMark(go);
        if (mark == MarkType.None)
            return;

        Color color = mark == MarkType.Orange ? RegularOverrideColor : AutoApplyOverrideColor;
        var dotRect = new Rect(selectionRect.xMax - 14f, selectionRect.y + 2f, 10f, 10f);

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(dotRect, EditorGUIUtility.whiteTexture);
        GUI.color = previousColor;
    }

    private static MarkType GetSubtreeMark(GameObject go)
    {
        int id = go.GetInstanceID();
        if (MarkMemo.TryGetValue(id, out MarkType cached))
            return cached;

        MarkType result = GetOwnMark(go);

        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            result = CombineMarks(result, GetSubtreeMark(t.GetChild(i).gameObject));

        MarkMemo[id] = result;
        return result;
    }

    private static MarkType CombineMarks(MarkType a, MarkType b)
    {
        if (a == MarkType.Orange || b == MarkType.Orange)
            return MarkType.Orange;

        if (a == MarkType.Blue || b == MarkType.Blue)
            return MarkType.Blue;

        return MarkType.None;
    }

    // The dot for this one object (subtree aggregation is done by GetSubtreeMark).
    // Question: relative to the last save, and after the next save + AutoApply,
    // does the SCENE file differ (orange) or only the PREFAB asset (blue)?
    private static MarkType GetOwnMark(GameObject go)
    {
        // Made into / unpacked from / reconnected to a prefab: the scene file's
        // framing of this object changed. Owned by the instance root; the
        // carried-along body is not marked. Never auto-applied -> orange.
        if (PrefabBoundaryChanged(go))
            return MarkType.Orange;

        bool childSetChanged = ChildSetChanged(go);

        if (!PrefabUtility.IsPartOfPrefabInstance(go))
        {
            // Plain scene object: the scene YAML is its full serialized state.
            bool changed = childSetChanged
                || !Saved.TryGetValue(go.GetInstanceID(), out Snapshot snap)
                || Fnv1a(PlainStateSignature(go)) != snap.PlainState;
            return changed ? MarkType.Orange : MarkType.None;
        }

        // Prefab-instance object with no baseline: the instance is new to the
        // scene (dragged in), or a prefab operation reassigned instanceIDs.
        if (!Saved.ContainsKey(go.GetInstanceID()))
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
                return MarkType.Orange;                 // new instance root

            if (PrefabUtility.GetCorrespondingObjectFromSource(go) != null)
                return MarkType.None;                   // carried-along body, owned by the root

            return Governed(go) ? MarkType.Blue : MarkType.Orange;  // an added-GameObject override
        }

        // Prefab-instance object: the scene YAML is only the overrides targeting
        // it. Effective values can differ from the last save because the prefab
        // asset changed (e.g. you applied an override) - that is not a scene
        // change and must not be marked.
        DiffOverrides(go, out bool defaultOverrideChanged, out bool realOverrideChanged);
        realOverrideChanged |= childSetChanged;

        if (!defaultOverrideChanged && !realOverrideChanged)
            return MarkType.None;

        // Root position/rotation/name/order stay in the scene even for a governed
        // instance that gets auto-applied.
        if (defaultOverrideChanged)
            return MarkType.Orange;

        // A real override changed: it stays in the scene unless AutoApply pushes
        // it to the prefab on save.
        return Governed(go) ? MarkType.Blue : MarkType.Orange;
    }

    private static bool Governed(GameObject go)
    {
        if (!AutoApplyPrefabOverrides.TargetSceneNames.Contains(go.scene.name))
            return false;

        if (PrefabUtility.IsAnyPrefabInstanceRoot(go) && HasOwnAutoApplyTag(go))
            return true;

        return HasTaggedAncestor(go.transform);
    }

    // ---- snapshot diffing --------------------------------------------

    private static void DiffOverrides(GameObject go, out bool defaultChanged, out bool realChanged)
    {
        OverrideSignatures(go, out string defaultSig, out string realSig);

        if (!Saved.TryGetValue(go.GetInstanceID(), out Snapshot snap))
        {
            // No baseline (whole instance is new / freshly dragged in): treat any
            // override it carries as new.
            defaultChanged = defaultSig.Length > 0;
            realChanged = realSig.Length > 0;
            return;
        }

        defaultChanged = Fnv1a(defaultSig) != snap.DefaultOverrides;
        realChanged = Fnv1a(realSig) != snap.RealOverrides;
    }

    // A direct child was added or removed (a delete shows up here on the parent).
    // Only meaningful against a baseline; a brand-new object's children are
    // handled object-by-object.
    private static bool ChildSetChanged(GameObject go)
    {
        return Saved.TryGetValue(go.GetInstanceID(), out Snapshot snap)
            && Fnv1a(ChildSetSignature(go)) != snap.Children;
    }

    // A prefab-identity flip that actually matters: this object is, or was, the
    // instance root. A "part:" child whose identity flips is just carried along.
    private static bool PrefabBoundaryChanged(GameObject go)
    {
        if (!Saved.TryGetValue(go.GetInstanceID(), out Snapshot snap))
            return false;

        if (Fnv1a(PrefabIdentity(go)) == snap.Identity)
            return false;

        return PrefabUtility.IsAnyPrefabInstanceRoot(go) || snap.WasPrefabRoot;
    }

    // ---- auto-apply governance (structural, unchanged behaviour) -----

    private static bool HasOwnAutoApplyTag(GameObject prefabRoot)
    {
        int id = prefabRoot.GetInstanceID();
        if (TagMemo.TryGetValue(id, out bool cached))
            return cached;

        bool result = ComputeHasOwnAutoApplyTag(prefabRoot);
        TagMemo[id] = result;
        return result;
    }

    private static bool ComputeHasOwnAutoApplyTag(GameObject prefabRoot)
    {
        if (prefabRoot.GetComponent<AutoApplyPrefab>() != null)
            return true;

        Transform t = prefabRoot.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform child = t.GetChild(i);

            if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                continue;

            if (ComputeHasOwnAutoApplyTag(child.gameObject))
                return true;
        }

        return false;
    }

    private static bool HasTaggedAncestor(Transform t)
    {
        Transform current = t.parent;

        while (current != null)
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject)
                && HasOwnAutoApplyTag(current.gameObject))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }
}
