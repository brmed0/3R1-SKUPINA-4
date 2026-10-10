using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class WheelController : MonoBehaviour
{
    public enum DriveType { AllWheelDrive, FrontWheelDrive, RearWheelDrive }

    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontRight;
    [SerializeField] private WheelCollider frontLeft;
    [SerializeField] private WheelCollider backRight;
    [SerializeField] private WheelCollider backLeft;

    [Header("Wheel Meshes (optional)")]
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform backRightMesh;
    [SerializeField] private Transform backLeftMesh;

    [Header("Body")]
    [SerializeField] private float mass = 1500f;
    [Tooltip("Offset from axle height. Negative = lower center of mass = harder to flip.")]
    [SerializeField] private float centerOfMassYOffset = -0.15f;

    [Header("Engine")]
    [SerializeField] private DriveType driveType = DriveType.AllWheelDrive;
    [SerializeField] private float totalMotorTorque = 2400f;
    [SerializeField] private float maxSpeedKmh = 120f;
    [SerializeField] private float maxReverseSpeedKmh = 25f;

    [Header("Brakes")]
    [SerializeField] private float brakeTorque = 3000f;
    [SerializeField, Range(0f, 1f)] private float rearBrakeShare = 0.6f;
    [SerializeField, Range(0f, 0.3f)] private float coastBraking = 0.05f;
    [SerializeField] private float handbrakeTorque = 2500f;

    [Header("Steering")]
    [SerializeField] private float maxSteerAngle = 28f;
    [SerializeField] private float steerSpeed = 120f;
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteerFactor = 0.3f;

    [Header("Suspension & Stability")]
    [SerializeField] private bool autoTuneSuspension = true;
    [SerializeField] private float suspensionDistance = 0.25f;
    [SerializeField] private bool useAntiRollBars = true;
    [SerializeField] private float antiRollStiffness = 5000f;

    private const int FL = 0, FR = 1, BL = 2, BR = 3;

    private Rigidbody rb;
    private WheelCollider[] wheels;      // FL, FR, BL, BR
    private Transform[] meshes;          // same order
    private Quaternion[] meshRotationOffset;

    private float throttleInput;
    private float steerInput;
    private bool brakeHeld;
    private bool handbrakeHeld;
    private float currentSteerAngle;

    private Vector3 Velocity =>
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity;
#else
        rb.velocity;
#endif

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        wheels = new[] { frontLeft, frontRight, backLeft, backRight };
        meshes = new[] { frontLeftMesh, frontRightMesh, backLeftMesh, backRightMesh };

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        rb.mass = mass;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.centerOfMass = CalculateCenterOfMass();

        if (autoTuneSuspension)
            TuneSuspension();

        // More physics sub-steps = steadier wheels at low speed.
        wheels[FL].ConfigureVehicleSubsteps(5f, 12, 15);

        CacheMeshOffsets();
    }

    private bool ValidateSetup()
    {
        string[] names = { "Front Left", "Front Right", "Back Left", "Back Right" };

        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null)
            {
                Debug.LogError($"[WheelController] {names[i]} WheelCollider is not assigned.", this);
                return false;
            }

            for (int j = i + 1; j < 4; j++)
            {
                if (wheels[i] == wheels[j])
                {
                    Debug.LogError($"[WheelController] {names[i]} and {names[j]} use the SAME WheelCollider.", this);
                    return false;
                }
            }

            if (Vector3.Dot(wheels[i].transform.up, transform.up) < 0.95f)
                Debug.LogWarning($"[WheelController] {names[i]} WheelCollider is rotated. Its rotation should be 0,0,0 relative to the car.", wheels[i]);

            if (meshes[i] != null && wheels[i].transform.IsChildOf(meshes[i]))
                Debug.LogWarning($"[WheelController] {names[i]} WheelCollider is the same object as, or a child of, its wheel mesh. Make them separate objects.", wheels[i]);
        }

        Vector3 fl = transform.InverseTransformPoint(wheels[FL].transform.position);
        Vector3 fr = transform.InverseTransformPoint(wheels[FR].transform.position);
        Vector3 bl = transform.InverseTransformPoint(wheels[BL].transform.position);
        Vector3 br = transform.InverseTransformPoint(wheels[BR].transform.position);

        if (fl.z <= bl.z || fr.z <= br.z)
            Debug.LogWarning("[WheelController] Front wheels are not in front of the back wheels (car forward must be +Z). Are the slots mixed up?", this);

        if (fl.x >= fr.x || bl.x >= br.x)
            Debug.LogWarning("[WheelController] A left wheel is on the right side of its right wheel. Are the slots mixed up?", this);

        if ((transform.lossyScale - Vector3.one).sqrMagnitude > 0.001f)
            Debug.LogWarning("[WheelController] The car root scale is not 1,1,1. WheelColliders behave badly on scaled objects.", this);

        return true;
    }

    private Vector3 CalculateCenterOfMass()
    {
        Vector3 center = Vector3.zero;
        foreach (WheelCollider w in wheels)
            center += w.transform.position;
        center /= wheels.Length;

        Vector3 local = transform.InverseTransformPoint(center);
        local.y += centerOfMassYOffset;
        return local;
    }

    private void TuneSuspension()
    {
        float sprungMass = rb.mass / 4f;
        float spring = sprungMass * Physics.gravity.magnitude / (suspensionDistance * 0.2f);
        float damper = Mathf.Sqrt(spring * sprungMass); // damping ratio of 0.5

        foreach (WheelCollider w in wheels)
        {
            w.suspensionDistance = suspensionDistance;
            w.suspensionSpring = new JointSpring
            {
                spring = spring,
                damper = damper,
                targetPosition = 0.5f
            };
        }
    }

    private void CacheMeshOffsets()
    {
        // Remember how each mesh is rotated relative to its collider,
        // so imported models with odd rotations still line up.
        meshRotationOffset = new Quaternion[4];

        for (int i = 0; i < 4; i++)
        {
            meshRotationOffset[i] = meshes[i] != null
                ? Quaternion.Inverse(wheels[i].transform.rotation) * meshes[i].rotation
                : Quaternion.identity;
        }
    }

    private void Update()
    {
        throttleInput = Input.GetAxis("Vertical");
        steerInput = Input.GetAxis("Horizontal");
        brakeHeld = Input.GetKey(KeyCode.Space);
        handbrakeHeld = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetKeyDown(KeyCode.R))
            ResetUpright();
    }

    private void FixedUpdate()
    {
        float forwardSpeed = Vector3.Dot(Velocity, transform.forward); // m/s
        float speedKmh = forwardSpeed * 3.6f;

        float throttle = throttleInput;
        float brake = 0f; // 0..1

        // Pressing the opposite direction while still rolling = brake first.
        bool oppositeInput = throttle * forwardSpeed < 0f && Mathf.Abs(forwardSpeed) > 1f;

        if (brakeHeld || oppositeInput)
        {
            brake = 1f;
            throttle = 0f;
        }
        else if (Mathf.Abs(throttle) < 0.05f)
        {
            brake = coastBraking; // light engine-braking so the car doesn't roll forever
        }

        // Speed limits.
        if (throttle > 0f && speedKmh >= maxSpeedKmh) throttle = 0f;
        if (throttle < 0f && speedKmh <= -maxReverseSpeedKmh) throttle = 0f;

        ApplyDrive(throttle);
        ApplyBrakes(brake);
        ApplySteering(speedKmh);

        if (useAntiRollBars)
        {
            ApplyAntiRoll(wheels[FL], wheels[FR]);
            ApplyAntiRoll(wheels[BL], wheels[BR]);
        }
    }

    private void ApplyDrive(float throttle)
    {
        bool front = driveType != DriveType.RearWheelDrive;
        bool rear = driveType != DriveType.FrontWheelDrive;
        int drivenWheels = (front ? 2 : 0) + (rear ? 2 : 0);

        float perWheel = throttle * totalMotorTorque / drivenWheels;

        wheels[FL].motorTorque = front ? perWheel : 0f;
        wheels[FR].motorTorque = front ? perWheel : 0f;
        wheels[BL].motorTorque = rear ? perWheel : 0f;
        wheels[BR].motorTorque = rear ? perWheel : 0f;
    }

    private void ApplyBrakes(float brake)
    {
        float front = brake * brakeTorque;
        float rear = brake * brakeTorque * rearBrakeShare + (handbrakeHeld ? handbrakeTorque : 0f);

        wheels[FL].brakeTorque = front;
        wheels[FR].brakeTorque = front;
        wheels[BL].brakeTorque = rear;
        wheels[BR].brakeTorque = rear;
    }

    private void ApplySteering(float speedKmh)
    {
        // Less steering lock at high speed = far fewer spin-outs and rollovers.
        float speedFactor = Mathf.Lerp(
            1f, highSpeedSteerFactor, Mathf.Clamp01(Mathf.Abs(speedKmh) / maxSpeedKmh));

        float target = steerInput * maxSteerAngle * speedFactor;

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle, target, steerSpeed * Time.fixedDeltaTime);

        wheels[FL].steerAngle = currentSteerAngle;
        wheels[FR].steerAngle = currentSteerAngle;
    }

    private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        float travelL = 1f;
        float travelR = 1f;

        bool groundedL = left.GetGroundHit(out WheelHit hit);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;

        bool groundedR = right.GetGroundHit(out hit);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;

        float force = (travelL - travelR) * antiRollStiffness;

        if (groundedL)
            rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (groundedR)
            rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }

    private void LateUpdate()
    {
        for (int i = 0; i < 4; i++)
        {
            if (meshes[i] == null) continue;

            wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
            meshes[i].SetPositionAndRotation(pos, rot * meshRotationOffset[i]);
        }
    }

    // Press R to flip the car back upright.
    private void ResetUpright()
    {
        rb.position = transform.position + Vector3.up;
        rb.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;
    }

    // Select the car during Play to see the center of mass (red dot).
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || rb == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.TransformPoint(rb.centerOfMass), 0.1f);
    }
}