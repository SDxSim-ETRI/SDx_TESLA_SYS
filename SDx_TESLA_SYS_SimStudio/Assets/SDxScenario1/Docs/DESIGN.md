# Scenario 1 implementation notes

## Scope

The provided 2026-10-22 SimStudio archive is the basis. The subsequent screenshots
specify the new robot/book starts, and the reference video illustrates the broad
route, not measured trajectories. This is a fixed-layout simulation experiment,
not perception or general-purpose task-and-motion planning.

- Command: Korean "scenario 1" (`\uC0C1\uD6691`). Whitespace around/between is accepted.
- Left FR3 grasps; the other arm holds a calibrated home pose.
- Source book: the existing `book_01306 (1)` instance, moved out of `Books_Left`.
- Start wrapper: robot Unity (0.581,0,1.635), book (1.588,0.15111069,1.644).
- Book's internal body offset is Unity Y +0.35. The 2.31 mm wrapper-height
  adjustment relative to the screenshot avoids initial table penetration.
- Both sliding doors start fully open; their motors are not introduced.
- Right shelf and book group are inactive. Left shelf remains in its existing pose.

## Physics, not a parented visual prop

Only reset writes native qpos. During execution base and arms are driven by their
existing actuators. Book/robot transforms are not animated by this module.
No book-parent change, weld equality, mocap attachment, exterior force on the base,
or obstacle-collision disable is used to fake task success.
The source book wrapper is reparented once in the **Editor** to make it an independent
scene object, not attached to the gripper during play.

FR3 motors use PD torque commands with limits 87 Nm on joints 1-4 and 12 Nm on
joints 5-7. The existing arm body gravity-compensation parameters are retained.
Both gripper tendon servos use gain .1568627451, bias (0,-1000,-50), force limit
[-40,40], with input 0=closed and 255=open. These are experiment tuning values,
not a real-hardware commissioning recommendation.

Runtime-only contact tuning: elliptic cone, impratio=10, noslip_iterations=0;
task book friction=(1.5,.1,.003), solref=(.005,1), solimp first three=(.99,.999,.001).
Original component assets are not rewritten. These runtime changes affect the
current unified MuJoCo model, and are re-applied on each model recreation.
The fixed timestep remains .001 and the existing implicitfast/Newton setup is retained.
Global noslip was rejected during tuning because it impeded skid-steer motion.

## Control flow

A dedicated unsafe-enabled `SDx.Scenario1` assembly references the installed MuJoCo
runtime assembly. No global Allow unsafe code setting is required. The existing
GUI and door classes remain in the default assembly; a separate default-assembly
bridge connects the GUI's existing UnityEvent without replacing UI code.

Control uses MjScene.ctrlCallback (after mj_step1, before mj_step2). Each command
is written to the native ctrl array AND MjActuator.Control, since the plugin also
synchronizes actuator values. Reset runs in preUpdateEvent, not during scene drawing.
UI submissions only queue the reset. Reset clears velocities, controls, warm-start
and simulation time with mj_resetData, restores the saved initial full qpos and
recomputes forward state. Other books are reset as well for repeatability.

The arm path is quintic joint interpolation through precomputed poses. Base
navigation follows calibrated waypoints with wheel feedback and acceleration
limits. A yaw feedforward gain of 2.0 converts commanded rotation to differential
wheel speed for the original skid-steer contact model; this is NOT track half-width.
At the shelf, a small damped-Jacobian IK correction is solved in separate mjData
using the actual base arrival pose and actual book-to-hand transform. This is
servo correction around known targets, not perception or a new route search.
Fixed elbow-biased seeds are important to avoid the other arm.

## Coordinates (Unity)

Start: (0.581,0,1.635).
Route position goals: (-.45,0,1.635) reversing; (-.45,0,-3.0);
(3.1,0,-3.15); (3.1,0,-4.5); (4.18,0,-4.5).
Turns: native yaw -pi/2, 0, -pi/2, 0, 0. Native +X is robot forward.
Unity/native vector conversion is (x,y,z)_Unity -> (x,z,y)_native.
Middle shelf support is Unity Y .790 m. Target book center is
Unity (5.23,.924364,-4.5); final upright book orientation is identity.
The body of a transported book is measured, not the imported wrapper's pivot.

## Boundaries

Native validation reconstructs collision bodies from the archive and screenshot
changes. It is not a Unity-exported current runtime model. Latest unsaved scene
changes outside the specified setup are not known. The large HouseWallB collision
mask was already disabled in the source and is not enabled by this package; the
route respects the visible opening and existing RoomWall collision without
silently enabling a solid convex hull around the whole building.

The C# source has not been compiled or run in Unity in this environment. The native
trial does not certify Unity rendering, callback ordering, or an arbitrary edited
scene. Structural guards prevent invalid references/NaNs from being presented as
successful runs. No failure animation or fallback attachment is included.
