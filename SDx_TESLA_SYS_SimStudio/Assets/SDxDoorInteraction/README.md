# SDx Passive Sliding Door Upgrade v1.0.0

An additive Unity Editor tool for the **two AWS FoldingDoor_01 instances** in the
user's Small House scene. It does not replace/reimport the whole world and does
not change the MuJoCo package, native library, robot, furniture, UI or physics
project settings.

Target: Unity 2022.3, Built-in RP, the user's existing MuJoCo Unity plugin/native
library setup (reported as 3.16.0). No ROS, URDF, new native binary or extra Unity
package is installed. The exact installed Unity plugin build must be verified
on the user's machine; see Docs/TEST_STATUS.md.

## What this changes

- Splits the original mesh into a fixed outer frame and one rigid sliding leaf.
- Retains source UVs and all non-handle source triangles (not a newly drawn door).
- Replaces the source door-wide collision block with 3 frame boxes and 1 shallow
  leaf box. Glass remains solid in physics: window panes are NOT grasp openings.
- Removes four cramped decorative handle rods and their eight mounts. Creates
  one physical D-handle on each face (two total), with about 63 mm free depth
  between the grip bar and the leaf. Grip diameter is 24 mm.
- Adds one passive, limited slide joint per original door. No actuator, spring,
  freejoint, equality weld, runtime transform animation or automatic grasp.
- Moves the entire leaf to a parallel rail plane, default **160 mm** from the
  fixed frame, so the leaf AND both handles can pass the fixed jambs without
  disabling their collisions. This offset is deliberate and visible.
- Default travel 2.4 m, mass 18 kg, damping 25 N s/m, friction loss 3 N. These are
  simulation design assumptions, NOT measured real-door parameters.

This is NOT a folding-door mechanism. The six visible source panels now move as
one rigid panel. It needs substantial clear space beside the doorway. Both
nearby furniture and the rest of the current world must be checked separately.

## Install

1. Stop Play, save the scene, and close Unity before extracting the ZIP.
2. Extract `SDxDoorInteraction/` under the project's `Assets/`.
3. Optional but recommended native check with the SAME library Unity uses:

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
python3 "$UNITY_PROJECT/Assets/SDxDoorInteraction/Tools/verify_doors.py" \
  --library "$UNITY_PROJECT/Assets/Plugins/x86_64/libmujoco.so"
