using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using TwistMsg = RosMessageTypes.Geometry.TwistMsg;

public class CmdVelSubscriber : MonoBehaviour
{
    [Header("Topic")]
    public string cmdVelTopic = "/cmd_vel"; // ROS 쪽과 맞추세요
    [Header("Movement")]
    public float linearScale = 1.0f;  // m/s -> Unity 단위 변환 스케일
    public float angularScale = 1.0f; // rad/s -> Unity 회전 스케일
    public bool usePhysics = true;

    Rigidbody rb;
    Vector3 lastLinear;
    float lastAngularZ;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(cmdVelTopic, OnTwist);
    }

    void OnTwist(TwistMsg msg)
    {
        // ROS: x forward, z yaw
        lastLinear = new Vector3((float)msg.linear.x, 0f, 0f) * linearScale;
        lastAngularZ = (float)msg.angular.z * angularScale;
    }

    void FixedUpdate()
    {
        // 전진은 로컬 x, 회전은 yaw로 적용
        var forward = transform.right; // Unity의 좌표계(URDF/임포트 축)에 따라 forward 축이 다를 수 있습니다.
        if (usePhysics && rb != null)
        {
            rb.MovePosition(rb.position + forward * lastLinear.x * Time.fixedDeltaTime);
            rb.MoveRotation(Quaternion.AngleAxis(Mathf.Rad2Deg * lastAngularZ * Time.fixedDeltaTime, Vector3.up) * rb.rotation);
        }
        else
        {
            transform.position += forward * lastLinear.x * Time.deltaTime;
            transform.Rotate(Vector3.up, Mathf.Rad2Deg * lastAngularZ * Time.deltaTime, Space.World);
        }
    }
}
