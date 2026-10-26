using System;
// SDX_EXECUTION_LOG_BEGIN
using SDx.SimStudio.Logging;
// SDX_EXECUTION_LOG_END
using System.Linq;
using Mujoco;
using UnityEngine;

namespace SDx.Scenario1 {
  // This is a partial of the EXISTING controller, not a second actuator writer.
  public sealed unsafe partial class SDxScenario1Controller {
    enum DoorStep {
      ApproachStop, FaceRail, SettleFace, AlignReverse, SettleAlign, SquareToRail,
      SettleSquare, PreHandle, AtHandle, CloseGrip, PullAlongRail, HoldOpen,
      ReleaseHandle, WithdrawHand, HomeRight, ReturnCorridor, FacePassage, Done
    }
    int doorStep=-1;
    bool doorFinished, doorAdvancePending;
    double doorAge, doorDuration, rightGrip=255;
    double doorX,doorY,doorYaw;
    // SDX_DOOR_ALIGNMENT_FIX_1_0
    bool doorAlignBraking;
    MjBody rightHand,doorLeaf;
    MjSlideJoint doorOneSlide,doorTwoSlide;
    Transform doorFrontGrasp;
    Vector3 doorGripLocal;
    readonly double[] rightGoal=new double[7],rightFrom=new double[7],rightTo=new double[7];
    bool DoorSequenceActive => doorStep>=0 && doorStep<(int)DoorStep.Done;
    // Quaternion math below is applied to native-coordinate vectors, as in Scenario 1 IK.
    static Quaternion DoorHandRotation => Quaternion.LookRotation(new Vector3(0,-1,0),new Vector3(1,0,0));
    double RightArmGoal(int i) => rightGoal[i];

