# SDx Door Editor GUI fix 1.0.1

## Applies to

Existing SDx Door Interaction v1.0.0 in Unity 2022.3.
This is a PATCH, not a stand-alone package. Only two Editor source files are replaced.
No Runtime component, MJCF, mesh, texture, scene, generated asset or .meta file is included.

## Symptom and scope

The screenshot shows `Upgrade complete (1.0.0)` followed by:
`EndLayoutGroup: BeginLayoutGroup must be called first.`

The original EditorWindow invoked a scene backup, AssetDatabase refresh, MuJoCo
import, selection changes and dialogs directly within OnGUI while a scroll layout
was still active. That is consistent with an IMGUI layout/re-entry error after
conversion. The exact Unity call stack was not reproduced in this environment.
The completed log does NOT establish native stability or successful robot grasping.

## Changes

- Defer the upgrade with EditorApplication.delayCall until the GUI pass has ended.
- Snapshot the selected original and parameters at click time; reject a second queued click.
- Use a scoped scroll view and horizontal layout instead of manual Begin/End pairs.
- Defer Inspector selection, validation dialogs, initial-pose actions, direction changes
  and restoration. Do not destroy the inspected target while drawing its Inspector.
- Recheck object/scene validity and Play/compilation state on dispatch.
- Do not swallow Unity ExitGUIException as an ordinary model failure.
- Run successful post-upgrade selection and feedback outside the rollback block.
- Show `Editor GUI fix 1.0.1` in the tool and Inspector.

Existing runtime metadata can still say `Passive Sliding Door 1.0.0` because the
model/runtime are intentionally unchanged. The second line identifies the Editor fix.

## Install without rebuilding your door

1. Stop Play. Close the Sliding Door Upgrade window. Save the CURRENT scene with
   Ctrl+S (or Save As to a new scene). Close Unity after saving.
2. Back up `Assets/SDxDoorInteraction/Editor/` OUTSIDE Assets.
3. Extract this ZIP under the project's `Assets/`, allowing overwrite of the two
   existing .cs files. Keep their existing .meta files; do not delete the package.
4. Reopen Unity and let scripts compile. Confirm `Editor GUI fix 1.0.1` is visible.
5. Select the ALREADY-CREATED `_Sliding` root with SDxPassiveSlidingDoor.
6. Clear old Console entries and click `Validate This Upgrade`.
7. If `STRUCTURE CHECK PASSED` appears, save and continue with travel-space checks.

Do not click Upgrade again on the old inactive original just to test this patch.
Do not reimport the whole house, reactivate the old door, remove Generated/ or
Assets/Local/MjImports/, or change physics settings to fix this GUI error.
Only another, not-yet-upgraded original should use the upgrade menu.

## What to check next

- The original remains inactive.
- Fixed Frame, Moving Leaf and Slide Joint references are assigned.
- Structural validation reports no problems.
- No new layout error is emitted on selection/validation.
- Start Closed/Half Open/Open still sets an EDIT-MODE initial state; Restore still
  reactivates the original only after confirmation.

If an error remains, stop before rebuilding doors. Select the first new red Console
entry and copy its complete message and call stack, not merely the bottom status line.

## Verification performed here

- Static source/delimiter/deferral checks: see GUI_FIX_1_0_1_CHECKS.json.
- Original 12 Python static geometry tests pass. Geometry is not changed by this patch.
- ZIP integrity and a simulated overlay on the original v1.0.0 package are checked.
- NO Unity Editor, Unity assembly compilation or native physics run was performed.
  A C# compiler was not available; static checks do not certify compilation or GUI behavior.

## Primary API references

- https://docs.unity.cn/Documentation/ScriptReference/EditorApplication-delayCall.html
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/GUIUtility.ExitGUI.html
