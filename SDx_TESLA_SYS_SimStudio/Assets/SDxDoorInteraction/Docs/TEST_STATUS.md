# Verification status

## Performed in the authoring environment

- Read original FoldingDoor_01 XML, visual OBJ, collision OBJ and texture from
  the uploaded world archive (not inferred from the screenshot).
- Welded equal-position vertices for connected-component analysis (127 parts).
- Partitioned the source's 10,668 triangles: 84 fixed-frame, 10,104 rigid-leaf,
  480 old-handle triangles removed. UV coordinates are retained.
- Parsed generated XML and checked every file/mesh reference and OBJ face/UV
  index; checked positive inertial values and the inertia triangle inequality.
- Confirmed one passive slide, no free joint, no actuator, no global option/size,
  no floor or equality constraints; confirmed 10 contact geoms and 4 target sites.
- Checked the two grip-channel clearances (>60 mm), the fixed-frame/leaf depth
  separation for both rail sides (>20 mm), and full-aperture clearance at 2.4 m
  travel for both opening directions using geometric bounds.
- All 12 geometry tests passed. Python syntax/static tests and ZIP integrity
  verified. See generated reports.
- C# source was inspected for API use, edit-only guards, culture-invariant XML,
  per-door scoping, relative paths, duplicate checks, Undo and restoration paths.

## NOT performed here

- No Unity Editor or C# compiler is present in this authoring environment.
  The C# files have NOT been compiled or clicked through in Unity.
- No real MuJoCo native binary is available here. Native XML compilation,
  numerical time stepping, contact tests, and Unity scene simulation have NOT
  been executed here. Internet retrieval of a binary was unavailable.
- The exact installed 3.16.0 Unity source was not retrievable. APIs were compared
  with the official public Unity plugin documentation/source; the user's
  installed plugin must pass its own compile/import checks.
- Current MainScene, latest asset transforms and live MuJoCo contact state were
  not supplied. Adjacent walls/furniture/other door overlap is NOT validated.
- No real FR3 gripper grasp, robot-arm IK, autonomous navigation, insertion,
  holding a book while opening the door, or performance test was performed.

## Runnable on the user's existing MuJoCo library

`Tools/verify_doors.py --library /absolute/path/libmujoco.so` performs native
compilation and isolated mechanics tests in a subprocess, using public C APIs
without struct-layout assumptions:

1. Both opening directions x both rail sides: stationary under gravity with no
   input, open with a Cartesian force applied at a handle point, close with
   reverse force, finite joint state and bounded soft-limit penetration.
2. Both opening directions: a small physical spherical test finger pushes the
   grip bar through contact. This is NOT an FR3 pinching/grasping validation.

The Python tool does not open or modify Unity, the native library, the original
world XML, or the saved scene. It prints explicit failure messages rather than
claiming simulation success from static checks alone.

Important: A mathematically valid gap and successful isolated mechanics test do
not establish task-level feasibility in the user's full house scene.
