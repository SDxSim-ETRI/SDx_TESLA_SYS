using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 5f;       // 기본 이동 속도
    public float fastSpeed = 15f;      // Shift 키를 눌렀을 때 속도
    public float sensitivity = 2f;     // 마우스 감도

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // 마우스 커서를 화면 중앙에 고정 & 보이지 않게
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 시작 각도 기록
        Vector3 rot = transform.localRotation.eulerAngles;
        rotationX = rot.y;
        rotationY = rot.x;
    }

    void Update()
    {
        // 마우스 입력
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        rotationX += mouseX * sensitivity;
        rotationY -= mouseY * sensitivity;
        rotationY = Mathf.Clamp(rotationY, -90f, 90f); // 위아래 회전 제한

        transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0f);

        // 이동 속도
        float speed = Input.GetKey(KeyCode.LeftShift) ? fastSpeed : moveSpeed;

        // 키보드 입력 (WASD + QE)
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.E)) move += transform.up;
        if (Input.GetKey(KeyCode.Q)) move -= transform.up;

        transform.position += move.normalized * speed * Time.deltaTime;

        // ESC 누르면 커서 잠금 해제
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
