# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A small Unity project used to prototype reusable architecture patterns (service locator, event bus, prefab tooling). Not a shipping game — expect single-scene, work-in-progress content and `WIP` commits.

## Setup & tooling

- **Unity 6000.3.22f1**, Universal Render Pipeline, new Input System. Open with `unity open .` or via Unity Hub.
- No build scripts, no linter, no CI. No automated tests exist yet (`com.unity.test-framework` is installed); EditMode/PlayMode tests would run from the Test Runner window or `unity test . --mode EditMode`.
- `com.unity.pipeline` is installed, so the `unity` CLI can drive a running Editor (recompile, run C#, inspect the scene, run tests). See the `unity-cli` skill.
- **Project rule for the `unity` CLI:** finish all code changes first, then **stop, end the turn, and ask** before running any `unity` command — a per-session yes from the user is required even under auto-accept. It is slow (one socket round-trip per call); when approved, batch everything into a single `eval_file` and keep the whole pass to a few calls. Full rule at the top of `~/.claude/skills/unity-cli/SKILL.md` and in project memory.

## Code layout & conventions

- **No assembly definitions.** All code is in the **global namespace** and compiles into Unity's default `Assembly-CSharp` / `Assembly-CSharp-Editor`.
- Editor tooling in `Assets/Scripts/Tooling/` (everything except the `AutoApplyPrefab` marker) uses `UnityEditor` **without** an `Editor/` folder or `#if UNITY_EDITOR` guard. Fine in the Editor; this would break a player build until that code is isolated.
- `Assets/Favourites/` is a vendored third-party Editor extension (Window > Favourites) — unrelated to project code.

## Architecture

### Global systems (`Assets/Scripts/`) — each pairs a static class with a play-mode reset hook

- **`ServiceLocator`** — static `Dictionary<Type, object>` with `Register/Get/TryGet/Unregister/Clear`. `ServiceUser<T>` is a base MonoBehaviour that auto-`Unregister`s itself in `OnDestroy`; concrete services register in `Awake` (currently only `PlayerManager : ServiceUser<PlayerManager>`).
- **`EventBus<T> where T : IEvent`** — one static `event Action<T>` per event struct. Event types live in `EventSystem/Events.cs` (`PlayerHealthChanged`, `PlayerDied`, `PointsChanged` — defined but not yet raised or consumed).
- **Reset pattern:** `ServiceLocator.RegisterPlayModeCleanup` and `EventBusCleanup` are `[InitializeOnLoad]` classes that clear their system's static state on `PlayModeStateChange.ExitingPlayMode` (`EventBusCleanup` reflects over every `IEvent` type to clear all buses). **Any new static/global system should add an equivalent `ExitingPlayMode` cleanup.**

### Prefab auto-apply tooling (`Assets/Scripts/Tooling/`)

Lets you edit environment prefab instances directly in the `_Main` scene and push changes back to the prefab asset on save.

- **`AutoApplyPrefab`** — empty marker MonoBehaviour, placed on prefab instances that should get this behavior.
- **`AutoApplyPrefabOverrides`** — `[InitializeOnLoad]`; on `sceneSaved` for a scene named in `TargetSceneNames`, finds marked instances, `ApplyPrefabInstance`s their overrides, then triggers a follow-up re-save.
- **`HierarchyDirtyIndicator`** — draws a dot in the Hierarchy: **blue** = a prefab override that will be silently auto-applied on save, **orange** = a change that won't be (plain object edit, or override on a non-marked instance). It detects "differs from last save" by diffing each GameObject against its own serialized-state snapshot (persisted across domain reloads via `SessionState`), because Unity has no such API — `EditorUtility.IsDirty` only means "modified since load" and never resets, and `HasPrefabInstanceAnyOverrides(_, false)` ignores root-transform moves.
- **`AutoApplyPrefabOverrides.TargetSceneNames`** (currently `{"_Main"}`) is the single source of truth for which scenes both tools act on.

### Other

- **`AutoSaveScriptableObjectEditor`** — `[CustomEditor(typeof(ScriptableObject), true)]` that writes any ScriptableObject inspector edit straight to disk. Applies to every SO project-wide (e.g. `Assets/Data/PlayerStats.asset`, a `PlayerStats` instance).
- **Gameplay** (single scene `Assets/Scenes/_Main.unity`): `PlayerManager` reads a `PlayerStats` SO and composes plain-C# `PlayerMovement` / `PlayerAttack`; input arrives via `PlayerInput` "Send Messages" (`OnMove`, `OnAttack`). `DoorSystem` + `TriggerZone` (UnityEvents fired on player enter/exit) is the door demo. Prefabs: `Player`, `Enviorment_1`, `DoorTrigger`, `Pillar`, `RandomThing`.
