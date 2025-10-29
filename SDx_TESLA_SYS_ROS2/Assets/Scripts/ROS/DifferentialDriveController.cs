using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using TwistMsg = RosMessageTypes.Geometry.TwistMsg;

public class DifferentialDriveController : MonoBehaviour
{
    public string cmdVelTopic = "/cmd_vel";
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;
    public float wheelRadius = 0.165f;
    public float trackWidth  = 0.55f;
    public float maxRPM = 120f;

    float v, w;
    float testUntil = 2f;   // 2초간 강제 전진 테스트

    ArticulationBody[] allWheels;
    ArticulationBody baseBody;

    void Awake()
    {
        // 루트(베이스) 찾기
        baseBody = GetComponent<ArticulationBody>();
        // 배열 합치기
        allWheels = new ArticulationBody[leftWheels.Length + rightWheels.Length];
        leftWheels.CopyTo(allWheels, 0);
        rightWheels.CopyTo(allWheels, leftWheels.Length);
    }

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(cmdVelTopic, OnTwist);
        PrepDrives(leftWheels);
        PrepDrives(rightWheels);
    }

    void OnTwist(TwistMsg msg)
    {
        // 테스트 구간 동안은 외부 입력 무시 (0으로 덮어써서 멈추는 현상 방지)
        if (Time.time < testUntil) return;
        v = (float)msg.linear.x;
        w = (float)msg.angular.z;
    }

    void FixedUpdate()
    {
        float wl = (v - w * (trackWidth * 0.5f)) / Mathf.Max(1e-6f, wheelRadius);
        float wr = (v + w * (trackWidth * 0.5f)) / Mathf.Max(1e-6f, wheelRadius);

        float maxRad = maxRPM * 2f * Mathf.PI / 60f;
        wl = Mathf.Clamp(wl, -maxRad, maxRad);
        wr = Mathf.Clamp(wr, -maxRad, maxRad);

        // 수면 방지 (매 프레임 깨워두기)
        if (baseBody != null) baseBody.WakeUp();
        foreach (var wbody in allWheels) if (wbody != null) wbody.WakeUp();

        ApplyVelocity(leftWheels, wl);
        ApplyVelocity(rightWheels, wr);
    }

    void PrepDrives(ArticulationBody[] wheels)
    {
        foreach (var w in wheels)
        {
            if (w == null) continue;
            var d = w.xDrive;
            d.stiffness   = 0f;
            d.damping     = 10f;
            d.forceLimit  = 20000f; // 더 키움
            d.target      = 0f;
            d.targetVelocity = 0f;
            d.driveType   = ArticulationDriveType.Velocity; // 핵심
            w.xDrive = d;
            w.jointFriction = 0.1f;
        }
    }

    void ApplyVelocity(ArticulationBody[] wheels, float omega)
    {
        foreach (var w in wheels)
        {
            if (w == null) continue;
            var d = w.xDrive;
            d.target = 0f;
            d.targetVelocity = omega;   // rad/s
            d.driveType = ArticulationDriveType.Velocity;   // 매 프레임 보강
            w.xDrive = d;
        }
    }
}
