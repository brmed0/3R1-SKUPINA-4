using System;
using UnityEngine;

/// <summary>
/// Arcade drift controller for a 4-wheel car using WheelColliders.
///
/// Core idea: everything is driven by ONE value, the slip angle (angle between the
/// car's nose and its direction of travel). The only thing changed at runtime is
/// the rear tires' sideways grip:
///
///   handbrake / throttle + steering / feint  -> loosen the rear     (START a drift)
///   counter-steer assist                     -> front wheels follow the slip angle (HOLD / CATCH)
///   max drift angle                          -> rear grip blends back in smoothly (no spin-outs)
///   speed retention                          -> small forward push while drifting on throttle
///
/// Setup: put this on the car root (needs a Rigidbody) and assign the 4 WheelColliders.
/// Input: old Input Manager (keyboard + gamepad through the default Vertical/Horizontal axes).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class WheelControll : MonoBehaviour
{
    // =====================================================================
    //  DEFAULT TUNING VALUES
    //  Every Inspector default lives in this block. Change a number here and
    //  use "Reset" on the component (or add the script to a new car) to pick
    //  it up. Components already in a scene keep the values serialized there.
    // =====================================================================
    private static class Defaults
    {
        // Car
        public const float CarMass = 1300f;
        public const float ComDrop = 0.25f;
        public const float ComForwardShift = 0f;

        // Engine
        public const float EnginePower = 2800f;
        public const float RearPowerShare = 0.6f;
        public const float TopSpeedKmh = 160f;
        public const float ReverseSpeedKmh = 30f;

        // Brakes
        public const float BrakeTorque = 2200f;
        public const float HandbrakePower = 3000f;

        // Steering
        public const float MaxSteer = 30f;
        public const float SteerSpeed = 160f;
        public const float HighSpeedSteer = 0.35f;
        public const float SteerReduceStartKmh = 30f;

        // Tires
        public const float GripFront = 1.8f;
        public const float GripRear = 1.6f;
        public const float GripForward = 1.5f;

        // Drift entry: handbrake + grip transitions
        public const float HandbrakeGrip = 0.45f;
        public const float GripLossRate = 10f;
        public const float GripRecovery = 1.2f;

        // Drift entry: power oversteer
        public const float PowerOversteer = 0.4f;
        public const float PowerOversteerMinKmh = 45f;
        public const float PowerOversteerMinSteer = 0.3f;

        // Drift entry: feint
        public const float FeintGripLoss = 0.4f;
        public const float FeintYawKick = 2.5f;
        public const float FeintDuration = 0.6f;
        public const float FeintSteerThreshold = 0.5f;
        public const float FeintWindow = 0.45f;
        public const float FeintMinKmh = 40f;

        // Drift detection
        public const float DriftEnterAngle = 12f;
        public const float DriftExitAngle = 8f;
        public const float DriftMinSpeedKmh = 25f;
        public const float DriftExitDelay = 0.2f;

        // Counter-steer
        public const float CounterSteerAssist = 0.5f;
        public const float CounterSteerDeadZone = 3f;
        public const float CounterSteerMinKmh = 15f;

        // Max drift angle
        public const float MaxDriftAngle = 55f;
        public const float AngleLimitStart = 0.6f;
        public const float MaxAngleRearGrip = 1.5f;
        public const float AngleLimitYawDamping = 3f;

        // Speed retention / holding
        public const float DriftSpeedBoost = 3f;
        public const float DriftBoostFullAngle = 30f;
        public const float DriftHoldGrip = 0.8f;

        // Stability
        public const float SuspensionTravel = 0.3f;
        public const float AntiRoll = 6000f;
        public const float Downforce = 30f;

        // Resistance
        public const float RollingResistance = 190f;
        public const float AirDrag = 0.5f;

        // Effects
        public const float SkidSlipThreshold = 0.4f;
        public const float SkidFullSlip = 1.2f;
        public const float SquealSmoothing = 12f;

        // Debug
        public const bool ShowDebug = true;
    }

    // =====================================================================
    //  INSPECTOR
    // =====================================================================

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

    [Header("Input (old Input Manager)")]
    [Tooltip("Handbrake key. WASD / arrows / left stick are already covered by the default Vertical and Horizontal axes.")]
    [SerializeField] private KeyCode handbrakeKey = KeyCode.Space;
    [Tooltip("Gamepad handbrake button (JoystickButton1 = B on an Xbox pad).")]
    [SerializeField] private KeyCode handbrakePadButton = KeyCode.JoystickButton1;
    [Tooltip("Puts the car back on its wheels.")]
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    [Header("Car")]
    [SerializeField, Range(500f, 3000f)] private float carMass = Defaults.CarMass;
    [Tooltip("How far below the axles the center of mass sits. Higher = harder to flip.")]
    [SerializeField, Range(0f, 0.8f)] private float comDrop = Defaults.ComDrop;
    [Tooltip("Moves the center of mass toward the front (+) or rear (-). Rear-heavy = easier to rotate.")]
    [SerializeField, Range(-0.6f, 0.6f)] private float comForwardShift = Defaults.ComForwardShift;

    [Header("Engine")]
    [Tooltip("Total drive torque shared between the wheels.")]
    [SerializeField, Range(500f, 8000f)] private float enginePower = Defaults.EnginePower;
    [Tooltip("Share of drive torque sent to the rear axle. 0.6 = rear-biased AWD, 1 = pure RWD.")]
    [SerializeField, Range(0f, 1f)] private float rearPowerShare = Defaults.RearPowerShare;
    [SerializeField, Range(40f, 300f)] private float topSpeedKmh = Defaults.TopSpeedKmh;
    [SerializeField, Range(5f, 80f)] private float reverseSpeedKmh = Defaults.ReverseSpeedKmh;

    [Header("Brakes")]
    [SerializeField, Range(0f, 6000f)] private float brakeTorque = Defaults.BrakeTorque;
    [Tooltip("Brake torque on the rear wheels while the handbrake is held.")]
    [SerializeField, Range(0f, 8000f)] private float handbrakePower = Defaults.HandbrakePower;

    [Header("Steering")]
    [Tooltip("Maximum front wheel angle in degrees.")]
    [SerializeField, Range(10f, 45f)] private float maxSteer = Defaults.MaxSteer;
    [Tooltip("How fast the wheels turn, in degrees per second.")]
    [SerializeField, Range(30f, 400f)] private float steerSpeed = Defaults.SteerSpeed;
    [Tooltip("Steering left at top speed, as a fraction of maxSteer. Lower = calmer at speed.")]
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteer = Defaults.HighSpeedSteer;
    [Tooltip("Speed where steering starts to reduce.")]
    [SerializeField, Range(0f, 100f)] private float steerReduceStartKmh = Defaults.SteerReduceStartKmh;

    [Header("Tires (1 = Unity default grip, higher = more grip)")]
    [SerializeField, Range(0.5f, 4f)] private float gripFront = Defaults.GripFront;
    [Tooltip("Base sideways grip of the rear tires. Everything drift-related scales this value.")]
    [SerializeField, Range(0.5f, 4f)] private float gripRear = Defaults.GripRear;
    [SerializeField, Range(0.5f, 4f)] private float gripForward = Defaults.GripForward;

    [Header("Drift Entry: Handbrake & Grip Transitions")]
    [Tooltip("Rear grip multiplier while the handbrake is held (lower = slides more).")]
    [SerializeField, Range(0.1f, 1f)] private float handbrakeGrip = Defaults.HandbrakeGrip;
    [Tooltip("How fast rear grip is lost (grip factor per second). High = the slide starts immediately.")]
    [SerializeField, Range(1f, 40f)] private float gripLossRate = Defaults.GripLossRate;
    [Tooltip("How fast rear grip comes back (grip factor per second). Low = softer, no snap-back.")]
    [SerializeField, Range(0.2f, 8f)] private float gripRecovery = Defaults.GripRecovery;

    [Header("Drift Entry: Power Oversteer")]
    [Tooltip("How much throttle + steering at speed loosens the rear. 0 = off.")]
    [SerializeField, Range(0f, 0.8f)] private float powerOversteer = Defaults.PowerOversteer;
    [Tooltip("Power oversteer only works above this speed.")]
    [SerializeField, Range(10f, 150f)] private float powerOversteerMinKmh = Defaults.PowerOversteerMinKmh;
    [Tooltip("Steering input needed before power oversteer kicks in (0..1).")]
    [SerializeField, Range(0f, 0.9f)] private float powerOversteerMinSteer = Defaults.PowerOversteerMinSteer;

    [Header("Drift Entry: Feint (weight shift)")]
    [Tooltip("Rear grip lost after a feint (steer one way, then quickly the other). 0 = no grip loss.")]
    [SerializeField, Range(0f, 0.8f)] private float feintGripLoss = Defaults.FeintGripLoss;
    [Tooltip("Extra yaw kick toward the new steering direction after a feint (rad/s^2). 0 = off.")]
    [SerializeField, Range(0f, 10f)] private float feintYawKick = Defaults.FeintYawKick;
    [Tooltip("How long the feint effect lasts (it fades out over this time).")]
    [SerializeField, Range(0.1f, 2f)] private float feintDuration = Defaults.FeintDuration;
    [Tooltip("Steering input that counts as 'steering hard' for feint detection (0..1).")]
    [SerializeField, Range(0.2f, 0.95f)] private float feintSteerThreshold = Defaults.FeintSteerThreshold;
    [Tooltip("The second steering direction must follow the first within this many seconds.")]
    [SerializeField, Range(0.1f, 1.5f)] private float feintWindow = Defaults.FeintWindow;
    [Tooltip("Feints only work above this speed.")]
    [SerializeField, Range(10f, 150f)] private float feintMinKmh = Defaults.FeintMinKmh;

    [Header("Drift Detection")]
    [Tooltip("Slip angle (degrees) needed to start a drift.")]
    [SerializeField, Range(3f, 45f)] private float driftEnterAngle = Defaults.DriftEnterAngle;
    [Tooltip("Slip angle below which the drift ends (lower than enter = no flicker).")]
    [SerializeField, Range(1f, 30f)] private float driftExitAngle = Defaults.DriftExitAngle;
    [SerializeField, Range(5f, 100f)] private float driftMinSpeedKmh = Defaults.DriftMinSpeedKmh;
    [Tooltip("Seconds the slip must stay low before the drift is declared over.")]
    [SerializeField, Range(0f, 1f)] private float driftExitDelay = Defaults.DriftExitDelay;

    [Header("Counter-Steer Assist")]
    [Tooltip("0 = off, 1 = strong. The front wheels are turned toward the direction of travel by (slip angle x this).")]
    [SerializeField, Range(0f, 1f)] private float counterSteerAssist = Defaults.CounterSteerAssist;
    [Tooltip("Slip angle (degrees) below which no counter-steer is applied.")]
    [SerializeField, Range(0f, 10f)] private float counterSteerDeadZone = Defaults.CounterSteerDeadZone;
    [Tooltip("Counter-steer fades in above this speed.")]
    [SerializeField, Range(0f, 60f)] private float counterSteerMinKmh = Defaults.CounterSteerMinKmh;

    [Header("Max Drift Angle (anti spin-out)")]
    [Tooltip("Slip angle at which the rear grip is fully restored.")]
    [SerializeField, Range(20f, 90f)] private float maxDriftAngle = Defaults.MaxDriftAngle;
    [Tooltip("Fraction of maxDriftAngle where grip starts coming back (0.6 = at 60%).")]
    [SerializeField, Range(0.2f, 0.95f)] private float angleLimitStart = Defaults.AngleLimitStart;
    [Tooltip("Rear grip multiplier at the limit (1 = normal grip, above 1 = extra bite to stop the spin).")]
    [SerializeField, Range(1f, 3f)] private float maxAngleRearGrip = Defaults.MaxAngleRearGrip;
    [Tooltip("Extra yaw damping near the limit. Stops the car from rotating further. 0 = off.")]
    [SerializeField, Range(0f, 10f)] private float angleLimitYawDamping = Defaults.AngleLimitYawDamping;

    [Header("Speed Retention & Holding")]
    [Tooltip("Forward acceleration (m/s^2) added while drifting on throttle. 0 = off.")]
    [SerializeField, Range(0f, 12f)] private float driftSpeedBoost = Defaults.DriftSpeedBoost;
    [Tooltip("Slip angle at which the speed boost reaches full strength.")]
    [SerializeField, Range(5f, 60f)] private float driftBoostFullAngle = Defaults.DriftBoostFullAngle;
    [Tooltip("Rear grip multiplier kept while drifting on throttle (lower = easier to hold). Lift off to catch.")]
    [SerializeField, Range(0.3f, 1f)] private float driftHoldGrip = Defaults.DriftHoldGrip;

    [Header("Stability")]
    [SerializeField, Range(0.1f, 0.6f)] private float suspensionTravel = Defaults.SuspensionTravel;
    [Tooltip("Anti-roll bar stiffness. Higher = flatter cornering, harder to flip.")]
    [SerializeField, Range(0f, 20000f)] private float antiRoll = Defaults.AntiRoll;
    [Tooltip("Downward force per m/s of speed.")]
    [SerializeField, Range(0f, 150f)] private float downforce = Defaults.Downforce;

    [Header("Resistance")]
    [SerializeField, Range(0f, 600f)] private float rollingResistance = Defaults.RollingResistance;
    [SerializeField, Range(0f, 2f)] private float airDrag = Defaults.AirDrag;

    [Header("Effects (smoke, skid marks, squeal)")]
    [Tooltip("Per-wheel slip where skidding starts (0 on the slip01 scale).")]
    [SerializeField, Range(0.1f, 2f)] private float skidSlipThreshold = Defaults.SkidSlipThreshold;
    [Tooltip("Per-wheel slip where skidding is at maximum (1 on the slip01 scale).")]
    [SerializeField, Range(0.3f, 4f)] private float skidFullSlip = Defaults.SkidFullSlip;
    [Tooltip("How quickly the tire squeal volume follows the slip. Higher = snappier.")]
    [SerializeField, Range(1f, 40f)] private float squealSmoothing = Defaults.SquealSmoothing;

    [Header("Debug")]
    [SerializeField] private bool showDebug = Defaults.ShowDebug;
    [SerializeField] private KeyCode debugToggleKey = KeyCode.F1;

    // =====================================================================
    //  PUBLIC API (read-only values + events for other scripts)
    // =====================================================================

    public const int FrontLeft = 0;
    public const int FrontRight = 1;
    public const int RearLeft = 2;
    public const int RearRight = 3;

    /// <summary>Per-wheel slip info for smoke / skid marks / sound.</summary>
    public struct WheelSlipData
    {
        public bool grounded;
        public float forwardSlip;
        public float sidewaysSlip;
        /// <summary>0 = gripping, 1 = fully sliding. Use this for smoke, skid marks and sound.</summary>
        public float slip01;
        public Vector3 point;
        public Vector3 normal;
    }

    /// <summary>Raised once when a drift begins.</summary>
    public event Action OnDriftStarted;
    /// <summary>Raised when a drift ends. The argument is the drift duration in seconds.</summary>
    public event Action<float> OnDriftEnded;
    /// <summary>Raised every physics step for each wheel (index: FrontLeft, FrontRight, RearLeft, RearRight).</summary>
    public event Action<int, WheelSlipData> OnWheelSlip;

    /// <summary>Speed in km/h (always positive).</summary>
    public float SpeedKmh => speedMS * 3.6f;
    /// <summary>Speed along the car's nose in km/h (negative = reversing).</summary>
    public float ForwardSpeedKmh => forwardSpeedMS * 3.6f;
    /// <summary>Signed slip angle in degrees. Positive = the car travels to the right of where its nose points.</summary>
    public float SlipAngle => slipAngle;
    public float AbsSlipAngle => Mathf.Abs(slipAngle);
    public bool IsDrifting => isDrifting;
    /// <summary>Seconds the current drift has lasted (0 when not drifting).</summary>
    public float DriftDuration => isDrifting ? driftTimer : 0f;
    public float LastDriftDuration => lastDriftDuration;
    public float ThrottleInput => throttleInput;     // -1 (reverse) .. 1 (forward)
    public float SteerInput => steerInput;           // -1 (left) .. 1 (right)
    public bool HandbrakeHeld => handbrakeHeld;
    /// <summary>Final rear grip multiplier (1 = normal, below 1 = loose).</summary>
    public float RearGripFactor => rearGripFactor;
    /// <summary>0..1, smoothed. Feed it straight into an AudioSource volume.</summary>
    public float TireSquealVolume => squealVolume;
    public WheelSlipData GetWheelSlip(int index) => slipData[index];

    // =====================================================================
    //  PRIVATE STATE
    // =====================================================================

    private const float RearBrakeShare = 0.6f;
    private const float CoastBrake = 100f;

    private const float SlipMinSpeed = 2f;          // m/s: below this the slip angle is meaningless
    private const float MinRearGripFactor = 0.2f;   // floor so stacked effects can never remove all grip

    private Rigidbody rb;
    private WheelCollider[] wheels;                 // FL, FR, BL, BR
    private Transform[] meshes;
    private Quaternion[] meshOffset;
    private Vector3[] meshCenterOffset;

    private readonly WheelSlipData[] slipData = new WheelSlipData[4];

    // input
    private float throttleInput, steerInput;
    private bool handbrakeHeld;

    private float appliedThrottle;                  // throttle after brake-first logic

    // motion
    private float speedMS, forwardSpeedMS, slipAngle;
    private float steerAngle;

    // grip
    private float intentGrip = 1f;                  // smoothed "how loose does the driver want the rear"
    private float rearGripFactor = 1f;              // intent blended with the max-angle limit
    private float limit01;                          // 0 = far from max angle, 1 = at the limit

    // feint
    private float feintTimer;
    private int feintDirection;
    private int lastStrongSteerSign;
    private float lastStrongSteerTime = -10f;

    // drift state
    private bool isDrifting;
    private float driftTimer, driftExitTimer, lastDriftDuration;

    private float squealVolume;
    private GUIStyle debugLabel;

    private Vector3 Velocity =>
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity;
#else
        rb.velocity;
#endif

    // =====================================================================
    //  SETUP
    // =====================================================================

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

        rb.interpolation = RigidbodyInterpolation.Interpolate;

        wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);

        ApplyCarSetup();
        CacheMeshOffsets();
    }

    // Live tuning: changing mass / COM / suspension / grip in the Inspector during Play takes effect.
    private void OnValidate()
    {
        driftExitAngle = Mathf.Min(driftExitAngle, driftEnterAngle);
        maxDriftAngle = Mathf.Max(maxDriftAngle, driftEnterAngle + 5f);
        skidFullSlip = Mathf.Max(skidFullSlip, skidSlipThreshold + 0.1f);

        if (Application.isPlaying && enabled && rb != null && wheels != null)
            ApplyCarSetup();
    }

    private bool WheelsAreValid()
    {
        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null)
            {
                Debug.LogError("[WheelControll] A WheelCollider slot is empty. Assign all 4 WheelColliders.", this);
                return false;
            }
            for (int j = i + 1; j < 4; j++)
            {
                if (wheels[i] == wheels[j])
                {
                    Debug.LogError("[WheelControll] The same WheelCollider is assigned to more than one slot.", this);
                    return false;
                }
            }
        }

        if (GetComponent<Rigidbody>() == null)
        {
            Debug.LogError("[WheelControll] No Rigidbody found on the car root.", this);
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            if (meshes[i] == null)
                Debug.LogWarning("[WheelControll] Wheel mesh " + i + " is not assigned. That wheel will not be animated (optional).", this);
        }
        return true;
    }

    // Mass, center of mass, suspension and tires. Safe to call again at runtime.
    private void ApplyCarSetup()
    {
        rb.mass = carMass;

        // Center of mass: middle of the wheels, dropped a bit lower (and optionally shifted).
        Vector3 center = Vector3.zero;
        foreach (WheelCollider w in wheels) center += w.transform.position;
        Vector3 com = transform.InverseTransformPoint(center / 4f);
        com.y -= comDrop;
        com.z += comForwardShift;
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

        SetupTires();
    }

    // Sets the tire grip curves so the car doesn't depend on whatever values are on the WheelColliders.
    private void SetupTires()
    {
        for (int i = 0; i < 4; i++)
        {
            wheels[i].forwardFriction = MakeCurve(0.4f, 1f, 0.8f, 0.5f, gripForward);
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

    // =====================================================================
    //  INPUT (Update)
    // =====================================================================

    private void Update()
    {
        throttleInput = Mathf.Clamp(Input.GetAxis("Vertical"), -1f, 1f);
        steerInput = Mathf.Clamp(Input.GetAxis("Horizontal"), -1f, 1f);
        handbrakeHeld = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakePadButton);

        if (Input.GetKeyDown(resetKey)) ResetUpright();
        if (Input.GetKeyDown(debugToggleKey)) showDebug = !showDebug;

        DetectFeint();
    }

    // Feint = steer hard one way, then hard the other way within a short window, at speed.
    // Detected here (input), applied in FixedUpdate (physics).
    private void DetectFeint()
    {
        if (feintGripLoss <= 0f && feintYawKick <= 0f) return;
        if (Mathf.Abs(steerInput) < feintSteerThreshold) return;

        int sign = steerInput > 0f ? 1 : -1;

        bool flipped = lastStrongSteerSign != 0 && sign != lastStrongSteerSign;
        bool quickEnough = Time.time - lastStrongSteerTime <= feintWindow;

        if (flipped && quickEnough && SpeedKmh >= feintMinKmh && feintTimer <= 0f)
        {
            feintTimer = feintDuration;
            feintDirection = sign;
        }

        lastStrongSteerSign = sign;
        lastStrongSteerTime = Time.time;
    }

    // =====================================================================
    //  PHYSICS (FixedUpdate)
    // =====================================================================

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        UpdateMotion();                 // speed + slip angle (the core value)
        UpdateDriftState(dt);           // IsDrifting, duration, events
        UpdateWheelSlip(dt);            // per-wheel slip, events, squeal
        ApplyThrottleAndBrakes();
        ApplySteering(dt);              // speed-sensitive steering + counter-steer
        UpdateRearGrip(dt);             // handbrake / power / feint / hold / angle limit
        ApplyDriftForces();             // speed retention, spin guard, feint kick
        ApplyResistanceAndStability();
    }

    // --- slip angle -------------------------------------------------------
    private void UpdateMotion()
    {
        Vector3 vel = Velocity;
        Vector3 up = transform.up;
        Vector3 flat = Vector3.ProjectOnPlane(vel, up);

        speedMS = flat.magnitude;
        forwardSpeedMS = Vector3.Dot(vel, transform.forward);

        float raw = speedMS > 0.01f ? Vector3.SignedAngle(transform.forward, flat, up) : 0f;

        // Driving backwards is not a slide: fade the angle out as the velocity points behind the car.
        float reverseBlend = forwardSpeedMS < -1f ? Mathf.InverseLerp(135f, 165f, Mathf.Abs(raw)) : 0f;
        float lowSpeedFade = Mathf.InverseLerp(SlipMinSpeed, SlipMinSpeed * 2f, speedMS);

        slipAngle = Mathf.Lerp(raw, 0f, reverseBlend) * lowSpeedFade;
    }

    // --- drift state ------------------------------------------------------
    private void UpdateDriftState(float dt)
    {
        float kmh = SpeedKmh;
        float absSlip = Mathf.Abs(slipAngle);

        bool canEnter = absSlip >= driftEnterAngle && kmh >= driftMinSpeedKmh;
        bool canStay = absSlip >= driftExitAngle && kmh >= driftMinSpeedKmh * 0.6f;

        if (!isDrifting)
        {
            if (canEnter)
            {
                isDrifting = true;
                driftTimer = 0f;
                driftExitTimer = 0f;
                OnDriftStarted?.Invoke();
            }
            return;
        }

        if (canStay)
        {
            driftExitTimer = 0f;
            driftTimer += dt;
        }
        else
        {
            driftExitTimer += dt;
            if (driftExitTimer >= driftExitDelay) EndDrift();
        }
    }

    private void EndDrift()
    {
        if (!isDrifting) return;
        isDrifting = false;
        lastDriftDuration = driftTimer;
        driftTimer = 0f;
        driftExitTimer = 0f;
        OnDriftEnded?.Invoke(lastDriftDuration);
    }

    // --- per-wheel slip for effects ----------------------------------------
    private void UpdateWheelSlip(float dt)
    {
        float maxSlip = 0f;

        for (int i = 0; i < 4; i++)
        {
            WheelSlipData d = default;

            if (wheels[i].GetGroundHit(out WheelHit hit))
            {
                d.grounded = true;
                d.forwardSlip = hit.forwardSlip;
                d.sidewaysSlip = hit.sidewaysSlip;

                float magnitude = Mathf.Sqrt(hit.forwardSlip * hit.forwardSlip + hit.sidewaysSlip * hit.sidewaysSlip);
                d.slip01 = Mathf.InverseLerp(skidSlipThreshold, skidFullSlip, magnitude);
                d.point = hit.point;
                d.normal = hit.normal;

                maxSlip = Mathf.Max(maxSlip, d.slip01);
            }

            slipData[i] = d;
            OnWheelSlip?.Invoke(i, d);
        }

        // Squeal follows the worst wheel; silent when (almost) parked.
        float movingGate = Mathf.InverseLerp(0.5f, 3f, speedMS);
        float smoothing = 1f - Mathf.Exp(-squealSmoothing * dt);
        squealVolume = Mathf.Lerp(squealVolume, maxSlip * movingGate, smoothing);
    }

    // --- throttle & brakes --------------------------------------------------
    private void ApplyThrottleAndBrakes()
    {
        float throttle = throttleInput;
        float brake = 0f;

        bool oppositeInput = throttle * forwardSpeedMS < 0f && Mathf.Abs(forwardSpeedMS) > 1f;
        if (oppositeInput)
        {
            brake = brakeTorque;     // pressing S while rolling forward = brake first
            throttle = 0f;
        }
        else if (Mathf.Abs(throttle) < 0.05f)
        {
            brake = CoastBrake;      // light engine braking when you let off
        }

        appliedThrottle = throttle;

        // Engine torque fades out toward top speed instead of hitting a wall.
        float limit = throttle >= 0f ? topSpeedKmh : reverseSpeedKmh;
        float ratio = Mathf.Clamp01(SpeedKmh / limit);
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
    }

    // --- steering -------------------------------------------------------------
    private void ApplySteering(float dt)
    {
        // Less steering at speed.
        float t = Mathf.InverseLerp(steerReduceStartKmh, Mathf.Max(topSpeedKmh, steerReduceStartKmh + 1f), SpeedKmh);
        float speedFactor = Mathf.Lerp(1f, highSpeedSteer, t);

        float target = steerInput * maxSteer * speedFactor + CounterSteer();
        target = Mathf.Clamp(target, -maxSteer, maxSteer);

        steerAngle = Mathf.MoveTowards(steerAngle, target, steerSpeed * dt);
        wheels[0].steerAngle = steerAngle;
        wheels[1].steerAngle = steerAngle;
    }

    // Turns the front wheels toward the direction of travel.
    // The angle that points the wheels exactly along the velocity IS the slip angle,
    // so the assist is simply (slip angle - dead zone) x assist, fading in with speed.
    // The player's own steering is added on top, so steering into the slide still widens the angle.
    private float CounterSteer()
    {
        if (counterSteerAssist <= 0f) return 0f;

        float speedGate = Mathf.InverseLerp(counterSteerMinKmh, counterSteerMinKmh * 2f + 1f, SpeedKmh);
        if (speedGate <= 0f) return 0f;

        float beyondDeadZone = Mathf.Max(0f, Mathf.Abs(slipAngle) - counterSteerDeadZone);
        if (beyondDeadZone <= 0f) return 0f;

        return Mathf.Sign(slipAngle) * beyondDeadZone * counterSteerAssist * speedGate;
    }

    // --- rear grip: the heart of the drift model --------------------------------
    private void UpdateRearGrip(float dt)
    {
        float throttle = Mathf.Max(0f, appliedThrottle);
        float kmh = SpeedKmh;

        // 1) What does the driver want? (1 = full grip, lower = looser rear)
        float intent = 1f;

        // Handbrake
        if (handbrakeHeld) intent *= handbrakeGrip;

        // Power oversteer: hard throttle + steering at speed
        float powerGate =
            Mathf.InverseLerp(0.5f, 1f, throttle) *
            Mathf.InverseLerp(powerOversteerMinKmh, powerOversteerMinKmh * 1.5f, kmh) *
            Mathf.InverseLerp(powerOversteerMinSteer, powerOversteerMinSteer + 0.4f, Mathf.Abs(steerInput));
        intent *= 1f - powerOversteer * powerGate;

        // Feint: grip loss that fades out over feintDuration
        if (feintTimer > 0f)
        {
            float k = Mathf.Clamp01(feintTimer / feintDuration);
            intent *= 1f - feintGripLoss * Mathf.SmoothStep(0f, 1f, k);
            feintTimer -= dt;
        }

        // Hold: while already drifting on throttle, keep the rear a little loose. Lifting off = catch.
        if (isDrifting && throttle > 0.3f) intent *= driftHoldGrip;

        intent = Mathf.Max(intent, MinRearGripFactor);

        // 2) Smooth transitions: lose grip fast, regain it slowly (no snap-back).
        float rate = intent < intentGrip ? gripLossRate : gripRecovery;
        intentGrip = Mathf.MoveTowards(intentGrip, intent, rate * dt);

        // 3) Max drift angle: blend grip back in as the slip angle nears the limit (smoothstep).
        float startAngle = maxDriftAngle * angleLimitStart;
        limit01 = Mathf.InverseLerp(startAngle, maxDriftAngle, Mathf.Abs(slipAngle));
        limit01 = limit01 * limit01 * (3f - 2f * limit01);

        rearGripFactor = Mathf.Lerp(intentGrip, maxAngleRearGrip, limit01);

        float rearStiffness = gripRear * rearGripFactor;
        SetSidewaysGrip(wheels[2], rearStiffness);
        SetSidewaysGrip(wheels[3], rearStiffness);
    }

    private void SetSidewaysGrip(WheelCollider wheel, float stiffness)
    {
        WheelFrictionCurve f = wheel.sidewaysFriction;
        f.stiffness = stiffness;
        wheel.sidewaysFriction = f;
    }

    // --- extra forces ---------------------------------------------------------------
    private void ApplyDriftForces()
    {
        float throttle = Mathf.Max(0f, appliedThrottle);

        // Speed retention: a small push (half along the nose, half along the velocity) while drifting on throttle.
        // It scales with slip angle, fades near top speed and switches off as the car nears the max angle.
        if (isDrifting && throttle > 0.1f && driftSpeedBoost > 0f)
        {
            float slipScale = Mathf.Clamp01(Mathf.Abs(slipAngle) / driftBoostFullAngle);
            float speedRatio = Mathf.Clamp01(SpeedKmh / topSpeedKmh);
            float boost = driftSpeedBoost * throttle * slipScale * (1f - speedRatio * speedRatio) * (1f - limit01);

            Vector3 velDir = Vector3.ProjectOnPlane(Velocity, transform.up).normalized;
            Vector3 dir = transform.forward + velDir;
            if (dir.sqrMagnitude > 0.0001f)
                rb.AddForce(dir.normalized * boost, ForceMode.Acceleration);
        }

        // Spin guard: damp the yaw rate near the max angle so the car can't keep rotating.
        if (limit01 > 0f && angleLimitYawDamping > 0f)
        {
            float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);
            rb.AddTorque(-transform.up * (yawRate * angleLimitYawDamping * limit01), ForceMode.Acceleration);
        }

        // Feint kick: a short yaw push toward the new steering direction (the "weight shift" swing).
        if (feintTimer > 0f && feintYawKick > 0f)
        {
            float k = Mathf.Clamp01(feintTimer / feintDuration);
            rb.AddTorque(transform.up * (feintDirection * feintYawKick * k), ForceMode.Acceleration);
        }
    }

    // --- resistance & stability ---------------------------------------------------------
    private void ApplyResistanceAndStability()
    {
        Vector3 vel = Velocity;
        float spd = vel.magnitude;

        if (spd > 0.1f)
            rb.AddForce(-vel / spd * (rollingResistance + airDrag * spd * spd));

        rb.AddForce(-transform.up * downforce * spd);

        ApplyAntiRoll(wheels[0], wheels[1]);
        ApplyAntiRoll(wheels[2], wheels[3]);
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

    // =====================================================================
    //  VISUALS
    // =====================================================================

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
        rb.position = rb.position + Vector3.up;
        rb.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;

        steerAngle = 0f;
        intentGrip = 1f;
        rearGripFactor = 1f;
        feintTimer = 0f;
        EndDrift();
    }

    // =====================================================================
    //  DEBUG DISPLAY (toggle with debugToggleKey, default F1)
    // =====================================================================

    private void OnGUI()
    {
        if (!showDebug) return;

        if (debugLabel == null)
        {
            debugLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            debugLabel.normal.textColor = Color.white;
        }

        // Scale with screen height so it stays readable on 1440p / 4K.
        float scale = Mathf.Max(0.75f, Screen.height / 1080f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float w = 340f;
        const float h = 200f;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(10f, 10f, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        string state = isDrifting
            ? "<color=#5CFF7A><b>DRIFTING</b></color>"
            : "<color=#BBBBBB>grip</color>";

        string text =
            "State: " + state + "\n" +
            "Speed: " + SpeedKmh.ToString("0") + " km/h\n" +
            "Slip angle: " + slipAngle.ToString("0.0") + " deg\n" +
            "Drift time: " + DriftDuration.ToString("0.0") + " s  (last " + lastDriftDuration.ToString("0.0") + " s)\n" +
            "Rear grip: " + rearGripFactor.ToString("0.00") + "   Angle limit: " + limit01.ToString("0.00") + "\n" +
            "Throttle: " + throttleInput.ToString("0.00") + "   Handbrake: " + (handbrakeHeld ? "ON" : "off") + "\n" +
            "Wheel slip FL/FR/RL/RR: " +
            slipData[0].slip01.ToString("0.0") + " " + slipData[1].slip01.ToString("0.0") + " " +
            slipData[2].slip01.ToString("0.0") + " " + slipData[3].slip01.ToString("0.0");

        GUI.Label(new Rect(20f, 16f, w - 20f, h - 40f), text, debugLabel);

        // Slip angle bar: marker moves left/right, full width = +/- maxDriftAngle.
        Rect bar = new Rect(20f, h - 8f, w - 20f, 8f);
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        float centerX = bar.x + bar.width * 0.5f;
        float markerX = centerX + Mathf.Clamp(slipAngle / maxDriftAngle, -1f, 1f) * bar.width * 0.5f;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(centerX - 1f, bar.y - 2f, 2f, bar.height + 4f), Texture2D.whiteTexture);
        GUI.color = isDrifting ? new Color(0.36f, 1f, 0.48f) : Color.white;
        GUI.DrawTexture(new Rect(markerX - 3f, bar.y - 3f, 6f, bar.height + 6f), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.matrix = previousMatrix;
    }
}