```

This Python tool uses only the standard library, does not require pip, and does
not modify your scene, sources or installed library. It tests an isolated door
in a separate process. Its contact probe is a small sphere, NOT an FR3 gripper.
A native-test failure must be reviewed before using the model.

## Apply to the current scene (one door at a time)

1. Open `MainScene`. Stay OUT of Play.
2. Select the original MjBody:
   `aws_robomaker_residential_FoldingDoor_01_FoldingDoor_01_001`.
   A child of that object may be selected; the tool searches upwards. Do NOT
   select the entire house root or FoldingDoor_02.
3. Open **Tools > SDx > Doors > Upgrade Selected Folding Door...**.
4. Keep defaults for the first trial. `Opening Direction` uses the door's LOCAL
   Unity Z axis (MJCF Y). The default for 001 is +Z, for 002 it is -Z; these are
   only starting choices, not a guarantee of safe world-space travel.
5. `Rail Side` chooses the door's local +X/-X face. Pick the side with clear space.
   The local X depth direction rotates with the current scene door Transform.
6. Click **Create Backup and Upgrade This Door**, then confirm.
7. Save the scene with Ctrl+S. Repeat for the `..._002` original.

The tool verifies the original name, approximate dimensions, unit scale,
upright orientation, lack of existing articulation or a MuJoCo ancestor, and required files. It
refuses unrelated/customized/scaled objects rather than silently replacing them.

### What happens during conversion

- A copy of the current scene is saved under `Assets/SDxDoorInteraction/Backups/`.
- The tool writes a uniquely named, relative-path MJCF under `Generated/<id>/`.
- The **official MjImporterWithAssets** imports ONLY that small door model.
  This invokes the user's native library to compile the door.
- A sibling root `..._Sliding` is placed at the current original position and
  rotation. This does not reset your door to the original ZIP coordinates.
- Frame and leaf are separate MjBodies. Source material/texture is reused when
  available; the included original texture is a fallback.
- Only after the replacement is ready, the original GameObject is deactivated.
  Its old large collider is therefore not meant to participate in Play.
- New door-specific MjGlobalSettings, if canonical import emits any, are removed.
  Existing world global settings are untouched.
- Undo is registered. MainScene is marked dirty but NOT silently saved over.

The old source world XML remains unchanged. Reimporting the old whole world
later will create its old doors again. Keep the saved Unity scene and generated
small door MJCFs; do not reimport the old world to apply this update.

## Inspect and test

Select `..._Sliding`. Its custom Inspector shows the joint, moving leaf and
front/back approach/grasp targets. `Validate This Upgrade` checks the expected
hierarchy, inactive original, and missing references. This is a structure check,
not a native scene simulation or a clearance/grasp certification.

Gizmos on the selected root: orange = closed envelope, cyan = open envelope and
swept volume. The envelope includes the handles. It is not a Collider.

**Edit-mode initial pose buttons**:

- Start Closed: start scenario with door shut.
- Start Half Open / Start Open: starting pose check, NOT runtime animation.
- Reverse Opening Direction: changes the scene joint axis and resets to closed.

These buttons update the leaf pose and `MjSlideJoint.Configuration` (MJCF `ref`)
together. Do NOT move the leaf alone or change `Configuration` as a runtime
controller. Move the replacement ROOT to reposition the entire doorway.
Changing these Inspector options affects the saved Unity scene; it does not
rewrite the generated MJCF file. Use an appropriate export/review if later
moving back to a native-only workflow.

### Mouse-spring smoke test

Keep existing 0.001 s Fixed Timestep and existing global settings. Start Play.
Select the moving leaf MjBody (the Inspector button helps), switch to Scene view
and use MuJoCo's **Ctrl + left-drag** mouse spring along the rail. Shift can be
added for horizontal-plane dragging. Do NOT use Unity's Transform arrows to
claim physical actuation. The frame should stay fixed; the leaf and handles
should translate together within the limit. Joint limits are soft constraints,
so small penetration at a stop under load is possible.

This tests rail mobility, NOT gripping the handle with FR3. The robot has no new
controller in this package and will not autonomously open the door. If the leaf
jams, inspect other world collisions, the inactive original and travel space;
do not globally disable collisions.

## Restore

Stop Play, select an upgraded root, then press **Restore Original Door** in its
Inspector, or use **Tools > SDx > Doors > Restore Selected Original**. This removes
that replacement and reactivates its original. Generated assets are deliberately
left intact; they may be shared by saved backup scenes. Alternatively open the
saved backup scene. Do not enable both old and new doors simultaneously.

## Files to retain in Git

Keep this package's source, models, texture, `Generated/`, all `.meta` files,
the changed MainScene, and the imported mesh/material assets under
`Assets/Local/MjImports/`. Those imported assets are not disposable Library cache.
Scene backups can be kept outside the published repository; a package-local
.gitignore excludes `Backups/`. No root Git configuration is changed.

## Coordinate convention / next stage

Source MJCF local X = depth, local Y = width/sliding axis, local Z = up.
Unity plugin maps this to local X = depth, local Z = width, local Y = up.
`FrontGrasp`, `BackGrasp`, `FrontApproach`, `BackApproach` are target sites tied to
the moving leaf; they move with the door. Their orientations are model reference
frames, not a ready-to-use Franka end-effector pose. Gripper-frame offsets and
IK, force limits, contact/friction, free-arm selection, base positioning and
opening/placement task logic are later work.

## Sources

- User-provided `aws-robomaker-small-house-world-mujoco.zip`, FoldingDoor_01 model.
- https://github.com/aws-robotics/aws-robomaker-small-house-world/tree/ros1
- https://mujoco.readthedocs.io/en/stable/unity.html
- https://mujoco.readthedocs.io/en/stable/XMLreference.html

See `ThirdParty/` for original-model attribution and modifications.
