# Source assets and modification notice

Visual mesh and texture source: user-uploaded
`aws-robomaker-small-house-world-mujoco.zip` / models /
`aws_robomaker_residential_FoldingDoor_01`.

Original upstream repository:
https://github.com/aws-robotics/aws-robomaker-small-house-world/tree/ros1

The original uploaded archive did not contain a top-level license/notice in the
extracted model files. `AWS_UPSTREAM_LICENSE.txt` records the license text
retrieved from the upstream ros1 LICENSE endpoint during this task. This file is
provenance, not a new license for unrelated robot/world assets and not a complete
third-party-asset legal audit. Preserve applicable upstream notices on public
redistribution.

Derived changes (2026-10-22): split frame/leaf; convert centimetric coordinates
by 0.01 into meters; remove original handle triangles; retain original UVs;
create independent primitive handle geoms and passive slide physics. No texture
pixels were modified. Source SHA-256 and geometric measurements are in
Docs/model_measurements.json.

The C# and Python tools are newly authored for this task; no MuJoCo source code,
MuJoCo binary, Unity binary or font file is bundled. They call existing public
MuJoCo Unity and Unity Editor APIs. No other models from the house are included.
