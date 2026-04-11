# AGENTS.md

## Project Overview
- Engine: Unity 6000.0.61f1
- Language: C#
- Gameplay scripts location: `Assets/Scripts`
- Runtime/editor split:
  - Runtime code: `Assets/Scripts/Runtime`
  - Editor tools code: `Assets/Scripts/Editor`
- Design reference: `docs/game_design.md`
- Monster design reference: `docs/monster_design.md`

## Unity CLI Pre-check
Before executing any Unity-related command:
1. Run: `unity-cli status`
   - If the command is not found, use:
     `C:\Users\owner\AppData\Local\unity-cli\unity-cli.exe status`
   - If `unity-cli` is installed but the current terminal/IDE session does not see it yet, open a new terminal or restart the IDE and try again
2. Verify:
   - `ready` is present
   - Project path is valid
   - Unity Editor is connected
3. If not ready:
   - Stop execution
   - Ask the user to open Unity or fix the connection
4. Only proceed if Unity is connected

## Unity Compile Verification
After code changes to Unity scripts or assets that can affect compilation:
1. Run: `unity-cli status`
   - If the command is not found, use:
     `C:\Users\owner\AppData\Local\unity-cli\unity-cli.exe status`
2. Clear the Unity Console:
   - Primary:
     `unity-cli console --clear`
   - Fallback:
     `C:\Users\owner\AppData\Local\unity-cli\unity-cli.exe console --clear`
3. Trigger Unity-side recompilation:
   - Primary:
     `unity-cli editor refresh --compile`
   - Fallback:
     `C:\Users\owner\AppData\Local\unity-cli\unity-cli.exe editor refresh --compile`
4. Fetch the actual Unity compile result:
   - Primary:
     `unity-cli console --filter error,warning --lines 100 --stacktrace short`
   - Fallback:
     `C:\Users\owner\AppData\Local\unity-cli\unity-cli.exe console --filter error,warning --lines 100 --stacktrace short`
5. If any compile errors appear:
   - Fix them when possible in the current turn
   - Re-run the compile verification steps until the Unity Console is clean or the remaining blocker is clearly explained
6. Report the compile verification result in the response
7. Do not rely on `dotnet build` as the primary Unity compile check. Unity Console results are the source of truth.

## Workflow for New Features
When adding a new feature:
1. Review relevant existing scripts first.
2. Extend existing structure and avoid rewrites.
3. Modify the minimum number of files.
4. If structural changes are necessary, explain what changed and why.

## Monster Feature Workflow
When starting monster design or monster implementation:
1. Review `docs/monster_design.md` first.
2. Check whether the monster spec includes starting band, step rules, timing result rules, and post-event state.
3. If required monster information is missing, ask for the missing parts before committing to a movement or pattern structure.

## Do Not Modify (unless explicitly required)
- `Assets/Plugins/`
- `Packages/`
- `ProjectSettings/`
- Any tool-generated folders (for example: `Library/`, `Temp/`, `Logs/`)

## Coding Rules (Unity/C#)
- Prefer `[SerializeField] private` for Inspector exposure instead of public fields.
- Avoid heavy logic in `Update()`; prefer events, coroutines, or timers where appropriate.
- Do not spam `FindObjectOfType()` / `GameObject.Find()` and never call them in `Update()`.
- Cache references through serialized fields or initialization paths.
- In timing/judgement hot paths, avoid per-frame allocations (no LINQ chains like `Where().ToList()` in loops).
- Prefer index-based iteration and reusable buffers in runtime timing code.

## Rhythm Game Timing Rules
- Runtime sync source must be FMOD playback time (DSP-based timeline), not `Time.time`.
- Chart event timing canonical unit is beat.
- Required chart meta for runtime:
  - `bpm`
  - `offsetSec`
  - `fmodEventPath`

## Chart Validation Policy
- Validation runs before save.
- Save is blocked if any `Error` exists.
- Save is allowed with `Warning` only.
- Minimum `Error` conditions:
  - `BPM <= 0`
  - `songLengthSec <= 0`
  - invalid `gridDivision`
  - more than 4 events in the same time slot
  - missing required meta values

## Event System Rules
- Event types:
  - `AttackTiming`
  - `MonsterAction`
  - `CameraCue`
  - `CameraMoving`
- Up to 4 events are allowed in one time slot.
- Execution order for same-slot events:
  - `AttackTiming -> MonsterAction -> CameraMoving -> CameraCue`

## FMOD Workflow Rules
- Always follow: `Save (FMOD Studio) -> Build Banks -> Unity Refresh`.
- Do not treat editor preview audio as runtime authority.
- Runtime playback and sync must follow FMOD events/banks.

## Comments and Encoding
- Write source code comments in Korean.
- Save source files as UTF-8 to prevent Korean text corruption.

## After Code Changes (Required in response)
Always include:
1. Which scripts were added/modified and why.
2. Step-by-step Unity Editor instructions:
   - Which GameObject gets which script.
   - Which serialized fields to assign.
   - Required tags/layers/input actions/colliders/rigidbodies.
   - Required prefab creation/update steps.
3. Assumptions (scene name, existing objects, expected hierarchy).
4. Test procedure and result summary.
   - Include whether `unity-cli editor refresh --compile` was run
   - Include the Unity Console error/warning result
5. Any unverified items or tests not run.
