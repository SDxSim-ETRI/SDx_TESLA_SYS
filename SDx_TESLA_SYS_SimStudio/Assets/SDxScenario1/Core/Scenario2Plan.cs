// SDx Scenario 2 v1.0.0. Native coordinates: X/Y horizontal, Z up.
// Calibrated for the supplied fixed-layout house and passive door model.
namespace SDx.Scenario1 {
  internal static class Scenario2Plan {
    internal const string DoorOneSuffix="FoldingDoor_01_FoldingDoor_01_001_Sliding";
    internal const string DoorTwoSuffix="FoldingDoor_01_FoldingDoor_01_002_Sliding";
    internal const double DoorStopX=-.45, DoorStopY=-1.40;
    internal const double AlignmentX=-.75;
    internal const double ReturnX=-.45, ReturnY=-1.63;
    internal const double PullTravel=2.4, PullMaxSpeed=.12;
    internal const double OpenMinimum=2.35;
    // A seed selects the elbow branch; IK still uses the actual stopped base pose.
    internal static readonly double[] RightPreHandleSeed={
      0.24188058164843274,-0.5878455873154477,0.22948312502967383,
      -2.619353204889843,-1.502708149897416,1.7153312575826,-0.3112658147063412
    };
  }
}
