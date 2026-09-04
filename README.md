# Various Tools

Located under `Scripts/Tooling`.

## Auto-Saving Scriptable Objects

Handled by `AutoSaveScriptableObjectEditor` — just needs to exist in the project, no setup required.

## Prefab Auto-Saving System

**Purpose:** To work together on a scene, this tool tries to prevent writes to the scene YAML file by auto-applying prefabs with a certain tag, and shows editor UI to give you an idea of what objects will change the scene file.

- Mark a prefab for auto-saving by applying the **`AutoApplyPrefab`** tag (mono).
- **`AutoApplyPrefabOverrides`** manages the saving.
  - For nested prefabs: the topmost prefab with the tag is the one that actually has its overrides saved.
  - Auto-applying only works on certain scenes — add more under **`TargetSceneNames`** under **`AutoApplyPrefabOverrides`**.

### `HierarchyDirtyIndicator`

Gives editor feedback on what will change the scene file:

- 🟠 **Orange** — this object will modify the scene file. Applies to all objects and prefabs *without* the tag. Note: it also applies to tagged prefabs when you change their position/rotation, since that's a scene change, not a prefab change.
- 🔵 **Blue** — you changed a tagged prefab, and on save the system will auto-apply that change to the prefab first, so the scene itself stays unchanged.

