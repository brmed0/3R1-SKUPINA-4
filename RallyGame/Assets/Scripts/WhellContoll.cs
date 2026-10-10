using UnityEngine;

// Rally car controller with realistic-feeling grip and drifting.
// Put it on the car root (needs a Rigidbody) and assign the 4 WheelColliders.
[RequireComponent(typeof(Rigidbody))]
public class WheelControll : MonoBehaviour
{
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

    [Header("Car")]
    [SerializeField] private float carMass = 1300f;
    [Tooltip("How far below the axles the center of mass sits. Higher = harder to flip.")]
    [SerializeField] private float comDrop = 0.2f;

    [Header("Engine")]
    [SerializeField] private float enginePower = 2800f;
    [SerializeField, Range(0f, 1f)] private float rearPowerShare = 0.6f;
    [SerializeField] private float topSpeedKmh = 160f;
    [SerializeField] private float reverseSpeedKmh = 30f;

    [Header("Brakes")]
    [SerializeField] private float brakeTorque = 2200f;
    [SerializeField] private float handbrakePower = 3000f;

    [Header("Steering")]
    [SerializeField] private float maxSteer = 30f;
    [SerializeField] private float steerSpeed = 140f;
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteer = 0.35f;

    [Header("Tires (1 = Unity default grip, higher = more grip)")]
    [SerializeField] private float gripFront = 1.8f;
    [SerializeField] private float gripRear = 1.6f;
    [SerializeField] private float gripForward = 1.5f;

    [Header("Drifting")]
    [Tooltip("Rear grip multiplier while the handbrake is held (lower = slides more).")]
    [SerializeField, Range(0.2f, 1f)] private float handbrakeGrip = 0.45f;
    [Tooltip("How fast rear grip comes back after releasing the handbrake.")]
    [SerializeField] private float gripRecovery = 2.5f;
    [Tooltip("How much full throttle loosens the rear (power slide). 0 = none.")]
    [SerializeField, Range(0f, 0.5f)] private float powerOversteer = 0.15f;
    [Tooltip("Auto counter-steer when sliding. 0 = off, 1 = very strong.")]
    [SerializeField, Range(0f, 1f)] private float counterSteerAssist = 0.5f;

    [Header("Stability")]
    [SerializeField] private float suspensionTravel = 0.3f;
    [SerializeField] private float antiRoll = 6000f;
    [SerializeField] private float downforce = 30f;

    [Header("Resistance")]
    [SerializeField] private float rollingResistance = 190f;
    [SerializeField] private float airDrag = 0.5f;

    private const float RearBrakeShare = 0.6f;
    private const float CoastBrake = 100f;

    private Rigidbody rb;
    private WheelCollider[] wheels;   // FL, FR, BL, BR
    private Transform[] meshes;
    private Quaternion[] meshOffset;
    private Vector3[] meshCenterOffset;

    private float throttleInput, steerInput, steerAngle, rearGripFactor = 1f;
    private bool handbrakeHeld;

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

        if (!WheelsAreValid())
        {
            enabled = false;
            return;
        }

        rb.mass = carMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Center of mass: middle of the wheels, dropped a bit lower.
        Vector3 center = Vector3.zero;
        foreach (WheelCollider w in wheels) center += w.transform.position;
        Vector3 com = transform.InverseTransformPoint(center / 4f);
        com.y -= comDrop;
        rb.centerOfMass = com;

        // Suspension tuned to the car's weight.
        float sprungMass = carMass / 4f;
        float spring = sprungMass * Physics.gravity.magnitude / (suspensionTravel * 0.25f);
        float damper = 1.2f * Mathf.Sqrt(spring * sprungMass);

