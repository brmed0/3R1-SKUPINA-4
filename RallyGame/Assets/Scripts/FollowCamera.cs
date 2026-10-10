using UnityEngine;

// Chase camera that stays behind the car.
// Attach to the Main Camera (NOT as a child of the car) and drag the car into "Target".
[RequireComponent(typeof(Camera))]
public class CameraScripz : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private float distance = 6f;
    [SerializeField] private float height = 2.5f;
    [SerializeField] private float lookHeight = 1f;

    [Header("Smoothing")]
    [Tooltip("Higher = camera sticks to its spot more tightly.")]
    [SerializeField] private float positionSmoothing = 10f;
    [Tooltip("Higher = camera swings behind the car faster. Lower = more drift view.")]
    [SerializeField] private float rotationSmoothing = 3f;

    [Header("Speed FOV (set boost to 0 to turn off)")]
    [SerializeField] private float baseFov = 60f;
    [SerializeField] private float fovBoost = 12f;
    [SerializeField] private float speedForMaxFov = 40f; // m/s (about 144 km/h)

    private Camera cam;
    private float currentYaw;
    private Vector3 lastTargetPosition;
    private float smoothedSpeed;

    private void Start()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            Debug.LogError("[FollowCamera] Drag your car into the Target slot.", this);
            enabled = false;
            return;
        }

        currentYaw = target.eulerAngles.y;
        lastTargetPosition = target.position;

        // Start right behind the car instead of flying in from wherever the camera was.
        transform.position = GetDesiredPosition();
        transform.LookAt(target.position + Vector3.up * lookHeight);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Follow the car's heading only (ignores pitch and roll).
        currentYaw = Mathf.LerpAngle(currentYaw, target.eulerAngles.y, 1f - Mathf.Exp(-rotationSmoothing * dt));

        // Move behind the car.
        transform.position = Vector3.Lerp(
            transform.position,
            GetDesiredPosition(),
            1f - Mathf.Exp(-positionSmoothing * dt));

        transform.LookAt(target.position + Vector3.up * lookHeight);

        // Widen the FOV as the car speeds up.
        float speed = (target.position - lastTargetPosition).magnitude / dt;
        lastTargetPosition = target.position;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-5f * dt));

        float targetFov = baseFov + fovBoost * Mathf.Clamp01(smoothedSpeed / speedForMaxFov);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-4f * dt));
    }

    private Vector3 GetDesiredPosition()
    {
        Quaternion yawOnly = Quaternion.Euler(0f, currentYaw, 0f);
        return target.position - yawOnly * Vector3.forward * distance + Vector3.up * height;
    }
}