    void ResetDoorSequence() {
      doorStep=-1;doorFinished=false;doorAdvancePending=false;doorAge=0;
      rightGrip=255;doorAlignBraking=false;
      Array.Copy(Scenario1Plan.home,rightGoal,7);
      Array.Copy(rightGoal,rightFrom,7);Array.Copy(rightGoal,rightTo,7);
    }
    static Transform DoorAncestor(Transform node,string suffix) {
      for(var t=node;t!=null;t=t.parent)
        if(t.name.EndsWith(suffix,StringComparison.Ordinal))return t;
      return null;
    }
    static MjSlideJoint UniqueDoor(MjSlideJoint[] slides,string suffix) {
      if(slides==null)throw new InvalidOperationException("The existing DoorSlides references are missing.");
      var matches=slides.Where(j=>j!=null && DoorAncestor(j.transform,suffix)!=null).ToArray();
      if(matches.Length!=1)throw new InvalidOperationException("Scenario 1 requires exactly one upgraded "+suffix+" in DoorSlides.");
      return matches[0];
    }
    void ResolveDoorBindings() {
      if(RobotRoot==null)throw new InvalidOperationException("Keep the existing prepared SDx_Scenario1 component and references.");
      var hands=RobotRoot.GetComponentsInChildren<MjBody>(true).Where(b=>b.name=="right_fr3_hand").ToArray();
      if(hands.Length!=1)throw new InvalidOperationException("Expected one right_fr3_hand MjBody.");
      rightHand=hands[0];
      doorOneSlide=UniqueDoor(DoorSlides,Scenario2Plan.DoorOneSuffix);
      doorTwoSlide=UniqueDoor(DoorSlides,Scenario2Plan.DoorTwoSuffix);
      doorLeaf=doorOneSlide.GetComponentInParent<MjBody>();
      if(doorLeaf==null)throw new InvalidOperationException("Door 001 slide has no moving MjBody parent.");
      var root=DoorAncestor(doorOneSlide.transform,Scenario2Plan.DoorOneSuffix);
      var targets=root.GetComponentsInChildren<Transform>(true)
        .Where(t=>t.name.EndsWith("_front_grasp",StringComparison.Ordinal)).ToArray();
      if(targets.Length!=1)throw new InvalidOperationException("Expected the existing door 001 front_grasp target.");
      doorFrontGrasp=targets[0];
      if(!doorFrontGrasp.IsChildOf(doorLeaf.transform))throw new InvalidOperationException("Front grasp target must move with door 001 leaf.");
      if(!doorOneSlide.gameObject.activeInHierarchy || !doorTwoSlide.gameObject.activeInHierarchy)
        throw new InvalidOperationException("Both upgraded sliding doors must be active; keep the old fixed doors inactive.");
      doorGripLocal=Native(doorLeaf.transform.InverseTransformPoint(doorFrontGrasp.position));
      if(Vector3.Distance(doorGripLocal,new Vector3(.085f,0,.85f))>.003f)
        throw new InvalidOperationException("Door handle geometry differs from the calibrated passive door v1.0.");
    }
    public bool ValidateDoorScenarioSetup(out string message) {
      try {
        ResolveDoorBindings();CheckArray(RightJoints,7);CheckArray(RightMotors,7);
        if(RightGripper==null)throw new InvalidOperationException("Right gripper reference is missing.");
        message="Scenario 1 bindings OK: right arm + door 001 front handle; door 002 reset only. This is not a dynamic grasp test.";
        return true;
      } catch(Exception ex){message=ex.Message;return false;}
    }
    Pose DoorGripPose() {
      Pose leaf=BodyPose(data,doorLeaf.MujocoId);
      leaf.position+=leaf.rotation*doorGripLocal;
      return leaf;
    }
    void CheckClosedDoorCalibration() {
      Vector3 expected=new Vector3(.01f,-2.2161f,.891881f);
      if(Vector3.Distance(DoorGripPose().position,expected)>.035f)
        throw new InvalidOperationException("Door 001 CLOSED handle is not at the calibrated native pose (0.01,-2.2161,0.891881). Scene transforms were not overwritten.");
      int id=doorOneSlide.MujocoId;
      Vector3 axis=BodyPose(data,doorLeaf.MujocoId).rotation*new Vector3(
        (float)model->jnt_axis[3*id],(float)model->jnt_axis[3*id+1],(float)model->jnt_axis[3*id+2]);
      if(Vector3.Dot(axis.normalized,Vector3.left)<.999f)
        throw new InvalidOperationException("Door 001 must open along native -X (Unity -X). Keep the calibrated opening direction.");
      if(Math.Abs(model->jnt_range[2*id])>.001 || Math.Abs(model->jnt_range[2*id+1]-2.4)>.01)
        throw new InvalidOperationException("Door 001 requires the existing 0..2.4 m passive slide range.");
    }
    void CaptureDoorHold() {
      int a=RobotFreeJoint.QposAddress;
      holdXY=new Vector2((float)data->qpos[a],(float)data->qpos[a+1]);holdYaw=BaseYaw();speed=angular=0;
    }
    void BeginDoorSequence() {EnterDoorStep(DoorStep.ApproachStop);}
    void DoorMotion(string label,double seconds,double[] goal=null) {
      status=label;doorDuration=seconds;
      if(goal!=null)Array.Copy(goal,rightTo,7);
    }
    void EnterDoorStep(DoorStep next) {
      doorStep=(int)next;doorAge=0;doorAdvancePending=false;age=0;doorDuration=70;
      Array.Copy(rightGoal,rightFrom,7);Array.Copy(rightGoal,rightTo,7);
      switch(next) {
        case DoorStep.ApproachStop:
          status="Door 001: stop before closed passage";doorX=Scenario2Plan.DoorStopX;doorY=Scenario2Plan.DoorStopY;break;
        case DoorStep.FaceRail:
          status="Door 001: turn parallel to rail";doorYaw=0;break;
        case DoorStep.SettleFace:
          CaptureDoorHold();DoorMotion("Door 001: settle for alignment",2);break;
        case DoorStep.AlignReverse:
          status="Door 001: straight reverse alignment";
          doorX=Scenario2Plan.AlignmentX;
          doorY=data->qpos[RobotFreeJoint.QposAddress+1];
          doorYaw=0;doorAlignBraking=false;break;
        case DoorStep.SettleAlign:
          CaptureDoorHold();DoorMotion("Door 001: alignment settle",1);break;
        case DoorStep.SquareToRail:
          status="Door 001: square base to rail";doorYaw=0;break;
        case DoorStep.SettleSquare:
          CaptureDoorHold();DoorMotion("Door 001: hold base",1);break;
        case DoorStep.PreHandle:
          rightGrip=255;
          DoorMotion("Door 001: right hand approach",5,
            SolveArm(DoorGripPose().position+new Vector3(0,.15f,0),DoorHandRotation,
              Scenario2Plan.RightPreHandleSeed,RightJoints,rightHand));break;
        case DoorStep.AtHandle:
          DoorMotion("Door 001: right hand at handle",3,
            SolveArm(DoorGripPose().position,DoorHandRotation,rightGoal,RightJoints,rightHand));break;
        case DoorStep.CloseGrip:
          rightGrip=0;DoorMotion("Door 001: close physical right gripper",2);break;
        case DoorStep.PullAlongRail:
          status="Door 001: pull with right hand and mobile base";doorDuration=45;
          doorX=data->qpos[RobotFreeJoint.QposAddress]-Scenario2Plan.PullTravel;
          doorY=data->qpos[RobotFreeJoint.QposAddress+1];doorYaw=0;break;
        case DoorStep.HoldOpen:
          CaptureDoorHold();DoorMotion("Door 001: hold open, left hand holds book",1);break;
        case DoorStep.ReleaseHandle:
          // Do not falsely report success or move a blocked door by animation.
          if(data->qpos[doorOneSlide.QposAddress]<Scenario2Plan.OpenMinimum)
            throw new InvalidOperationException("Door 001 did not reach the verified open position. No teleport or weld fallback is used.");
          rightGrip=255;DoorMotion("Door 001: release handle",1);break;
        case DoorStep.WithdrawHand:
          DoorMotion("Door 001: withdraw right gripper",3,
            SolveArm(DoorGripPose().position+new Vector3(0,.15f,0),DoorHandRotation,rightGoal,RightJoints,rightHand));break;
        case DoorStep.HomeRight:
          DoorMotion("Door 001: return right arm home",5,Scenario1Plan.home);break;
        case DoorStep.ReturnCorridor:
          status="Door 001: return to original corridor";doorX=Scenario2Plan.ReturnX;doorY=Scenario2Plan.ReturnY;break;
        case DoorStep.FacePassage:
          status="Door 001: face open passage";doorYaw=-Math.PI/2;break;
        case DoorStep.Done:
          doorFinished=true;rightGrip=255;Array.Copy(Scenario1Plan.home,rightGoal,7);
          status="Navigate fixed route through opened door 001";duration=999;age=0;
          RecordMilestone("Door 001 opened by contact; resume Scenario 1 waypoint 2");break;
      }
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.DoorStage((int)next,status);
      // SDX_EXECUTION_LOG_END
    }
    static bool DoorNavigates(DoorStep p) => p==DoorStep.ApproachStop || p==DoorStep.FaceRail ||
      p==DoorStep.AlignReverse || p==DoorStep.SquareToRail || p==DoorStep.PullAlongRail ||
      p==DoorStep.ReturnCorridor || p==DoorStep.FacePassage;
    void RunDoorControl(double dt,double x,double y,double yaw,out double vc,out double wc,out bool navigate) {
      vc=wc=0;DoorStep p=(DoorStep)doorStep;navigate=DoorNavigates(p);
      if(!navigate) {
        double f=Math.Min(1,(doorAge+dt)/doorDuration),s=f*f*f*(10+f*(-15+6*f));
        for(int i=0;i<7;i++)rightGoal[i]=rightFrom[i]+(rightTo[i]-rightFrom[i])*s;
        return;
      }
      bool reached;
      if(p==DoorStep.FaceRail || p==DoorStep.SquareToRail || p==DoorStep.FacePassage) {
        double error=Wrap(doorYaw-yaw);
        reached=Math.Abs(error)<.008 && Math.Abs(angular)<.015;
        wc=Math.Abs(error)<.008?0:Clamp(3*error,-.35,.35);
      } else if(p==DoorStep.AlignReverse) {
        // Native -X is the calibrated reverse direction at yaw zero.
        // Do not use atan2(target-current) near this short segment's end:
        // a tiny lateral residual must not become a 90/180 degree turn.
        double remaining=x-doorX;
        double lateral=doorY-y;
        double railError=Wrap(doorYaw-yaw);

        // Stop on a materially different layout instead of steering around
        // the target beside a closed door.
        if(Math.Abs(lateral)>.05 || remaining<-.04 ||
           Math.Abs(railError)>.35)
          throw new InvalidOperationException(
            "Door alignment departed from the fixed rail corridor. " +
            "remaining="+remaining.ToString("F4")+
            ", lateral="+lateral.ToString("F4")+
            ", yawError="+railError.ToString("F4")+
            ". No turn-around or teleport fallback was applied.");

        if(!doorAlignBraking && remaining<=.012) {
          doorAlignBraking=true;
          Debug.Log(ScenarioTag+
            "DoorAlignFix 1.0.0: brake without target re-chasing. "+
            "base(native)=("+x.ToString("F4")+","+y.ToString("F4")+
            "), remaining="+remaining.ToString("F4")+
            ", lateral="+lateral.ToString("F4"),this);
        }

        if(doorAlignBraking) {
          // Latch braking; do not reacquire a point behind the vehicle.
          vc=wc=0;
          int d=checked((int)model->jnt_dofadr[RobotFreeJoint.MujocoId]);
          double vx=data->qvel[d],vy=data->qvel[d+1];
          double wx=data->qvel[d+3],wy=data->qvel[d+4],wz=data->qvel[d+5];
          double measuredSpeed=Math.Sqrt(vx*vx+vy*vy);
          double measuredAngular=Math.Sqrt(wx*wx+wy*wy+wz*wz);
          reached=Math.Abs(speed)<.018 && Math.Abs(angular)<.015 &&
            measuredSpeed<.025 && measuredAngular<.05;
        } else {
          // For reverse travel, the lateral correction has the opposite sign.
          // Bounded yaw reference avoids pointing sideways near the target.
          double correction=Clamp(-.4*lateral,-.08,.08);
          double heading=Wrap(doorYaw+correction-yaw);
          vc=-Math.Min(.12,.9*Math.Max(0,remaining));
          wc=Clamp(3*heading,-.12,.12);
          reached=false;
        }
      } else if(p==DoorStep.PullAlongRail) {
        double error=doorX-x;
        vc=Clamp(.7*error,-Scenario2Plan.PullMaxSpeed,Scenario2Plan.PullMaxSpeed);
        wc=Clamp(3*Wrap(doorYaw-yaw)-1.2*(doorY-y),-.12,.12);
        reached=Math.Abs(error)<.006 && Math.Abs(speed)<.015;
        if(Math.Abs(error)<.006)vc=0;
      } else {
        double dx=doorX-x,dy=doorY-y,dist=Math.Sqrt(dx*dx+dy*dy);
        bool reverse=p==DoorStep.AlignReverse;
        double error=Wrap(Math.Atan2(dy,dx)+(reverse?Math.PI:0)-yaw);
        reached=dist<.012 && Math.Abs(speed)<.018;
        vc=Math.Min(.28,.9*dist)*(reverse?-1:1)*Math.Max(0,Math.Cos(error));
        wc=Clamp(3*error,-.4,.4);if(dist<.012)vc=wc=0;
      }
      if(reached)doorAdvancePending=true;
      if(doorAge>doorDuration)
        throw new InvalidOperationException("Scenario 1 calibrated door maneuver is blocked. No collision disabling or base teleport is applied.");
    }
    void AdvanceDoorAfterStep(double dt) {
      doorAge+=dt;
      var p=(DoorStep)doorStep;
      if(doorAdvancePending || (!DoorNavigates(p) && doorAge>=doorDuration-1e-8)) {
        RecordMilestone(status);EnterDoorStep((DoorStep)(doorStep+1));
      }
    }
  }
}