        foreach (WheelCollider w in wheels)
        {
            w.suspensionDistance = suspensionTravel;
            w.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = 0.5f };
        }

        wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);

        SetupTires();
        CacheMeshOffsets();
    }

    private bool WheelsAreValid()
    {
        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null)
            {
                Debug.LogError("[WheelControll] A WheelCollider slot is empty.", this);
                return false;
            }
            for (int j = i + 1; j < 4; j++)
            {
                if (wheels[i] == wheels[j])
                {
                    Debug.LogError("[WheelControll] Two slots use the same WheelCollider. Check Front/Back Left and Right.", this);
                    return false;
                }
            }
        }
        return true;
    }

    // Sets the tire grip curves so the car doesn't depend on Inspector values.
    private void SetupTires()
    {
        for (int i = 0; i < 4; i++)
        {
            wheels[i].forwardFriction = MakeCurve(0.4f, 1f, 0.8f, 0.85f, gripForward);
            wheels[i].sidewaysFriction = MakeCurve(0.25f, 1f, 0.6f, 0.8f, i < 2 ? gripFront : gripRear);
        }
    }

    private static WheelFrictionCurve MakeCurve(float extSlip, float extValue, float asySlip, float asyValue, float stiffness)
    {
        return new WheelFrictionCurve
        {
            extremumSlip = extSlip,
            extremumValue = extValue,
            asymptoteSlip = asySlip,
            asymptoteValue = asyValue,
            stiffness = stiffness
        };
    }

    // Remembers each mesh's rotation and visual center, so the wheel
    // lines up with its collider even if the model's pivot is off to the side.
    private void CacheMeshOffsets()
    {
        meshOffset = new Quaternion[4];
        meshCenterOffset = new Vector3[4];

        for (int i = 0; i < 4; i++)
        {
            meshOffset[i] = Quaternion.identity;
            if (meshes[i] == null) continue;

            meshOffset[i] = Quaternion.Inverse(wheels[i].transform.rotation) * meshes[i].rotation;

            Renderer[] renderers = meshes[i].GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            meshCenterOffset[i] = Quaternion.Inverse(meshes[i].rotation) * (bounds.center - meshes[i].position);
        }
    }

    private void Update()
    {
        throttleInput = Input.GetAxis("Vertical");
        steerInput = Input.GetAxis("Horizontal");
        handbrakeHeld = Input.GetKey(KeyCode.Space);

        if (Input.GetKeyDown(KeyCode.R))
            ResetUpright();
    }

    private void FixedUpdate()
    {
        Vector3 vel = Velocity;
        float speed = Vector3.Dot(vel, transform.forward); // m/s, negative = reversing
        float kmh = speed * 3.6f;
        float dt = Time.fixedDeltaTime;

        // --- throttle & braking ---
        float throttle = throttleInput;
        float brake = 0f;

        bool oppositeInput = throttle * speed < 0f && Mathf.Abs(speed) > 1f;
        if (oppositeInput)
        {
            brake = brakeTorque;     // pressing S while rolling forward = brake first
            throttle = 0f;
        }
        else if (Mathf.Abs(throttle) < 0.05f)
        {
            brake = CoastBrake;      // light engine braking when you let off
        }

        // Engine torque fades out toward top speed instead of hitting a wall.
        float limit = throttle >= 0f ? topSpeedKmh : reverseSpeedKmh;
        float ratio = Mathf.Clamp01(Mathf.Abs(kmh) / limit);
        float engine = throttle * enginePower * (1f - ratio * ratio);

        float frontTorque = engine * (1f - rearPowerShare) * 0.5f;
        float rearTorque = engine * rearPowerShare * 0.5f;
        float handbrake = handbrakeHeld ? handbrakePower : 0f;

        wheels[0].motorTorque = frontTorque;
        wheels[1].motorTorque = frontTorque;
        wheels[2].motorTorque = rearTorque;
        wheels[3].motorTorque = rearTorque;

        wheels[0].brakeTorque = brake;
        wheels[1].brakeTorque = brake;
        wheels[2].brakeTorque = brake * RearBrakeShare + handbrake;
        wheels[3].brakeTorque = brake * RearBrakeShare + handbrake;

        // --- steering (gentler at speed, with counter-steer help in slides) ---
        float speedFactor = Mathf.Lerp(1f, highSpeedSteer, Mathf.Clamp01(Mathf.Abs(kmh) / topSpeedKmh));
        float target = steerInput * maxSteer * speedFactor + CounterSteer(vel, speed);
        target = Mathf.Clamp(target, -maxSteer, maxSteer);
        steerAngle = Mathf.MoveTowards(steerAngle, target, steerSpeed * dt);
        wheels[0].steerAngle = steerAngle;
        wheels[1].steerAngle = steerAngle;

        // --- rear grip: handbrake and throttle loosen the rear so it can slide ---
        float rearTarget = handbrakeHeld ? handbrakeGrip : 1f;
        float gripSpeed = rearTarget < rearGripFactor ? 10f : gripRecovery;
        rearGripFactor = Mathf.MoveTowards(rearGripFactor, rearTarget, gripSpeed * dt);

        float powerSlide = 1f - powerOversteer * Mathf.Max(0f, throttle);
        float rearStiffness = gripRear * rearGripFactor * powerSlide;
        SetSidewaysGrip(wheels[2], rearStiffness);
        SetSidewaysGrip(wheels[3], rearStiffness);

        // --- resistance & stability ---
        float spd = vel.magnitude;
        if (spd > 0.1f)
            rb.AddForce(-vel / spd * (rollingResistance + airDrag * spd * spd));

        rb.AddForce(-transform.up * downforce * spd);
        ApplyAntiRoll(wheels[0], wheels[1]);
        ApplyAntiRoll(wheels[2], wheels[3]);
    }

    // When the car slides, point the front wheels toward the direction of travel.
    private float CounterSteer(Vector3 vel, float forwardSpeed)
    {
        if (counterSteerAssist <= 0f || forwardSpeed < 4f) return 0f;

        Vector3 flat = Vector3.ProjectOnPlane(vel, transform.up);
        if (flat.sqrMagnitude < 16f) return 0f;

        float slip = Vector3.SignedAngle(transform.forward, flat, transform.up);
        float beyondDeadZone = Mathf.Max(0f, Mathf.Abs(slip) - 4f) * Mathf.Sign(slip);
        return beyondDeadZone * counterSteerAssist;
    }

    private void SetSidewaysGrip(WheelCollider wheel, float stiffness)
    {
        WheelFrictionCurve f = wheel.sidewaysFriction;
        f.stiffness = stiffness;
        wheel.sidewaysFriction = f;
    }

    private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        float travelL = 1f, travelR = 1f;

        bool groundedL = left.GetGroundHit(out WheelHit hit);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;

        bool groundedR = right.GetGroundHit(out hit);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;

        float force = (travelL - travelR) * antiRoll;

        if (groundedL) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (groundedR) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }

    private void LateUpdate()
    {
        for (int i = 0; i < 4; i++)
        {
            if (meshes[i] == null) continue;

            wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
            Quaternion finalRot = rot * meshOffset[i];
            meshes[i].SetPositionAndRotation(pos - finalRot * meshCenterOffset[i], finalRot);
        }
    }

    // Press R to put the car back on its wheels.
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
}