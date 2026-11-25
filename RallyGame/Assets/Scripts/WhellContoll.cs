using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// WheelCollider vehicle controller tuned for the European BMW E36 M3 3.2 coupe.
/// The assisted controls keep W/S for drive, brake and reverse. E/Q can select gears.
/// </summary>
[MovedFrom(true, sourceAssembly: "Assembly-CSharp", sourceClassName: "WheelControll")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class WhellContoll : MonoBehaviour
{
    // =====================================================================
    //  STOCK E36 M3 3.2 TUNE
    // =====================================================================
    private static class Defaults
    {
        public const float CarMass = 1460f;
        public const float FrontWeightShare = 0.52f;
        public const float CenterOfMassHeight = 0.50f;
        public const float Wheelbase = 2.70f;
        public const float FrontTrack = 1.430f;
        public const float RearTrack = 1.451f;
        public const float FrontTireRadius = (17f * 0.0254f + 2f * 0.225f * 0.45f) * 0.5f;
        public const float RearTireRadius = (17f * 0.0254f + 2f * 0.245f * 0.40f) * 0.5f;

        public const float DrivetrainEfficiency = 0.90f;
        public const float MaxSpeedKmh = 250f;
        public const float ReverseSpeedKmh = 40f;

        // Differential (mild LSD, ~25-30% lock)
        public const float LsdLock = 0.25f;              // 0 = open, 1 = locked
        public const float LsdPreload = 35f;             // Nm
        public const float LsdRamp = 2f;                 // Nm of torque shifted per rpm of wheel speed difference

        // Brakes
        public const float BrakeTorque = 1600f;          // per FRONT wheel
        public const float RearBrakeShare = 0.55f;       // rear torque relative to front (0.55 = ~65% front bias)
        public const float HandbrakePower = 2000f;       // per REAR wheel, still locks them

        // Pedal smoothing (units per second)
        public const float ThrottleRise = 6f;               // gentler pedal, no instant stab
        public const float ThrottleFall = 8f;
        public const float BrakeRise = 6f;
        public const float BrakeFall = 12f;
        public const float HandbrakeRise = 8f;
        public const float HandbrakeFall = 7f;

        // Steering
        public const float MaxSteer = 32f;               // road wheel degrees
        public const float LockToLockTurns = 3f;         // steering wheel turns lock to lock
        public const float SteerSpeed = 1200f;           // now in STEERING WHEEL deg/s
        public const float HighSpeedSteer = 0.2f;           // much calmer steering at speed
        public const float SteerReduceStartKmh = 20f;
        public const float TopSpeedKmh = 200f;           // reference speed for the steering reduction only
        public const float SteerRiseRate = 2.0f;         // input ramp toward the key / stick (units per second)
        public const float SteerReturnRate = 4f;         // input ramp back to center (grows with speed)

        // Tires
        public const float GripFront = 1.22f;                // wider sport tires
        public const float GripRear = 1.42f;                // rear > front = stable, understeers first at the limit
        public const float GripForward = 1.18f;
        public const float SidewaysPeakSlip = 0.16f;     // slip where side grip peaks
        public const float SidewaysSlideSlip = 0.55f;    // slip where the tire is fully sliding
        public const float SidewaysSlideGrip = 0.91f;    // grip kept when fully sliding (fraction of peak)
        public const float LoadSensitivity = 0.15f;      // more load = less grip per load (weight transfer costs grip)
        public const float CombinedSlipReference = 0.65f; // forward slip where side grip is cut the most
        public const float CombinedSlipGripLoss = 0.42f;  // side grip lost on a locked / fully spinning wheel
        public const float GripResponse = 25f;           // how fast grip follows load / slip (per second)

        // Traction control (optional game assist)
        public const float TractionControl = 0.48f;       // 0 = off, 1 = cuts throttle completely when the wheels spin
        public const float TcSlipStart = 0.16f;          // rear wheel forward slip where TC starts to cut
        public const float TcSlipFull = 0.43f;           // rear wheel forward slip where TC cuts the most

        // Drift detection (only for IsDrifting + events, it does not change the physics)
        public const float DriftEnterAngle = 12f;
        public const float DriftExitAngle = 8f;
        public const float DriftMinSpeedKmh = 25f;
        public const float DriftExitDelay = 0.2f;

        // Counter-steer assist (0 = off)
        public const float CounterSteerAssist = 0.23f;
        public const float CounterSteerDeadZone = 3f;
        public const float CounterSteerMinKmh = 15f;

        // Stability
        public const float SuspensionTravel = 0.22f;
        public const float AntiRoll = 4200f;
        public const float Downforce = 0f;              // was 30. Small, just calms high speed

        // Resistance
        public const float RollingResistance = 170f;     // N
        public const float RollingResistanceSpeedGain = 0.006f; // extra rolling resistance per m/s
        public const float AirDrag = 0.41f;              // N per (m/s)^2, ~Cd 0.32 x 2.0 m^2

        // Effects
        public const float SkidSlipThreshold = 0.4f;
        public const float SkidFullSlip = 1.2f;
        public const float SquealSmoothing = 12f;

        // Debug
        public const bool ShowDebug = false;
        public const float MaxDriftAngle = 55f;          // only the full scale of the HUD slip bar now
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

    [Header("European M3 3.2 setup")]
    [Tooltip("Apply the stock road car tune at startup. Turn this off to use the chassis and tire values below.")]
    [SerializeField] private bool useFactoryTune = true;
    [SerializeField, Range(500f, 3000f)] private float carMass = Defaults.CarMass;
    [SerializeField, Range(0.1f, 1f)] private float centerOfMassHeight = Defaults.CenterOfMassHeight;
    [SerializeField, Range(0.3f, 0.7f)] private float frontWeightShare = Defaults.FrontWeightShare;

    [Header("Six speed Getrag and clutch")]
    [SerializeField] private bool automaticShifting = true;
    [SerializeField] private KeyCode shiftUpKey = KeyCode.E;
    [SerializeField] private KeyCode shiftDownKey = KeyCode.Q;
    [SerializeField, Range(0.5f, 1f)] private float drivetrainEfficiency = Defaults.DrivetrainEfficiency;
    [SerializeField, Min(0.1f)] private float shiftDuration = 0.23f;
    [SerializeField, Range(60f, 400f)] private float maxSpeedKmh = Defaults.MaxSpeedKmh;
    [SerializeField, Range(5f, 80f)] private float reverseSpeedKmh = Defaults.ReverseSpeedKmh;

    private static readonly float[] ForwardRatios = { 4.23f, 2.51f, 1.67f, 1.23f, 1f, 0.83f };
    private const float ReverseRatio = 3.89f;
    private const float FinalDrive = 3.23f;
    private const float EngineIdleRpm = 800f;
    private const float EngineLimitRpm = 7600f;
    private const float EngineInertia = 0.26f;
    private const float ClutchCapacity = 430f;
    private const float ClutchStiffness = 32f;

    // Estimated full-load curve pinned to 350 Nm at 3250 and 236 kW at 7400.
    private readonly AnimationCurve engineTorqueCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(800f, 135f), new Keyframe(1500f, 230f),
        new Keyframe(2500f, 313f), new Keyframe(3250f, 350f), new Keyframe(4500f, 348f),
        new Keyframe(5500f, 340f), new Keyframe(6500f, 326f), new Keyframe(7400f, 304.5f),
        new Keyframe(7600f, 290f), new Keyframe(8000f, 230f));

    [Header("Differential (mild LSD)")]
    [Tooltip("0 = open diff, 1 = locked. E36 LSD is mild.")]
    [SerializeField, Range(0f, 1f)] private float lsdLock = Defaults.LsdLock;
    [SerializeField, Range(0f, 300f)] private float lsdPreload = Defaults.LsdPreload;
    [Tooltip("Nm moved from the faster rear wheel to the slower one, per rpm of difference.")]
    [SerializeField, Range(0f, 20f)] private float lsdRamp = Defaults.LsdRamp;

    [Header("Traction Control (set to 0 to turn off)")]
    [Tooltip("Optional game assist. Zero lets the rear tires spin under power.")]
    [SerializeField, Range(0f, 1f)] private float tractionControl = Defaults.TractionControl;
    [Tooltip("Rear wheel forward slip where TC starts to cut.")]
    [SerializeField, Range(0.05f, 1f)] private float tcSlipStart = Defaults.TcSlipStart;
    [Tooltip("Rear wheel forward slip where TC cuts the most.")]
    [SerializeField, Range(0.1f, 2f)] private float tcSlipFull = Defaults.TcSlipFull;

    [Header("Brakes")]
    [SerializeField] private bool antiLockBrakes = true;
    [Tooltip("Torque per front wheel (Nm).")]
    [SerializeField, Range(0f, 6000f)] private float brakeTorque = Defaults.BrakeTorque;
    [Tooltip("Rear brake torque relative to front. 0.55 = ~65% front bias.")]
    [SerializeField, Range(0.1f, 1.5f)] private float rearBrakeShare = Defaults.RearBrakeShare;
    [Tooltip("Brake torque on each rear wheel while the handbrake is held.")]
    [SerializeField, Range(0f, 8000f)] private float handbrakePower = Defaults.HandbrakePower;

    [Header("Pedal Smoothing (units per second)")]
    [SerializeField, Range(1f, 30f)] private float throttleRiseRate = Defaults.ThrottleRise;
    [SerializeField, Range(1f, 30f)] private float throttleFallRate = Defaults.ThrottleFall;
    [SerializeField, Range(1f, 30f)] private float brakeRiseRate = Defaults.BrakeRise;
    [SerializeField, Range(1f, 30f)] private float brakeFallRate = Defaults.BrakeFall;
    [SerializeField, Range(1f, 30f)] private float handbrakeRiseRate = Defaults.HandbrakeRise;
    [SerializeField, Range(1f, 30f)] private float handbrakeFallRate = Defaults.HandbrakeFall;

    [Header("Steering")]
    [Tooltip("Maximum front wheel angle in degrees.")]
    [SerializeField, Range(10f, 45f)] private float maxSteer = Defaults.MaxSteer;
    [Tooltip("Steering wheel turns from lock to lock. E36 = about 3. Sets the steering ratio.")]
    [SerializeField, Range(1.5f, 5f)] private float steerLockToLockTurns = Defaults.LockToLockTurns;
    [Tooltip("How fast the STEERING WHEEL can be turned (deg/s). Road wheel speed = this / steering ratio.")]
    [SerializeField, Range(300f, 3000f)] private float steerSpeed = Defaults.SteerSpeed;
    [Tooltip("Steering left at the reference top speed, as a fraction of maxSteer. Lower = calmer at speed.")]
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteer = Defaults.HighSpeedSteer;
    [Tooltip("Speed where steering starts to reduce.")]
    [SerializeField, Range(0f, 100f)] private float steerReduceStartKmh = Defaults.SteerReduceStartKmh;
    [Tooltip("Reference speed where the steering reduction ends. The real top speed now comes from gearing and drag.")]
    [SerializeField, Range(40f, 300f)] private float topSpeedKmh = Defaults.TopSpeedKmh;
    [Tooltip("How fast the steering input ramps toward the key / stick (units per second). Lower = lazier.")]
    [SerializeField, Range(0.5f, 10f)] private float steerRiseRate = Defaults.SteerRiseRate;
    [Tooltip("How fast the steering returns to center when released. Grows with speed (self-centering).")]
    [SerializeField, Range(0.5f, 12f)] private float steerReturnRate = Defaults.SteerReturnRate;

    [Header("Tires (1 = Unity default grip, higher = more grip)")]
    [SerializeField, Range(0.5f, 4f)] private float gripFront = Defaults.GripFront;
    [Tooltip("Base sideways grip of the rear tires. Front vs rear is the understeer / oversteer balance.")]
    [SerializeField, Range(0.5f, 4f)] private float gripRear = Defaults.GripRear;
    [SerializeField, Range(0.5f, 4f)] private float gripForward = Defaults.GripForward;
    [Tooltip("Slip where side grip peaks. Higher = tire needs more slip angle before it grips fully.")]
    [SerializeField, Range(0.05f, 0.6f)] private float sidewaysPeakSlip = Defaults.SidewaysPeakSlip;
    [Tooltip("Slip where the tire is fully sliding.")]
    [SerializeField, Range(0.2f, 2f)] private float sidewaysSlideSlip = Defaults.SidewaysSlideSlip;
    [Tooltip("Grip kept when fully sliding, as a fraction of peak. Lower = snappier break-away, harder to catch.")]
    [SerializeField, Range(0.3f, 1f)] private float sidewaysSlideGrip = Defaults.SidewaysSlideGrip;
    [Tooltip("0 = grip scales exactly with load. Higher = weight transfer costs more total grip (lift-off oversteer).")]
    [SerializeField, Range(0f, 0.6f)] private float loadSensitivity = Defaults.LoadSensitivity;
    [Tooltip("Forward slip (wheelspin / lockup) where side grip loss is full.")]
    [SerializeField, Range(0.2f, 2f)] private float combinedSlipReference = Defaults.CombinedSlipReference;
    [Tooltip("Side grip lost on a locked or fully spinning wheel. 0 = none (handbrake will not slide).")]
    [SerializeField, Range(0f, 0.95f)] private float combinedSlipGripLoss = Defaults.CombinedSlipGripLoss;
    [Tooltip("How fast tire grip follows load and slip. Lower = softer, higher = twitchier.")]
    [SerializeField, Range(3f, 80f)] private float gripResponse = Defaults.GripResponse;

    [Header("Drift Detection (HUD + events only)")]
    [Tooltip("Slip angle (degrees) needed to start a drift.")]
    [SerializeField, Range(3f, 45f)] private float driftEnterAngle = Defaults.DriftEnterAngle;
    [Tooltip("Slip angle below which the drift ends (lower than enter = no flicker).")]
    [SerializeField, Range(1f, 30f)] private float driftExitAngle = Defaults.DriftExitAngle;
    [SerializeField, Range(5f, 100f)] private float driftMinSpeedKmh = Defaults.DriftMinSpeedKmh;
    [Tooltip("Seconds the slip must stay low before the drift is declared over.")]
    [SerializeField, Range(0f, 1f)] private float driftExitDelay = Defaults.DriftExitDelay;

    [Header("Counter-Steer Assist (set to 0 to turn off)")]
    [Tooltip("0 = off, 1 = strong. The front wheels are turned toward the direction of travel by (slip angle x this).")]
    [SerializeField, Range(0f, 1f)] private float counterSteerAssist = Defaults.CounterSteerAssist;
    [Tooltip("Slip angle (degrees) below which no counter-steer is applied.")]
    [SerializeField, Range(0f, 10f)] private float counterSteerDeadZone = Defaults.CounterSteerDeadZone;
    [Tooltip("Counter-steer fades in above this speed.")]
    [SerializeField, Range(0f, 60f)] private float counterSteerMinKmh = Defaults.CounterSteerMinKmh;

    [Header("Stability")]
    [Tooltip("Damp yaw only while a rear slide is growing. Zero disables this assist.")]
    [SerializeField, Range(0f, 5f)] private float spinDamping = 2.4f;
    [SerializeField, Range(0.1f, 0.6f)] private float suspensionTravel = Defaults.SuspensionTravel;
    [Tooltip("Anti-roll bar stiffness. Higher = flatter cornering, harder to flip.")]
    [SerializeField, Range(0f, 20000f)] private float antiRoll = Defaults.AntiRoll;
    [Tooltip("Downward force per m/s of speed.")]
    [SerializeField, Range(0f, 150f)] private float downforce = Defaults.Downforce;

    [Header("Resistance")]
    [SerializeField, Range(0f, 600f)] private float rollingResistance = Defaults.RollingResistance;
    [Tooltip("Rolling resistance grows by this fraction per m/s of speed.")]
    [SerializeField, Range(0f, 0.05f)] private float rollingResistanceSpeedGain = Defaults.RollingResistanceSpeedGain;
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
    [Tooltip("Full scale (+/-) of the slip angle bar in the debug HUD.")]
    [SerializeField, Range(20f, 90f)] private float maxDriftAngle = Defaults.MaxDriftAngle;

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
        /// <summary>Vertical load on this tire in Newtons (shows weight transfer).</summary>
        public float load;
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
    /// <summary>Average rear side-grip multiplier (1 = normal, below 1 = loose or unloaded).</summary>
    public float RearGripFactor => rearGripFactor;
    /// <summary>0..1, smoothed. Feed it straight into an AudioSource volume.</summary>
    public float TireSquealVolume => squealVolume;
    public float EngineRpm => engineOmega / RpmToRad;
    public float IdleRpm => EngineIdleRpm;
    public float MaxRpm => EngineLimitRpm;
    public int Gear { get; private set; } = 1;
    public bool IsShifting => shiftTimeRemaining > 0f;
    public float Throttle => appliedThrottle;
    public float Brake => brakePedal;
    public float Handbrake => handbrakePedal;
    public float Clutch => clutchTravel;
    public float EngineTorqueNm { get; private set; }
    public float DistanceMeters { get; private set; }
    public WheelSlipData GetWheelSlip(int index) => slipData[index];

    // =====================================================================
    //  PRIVATE STATE
    // =====================================================================

    private const float CoastBrake = 100f;           // now only a standstill hold, engine braking does the rest

    private const float SlipMinSpeed = 2f;           // m/s: below this the slip angle is meaningless
    private const float FlippedUpThreshold = 0.25f;  // transform.up.y below this = car is on its side / roof
    private const float RpmToRad = 0.10471976f;      // 2*pi/60
    private const float SteerReturnSpeedBoost = 2f;  // self-centering is this many times stronger at top speed

    private Rigidbody rb;
    private WheelCollider[] wheels;                 // FL, FR, BL, BR
    private Transform[] meshes;
    private Quaternion[] meshOffset;
    private Vector3[] meshCenterOffset;

    private readonly WheelSlipData[] slipData = new WheelSlipData[4];

    // input
    private float throttleInput, steerInput;
    private bool handbrakeHeld;

    private float appliedThrottle;                  // throttle pedal after smoothing
    private float brakePedal;
    private float handbrakePedal;
    private float steerSmooth;                      // steering input after smoothing (-1..1)

    // motion
    private float speedMS, forwardSpeedMS, slipAngle;
    private float steerAngle;
    private int groundedCount;

    private bool reverseSelected;
    private float tractionCut;
    private float engineOmega = EngineIdleRpm * RpmToRad;
    private float clutchEngagement, clutchTravel = 1f, filteredWheelRpm;
    private float shiftTimeRemaining, shiftCooldown, manualShiftHold;
    private int selectedGear = 1, queuedShift;
    private bool limiterActive;
    private float wheelbase, frontTrack;
    private readonly float[] absPressure = { 1f, 1f, 1f, 1f };

    private readonly float[] latGripState = { 1f, 1f, 1f, 1f };
    private readonly float[] fwdGripState = { 1f, 1f, 1f, 1f };
    private readonly float[] staticLoad = { 3000f, 3000f, 3000f, 3000f };
    private float rearGripFactor = 1f;
    private float frontGripFactor = 1f;

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

    private float SteeringRatio => steerLockToLockTurns * 360f / (2f * Mathf.Max(maxSteer, 1f));

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

        SortAxle(FrontLeft, FrontRight);
        SortAxle(RearLeft, RearRight);
        if (useFactoryTune)
        {
            ApplyFactoryTune();
            FitFactoryGeometry();
        }

        wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);

        ApplyCarSetup();
        CacheMeshOffsets();
    }

    private void OnDisable()
    {
        if (wheels == null)
            return;

        foreach (WheelCollider wheel in wheels)
        {
            if (wheel == null)
                continue;

            wheel.motorTorque = 0f;
            wheel.brakeTorque = 0f;
        }
    }

    // Live tuning applies chassis and tire changes during play.
    private void OnValidate()
    {
        driftExitAngle = Mathf.Min(driftExitAngle, driftEnterAngle);
        skidFullSlip = Mathf.Max(skidFullSlip, skidSlipThreshold + 0.1f);

        sidewaysSlideSlip = Mathf.Max(sidewaysSlideSlip, sidewaysPeakSlip + 0.05f);
        tcSlipFull = Mathf.Max(tcSlipFull, tcSlipStart + 0.1f);

        if (Application.isPlaying && enabled && rb != null && wheels != null)
            ApplyCarSetup();
    }

    [ContextMenu("Copy E36 M3 Factory Tune")]
    private void ApplyFactoryTune()
    {
        carMass = Defaults.CarMass;
        centerOfMassHeight = Defaults.CenterOfMassHeight;
        frontWeightShare = Defaults.FrontWeightShare;
        drivetrainEfficiency = Defaults.DrivetrainEfficiency;
        maxSpeedKmh = Defaults.MaxSpeedKmh;
        reverseSpeedKmh = Defaults.ReverseSpeedKmh;
        lsdLock = Defaults.LsdLock;
        lsdPreload = Defaults.LsdPreload;
        lsdRamp = Defaults.LsdRamp;
        brakeTorque = Defaults.BrakeTorque;
        rearBrakeShare = Defaults.RearBrakeShare;
        handbrakePower = Defaults.HandbrakePower;
        throttleRiseRate = Defaults.ThrottleRise;
        throttleFallRate = Defaults.ThrottleFall;
        brakeRiseRate = Defaults.BrakeRise;
        brakeFallRate = Defaults.BrakeFall;
        handbrakeRiseRate = Defaults.HandbrakeRise;
        handbrakeFallRate = Defaults.HandbrakeFall;
        maxSteer = Defaults.MaxSteer;
        steerLockToLockTurns = Defaults.LockToLockTurns;
        steerSpeed = Defaults.SteerSpeed;
        highSpeedSteer = Defaults.HighSpeedSteer;
        steerReduceStartKmh = Defaults.SteerReduceStartKmh;
        topSpeedKmh = Defaults.TopSpeedKmh;
        steerRiseRate = Defaults.SteerRiseRate;
        steerReturnRate = Defaults.SteerReturnRate;
        gripFront = Defaults.GripFront;
        gripRear = Defaults.GripRear;
        gripForward = Defaults.GripForward;
        sidewaysPeakSlip = Defaults.SidewaysPeakSlip;
        sidewaysSlideSlip = Defaults.SidewaysSlideSlip;
        sidewaysSlideGrip = Defaults.SidewaysSlideGrip;
        loadSensitivity = Defaults.LoadSensitivity;
        combinedSlipReference = Defaults.CombinedSlipReference;
        combinedSlipGripLoss = Defaults.CombinedSlipGripLoss;
        gripResponse = Defaults.GripResponse;
        tractionControl = Defaults.TractionControl;
        tcSlipStart = Defaults.TcSlipStart;
        tcSlipFull = Defaults.TcSlipFull;
        counterSteerAssist = Defaults.CounterSteerAssist;
        suspensionTravel = Defaults.SuspensionTravel;
        antiRoll = Defaults.AntiRoll;
        downforce = Defaults.Downforce;
        rollingResistance = Defaults.RollingResistance;
        rollingResistanceSpeedGain = Defaults.RollingResistanceSpeedGain;
        airDrag = Defaults.AirDrag;
        showDebug = Defaults.ShowDebug;
    }

    private Vector3 WheelCenter(int index)
    {
        WheelCollider wheel = wheels[index];
        return wheel.transform.TransformPoint(wheel.center);
    }

    private Vector3 AxleCenter(int left, int right) => (WheelCenter(left) + WheelCenter(right)) * 0.5f;

    private void SortAxle(int left, int right)
    {
        if (Vector3.Dot(WheelCenter(right) - WheelCenter(left), transform.right) >= 0f)
            return;

        (wheels[left], wheels[right]) = (wheels[right], wheels[left]);
        (meshes[left], meshes[right]) = (meshes[right], meshes[left]);
    }

    private void FitFactoryGeometry()
    {
        Vector3 scale = transform.lossyScale;
        float modelWheelbase = Vector3.Dot(AxleCenter(FrontLeft, FrontRight) - AxleCenter(RearLeft, RearRight), transform.forward);
        // The existing E36 scene rig is a two-times-scale import.
        bool oversized = Mathf.Abs(scale.x - scale.y) < 0.01f && Mathf.Abs(scale.y - scale.z) < 0.01f &&
            modelWheelbase > Defaults.Wheelbase * 1.5f;
        if (oversized)
            transform.localScale *= Defaults.Wheelbase / modelWheelbase;

        Vector3 front = AxleCenter(FrontLeft, FrontRight);
        Vector3 rear = AxleCenter(RearLeft, RearRight);
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wheel = wheels[i];
            Transform mount = wheel.transform;
            Vector3 center = WheelCenter(i);
            if (oversized)
            {
                Vector3 axle = i < RearLeft ? front : rear;
                float track = i < RearLeft ? Defaults.FrontTrack : Defaults.RearTrack;
                float side = i == FrontLeft || i == RearLeft ? -1f : 1f;
                center += transform.right * (side * track * 0.5f - Vector3.Dot(center - axle, transform.right));
            }
            mount.rotation = transform.rotation;
            Vector3 wheelScale = mount.lossyScale;
            mount.localScale = new Vector3(
                mount.localScale.x / wheelScale.x,
                mount.localScale.y / wheelScale.y,
                mount.localScale.z / wheelScale.z);
            wheel.center = mount.InverseTransformPoint(center);
            wheel.radius = i < RearLeft ? Defaults.FrontTireRadius : Defaults.RearTireRadius;
        }

        // The E36 model has separate rim and tire children under each wheel group.
        if (frontLeftMesh == null && frontRightMesh == null && backLeftMesh == null && backRightMesh == null)
        {
            Transform leftFrontVisual = transform.Find("RF");
            Transform rightFrontVisual = transform.Find("LF");
            Transform leftRearVisual = transform.Find("LB");
            Transform rightRearVisual = transform.Find("RB");
            if (leftFrontVisual != null && rightFrontVisual != null && leftRearVisual != null && rightRearVisual != null)
            {
                meshes[FrontLeft] = leftFrontVisual;
                meshes[FrontRight] = rightFrontVisual;
                meshes[RearLeft] = leftRearVisual;
                meshes[RearRight] = rightRearVisual;
            }
        }
    }

    private bool WheelsAreValid()
    {
        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null)
            {
                Debug.LogError("[WhellContoll] A WheelCollider slot is empty. Assign all 4 WheelColliders.", this);
                return false;
            }
            for (int j = i + 1; j < 4; j++)
            {
                if (wheels[i] == wheels[j])
                {
                    Debug.LogError("[WhellContoll] The same WheelCollider is assigned to more than one slot.", this);
                    return false;
                }
            }
        }

        if (GetComponent<Rigidbody>() == null)
        {
            Debug.LogError("[WhellContoll] No Rigidbody found on the car root.", this);
            return false;
        }

        return true;
    }

    private void ApplyCarSetup()
    {
        rb.mass = carMass;
        Vector3 front = AxleCenter(FrontLeft, FrontRight);
        Vector3 rear = AxleCenter(RearLeft, RearRight);
        wheelbase = Mathf.Max(0.1f, Vector3.Dot(front - rear, transform.forward));
        frontTrack = Mathf.Abs(Vector3.Dot(WheelCenter(FrontRight) - WheelCenter(FrontLeft), transform.right));

        Vector3 axle = Vector3.Lerp(rear, front, frontWeightShare);
        float averageRadius = (wheels[FrontLeft].radius + wheels[RearLeft].radius) * 0.5f;
        // The target hub is below the top of the WheelCollider suspension ray.
        float rideExtension = suspensionTravel * 0.45f;
        Vector3 com = axle + transform.up * (centerOfMassHeight - averageRadius - rideExtension);
        rb.centerOfMass = transform.InverseTransformPoint(com);

        for (int i = 0; i < wheels.Length; i++)
        {
            bool frontWheel = i < RearLeft;
            WheelCollider wheel = wheels[i];
            float supportedMass = carMass * (frontWheel ? frontWeightShare : 1f - frontWeightShare) * 0.5f;
            staticLoad[i] = supportedMass * Physics.gravity.magnitude;
            wheel.sprungMass = supportedMass;
            wheel.suspensionDistance = suspensionTravel;
            float spring = frontWheel ? 36000f : 39000f;
            wheel.suspensionSpring = new JointSpring
            {
                spring = spring,
                damper = 0.9f * Mathf.Sqrt(spring * supportedMass),
                targetPosition = 0.32f
            };
            wheel.forceAppPointDistance = frontWheel ? 0.18f : 0.22f;
            wheel.forwardFriction = MakeCurve(0.20f, 1f, 0.65f, 0.84f, gripForward);
            wheel.sidewaysFriction = MakeCurve(sidewaysPeakSlip, 1f, sidewaysSlideSlip,
                sidewaysSlideGrip, frontWheel ? gripFront : gripRear);
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
        // The pedals and rack already smooth these inputs in FixedUpdate.
        throttleInput = Mathf.Clamp(Input.GetAxisRaw("Vertical"), -1f, 1f);
        steerInput = Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);
        handbrakeHeld = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakePadButton);

        if (Input.GetKeyDown(shiftUpKey)) queuedShift = 1;
        if (Input.GetKeyDown(shiftDownKey)) queuedShift = -1;
        if (Input.GetKeyDown(resetKey)) ResetUpright();
        if (Input.GetKeyDown(debugToggleKey)) showDebug = !showDebug;
    }

    // =====================================================================
    //  PHYSICS (FixedUpdate)
    // =====================================================================

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        UpdateMotion();
        DistanceMeters += speedMS * dt;
        UpdateDriftState(dt);           // IsDrifting, duration, events
        UpdateWheelSlip(dt);            // per-wheel slip + load, events, squeal
        ApplyThrottleAndBrakes(dt);
        ApplySteering(dt);              // smoothed, speed-sensitive steering + counter-steer
        UpdateFrontGrip(dt);            // tire grip from load + combined slip
        UpdateRearGrip(dt);
        ApplyResistanceAndStability();
        DampenGrowingSlide();
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
        groundedCount = 0;   // used so rolling resistance stops when airborne

        for (int i = 0; i < 4; i++)
        {
            WheelSlipData d = default;

            if (wheels[i].GetGroundHit(out WheelHit hit))
            {
                d.grounded = true;
                groundedCount++;
                d.forwardSlip = hit.forwardSlip;
                d.sidewaysSlip = hit.sidewaysSlip;
                d.load = hit.force;   // vertical tire load, drives the weight-transfer grip model

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

    private void ApplyThrottleAndBrakes(float dt)
    {
        bool upright = transform.up.y > FlippedUpThreshold;
        UpdateDriveDirection();

        float demand = reverseSelected ? -throttleInput : throttleInput;
        float throttleTarget = upright ? Mathf.Max(0f, demand) : 0f;
        float brakeTarget = Mathf.Max(0f, -demand);
        float handbrakeTarget = handbrakeHeld ? 1f : 0f;
        appliedThrottle = Mathf.MoveTowards(appliedThrottle, throttleTarget,
            (throttleTarget > appliedThrottle ? throttleRiseRate : throttleFallRate) * dt);
        brakePedal = Mathf.MoveTowards(brakePedal, brakeTarget,
            (brakeTarget > brakePedal ? brakeRiseRate : brakeFallRate) * dt);
        handbrakePedal = Mathf.MoveTowards(handbrakePedal, handbrakeTarget,
            (handbrakeTarget > handbrakePedal ? handbrakeRiseRate : handbrakeFallRate) * dt);

        UpdateTractionControl(dt);
        UpdateGearbox(dt);
        ApplyDriveTorque(UpdateDrivetrain(dt, upright), upright);

        float hold = speedMS < 0.6f && throttleTarget < 0.05f && brakePedal < 0.05f
            ? CoastBrake * (1f - speedMS / 0.6f) : 0f;

        for (int i = 0; i < wheels.Length; i++)
        {
            bool rear = i >= RearLeft;
            float service = brakePedal * brakeTorque * (rear ? rearBrakeShare : 1f);
            // Service ABS can release individual wheels. The mechanical handbrake bypasses it.
            bool locking = antiLockBrakes && service > 0f && speedMS > 3f &&
                slipData[i].grounded && Mathf.Abs(slipData[i].forwardSlip) > 0.25f;
            absPressure[i] = Mathf.MoveTowards(absPressure[i], locking ? 0.15f : 1f,
                (locking ? 12f : 5f) * dt);
            wheels[i].brakeTorque = service * absPressure[i] + (rear ? handbrakePedal * handbrakePower : 0f) + hold;
        }
    }

    private void UpdateTractionControl(float dt)
    {
        float target = 0f;
        if (tractionControl > 0f && groundedCount > 0 && appliedThrottle > 0.05f && brakePedal < 0.1f && handbrakePedal < 0.1f)
        {
            float spin = Mathf.Max(Mathf.Abs(slipData[RearLeft].forwardSlip), Mathf.Abs(slipData[RearRight].forwardSlip));
            float rearSideSlip = Mathf.Max(Mathf.Abs(slipData[RearLeft].sidewaysSlip), Mathf.Abs(slipData[RearRight].sidewaysSlip));
            // Let the driver launch in a straight line. Trim power when rear wheelspin uses up cornering grip.
            float cornering = Mathf.InverseLerp(0.025f, sidewaysPeakSlip, rearSideSlip);
            target = tractionControl * cornering * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tcSlipStart, tcSlipFull, spin));
        }
        tractionCut = Mathf.MoveTowards(tractionCut, target, (target > tractionCut ? 12f : 4f) * dt);
    }

    // Preserve the existing brake-first W/S controls, including on a slope.
    private void UpdateDriveDirection()
    {
        if (speedMS > 0.65f)
            return;

        bool wantsReverse = !reverseSelected && throttleInput < -0.12f;
        bool wantsForward = reverseSelected && throttleInput > 0.12f;
        if (wantsReverse || wantsForward)
        {
            reverseSelected = wantsReverse;
            appliedThrottle = 0f;
            BeginShift(reverseSelected ? -1 : 1);
            manualShiftHold = 0f;
            queuedShift = 0;
        }
    }

    private static float OverallRatio(int gear)
    {
        return (gear < 0 ? -ReverseRatio : ForwardRatios[gear - 1]) * FinalDrive;
    }

    private float DrivenWheelRpm() => (wheels[RearLeft].rpm + wheels[RearRight].rpm) * 0.5f;

    private void BeginShift(int gear)
    {
        selectedGear = gear;
        shiftTimeRemaining = shiftDuration;
        clutchEngagement = 0f;
    }

    private void UpdateGearbox(float dt)
    {
        shiftCooldown = Mathf.Max(0f, shiftCooldown - dt);
        manualShiftHold = Mathf.Max(0f, manualShiftHold - dt);
        if (IsShifting)
        {
            shiftTimeRemaining = Mathf.Max(0f, shiftTimeRemaining - dt);
            if (!IsShifting)
            {
                Gear = selectedGear;
                shiftCooldown = 0.22f;
            }
            return;
        }

        int shift = queuedShift;
        queuedShift = 0;
        if (reverseSelected || Gear < 1)
            return;

        // Prevent downshifts that would over-rev even if the driven wheels are sliding.
        float roadWheelRpm = Mathf.Abs(forwardSpeedMS) / (wheels[RearLeft].radius * RpmToRad);
        float wheelRpm = Mathf.Abs(DrivenWheelRpm());
        float safeWheelRpm = Mathf.Max(roadWheelRpm, wheelRpm);
        if (shift != 0)
        {
            int requested = Mathf.Clamp(Gear + shift, 1, ForwardRatios.Length);
            if (requested != Gear && (shift > 0 || safeWheelRpm * OverallRatio(requested) < EngineLimitRpm - 180f))
            {
                BeginShift(requested);
                manualShiftHold = 2f;
            }
            return;
        }

        if (!automaticShifting || manualShiftHold > 0f || shiftCooldown > 0f ||
            groundedCount < 2 || handbrakePedal > 0.15f)
            return;

        float rpm = Mathf.Min(wheelRpm, roadWheelRpm + 350f / OverallRatio(Gear)) * OverallRatio(Gear);
        float upshiftRpm = Mathf.Lerp(3150f, 7410f, appliedThrottle);
        float downshiftRpm = Mathf.Lerp(1450f, 3750f, appliedThrottle);
        if (Gear < ForwardRatios.Length && rpm > upshiftRpm && EngineRpm > upshiftRpm - 200f)
            BeginShift(Gear + 1);
        else if (Gear > 1 && rpm < downshiftRpm &&
                 safeWheelRpm * OverallRatio(Gear - 1) < Mathf.Min(upshiftRpm - 350f, EngineLimitRpm - 180f))
            BeginShift(Gear - 1);
    }

    // Integrate crank inertia, then transfer torque through a finite-capacity slipping clutch.
    private float UpdateDrivetrain(float dt, bool upright)
    {
        float ratio = OverallRatio(Gear);
        // WheelCollider reports the last physics step's wheel speeds. Compliance in
        // the driveline keeps that feedback from alternating between drive and braking.
        filteredWheelRpm = Mathf.Lerp(filteredWheelRpm, DrivenWheelRpm(), 1f - Mathf.Exp(-dt / 0.10f));
        float shaftOmega = filteredWheelRpm * RpmToRad * ratio;
        float rpm = EngineRpm;
        if (rpm >= EngineLimitRpm)
            limiterActive = true;
        else if (rpm <= EngineLimitRpm - 170f)
            limiterActive = false;

        float signedSpeed = forwardSpeedMS * 3.6f * Mathf.Sign(ratio);
        float speedLimit = Gear < 0 ? reverseSpeedKmh : maxSpeedKmh;
        float speedCut = Mathf.InverseLerp(speedLimit, speedLimit - 4f, signedSpeed);
        float throttle = upright && !limiterActive && !IsShifting
            ? appliedThrottle * (1f - tractionCut) * speedCut : 0f;
        float pumpingLoss = 18f + 0.008f * rpm;
        float idleTorque = Mathf.Clamp((EngineIdleRpm + 65f - rpm) * 0.4f, 0f, 95f);
        // The curve is net crank torque at full load. Pumping loss is added on lift.
        EngineTorqueNm = engineTorqueCurve.Evaluate(rpm) * throttle
            - pumpingLoss * (1f - throttle) + idleTorque * (1f - throttle);
        float freeOmega = engineOmega + EngineTorqueNm * dt / EngineInertia;

        float launchRpm = Gear <= 1 ? Mathf.Lerp(EngineIdleRpm + 100f, 2900f, appliedThrottle) : EngineIdleRpm;
        bool brakingToStop = brakePedal > 0.08f && shaftOmega < 1250f * RpmToRad;
        bool clutchOpen = IsShifting || !upright || handbrakePedal > 0.22f || brakingToStop ||
            (shaftOmega < EngineIdleRpm * RpmToRad && appliedThrottle < 0.04f);
        float clutchTorque = 0f;
        float clutchPressure = 0f;
        if (clutchOpen)
            clutchEngagement = 0f;
        else
        {
            clutchEngagement = Mathf.MoveTowards(clutchEngagement, 1f, 7f * dt);
            float capacity = ClutchCapacity * clutchEngagement;
            float maxDriveTorque = capacity;
            // Slip on launch to hold the revs up. At road speed the wheels can backdrive the engine.
            if (shaftOmega < launchRpm * RpmToRad)
                maxDriveTorque = Mathf.Clamp((freeOmega - launchRpm * RpmToRad) * EngineInertia / dt, 0f, capacity);

            float speedDifference = freeOmega - shaftOmega;
            clutchTorque = Mathf.Clamp(speedDifference / (dt / EngineInertia + 1f / ClutchStiffness),
                -capacity, maxDriveTorque);
            clutchPressure = (speedDifference > 0f ? maxDriveTorque : capacity) / ClutchCapacity;
        }

        engineOmega = Mathf.Clamp(freeOmega - clutchTorque * dt / EngineInertia,
            EngineIdleRpm * 0.6f * RpmToRad, (EngineLimitRpm + 450f) * RpmToRad);
        clutchTravel = 1f - clutchPressure;

        return clutchTorque * ratio * drivetrainEfficiency;
    }

    private void ApplyDriveTorque(float wheelTorque, bool upright)
    {
        float half = upright ? wheelTorque * 0.5f : 0f;
        float rpmDifference = wheels[RearLeft].rpm - wheels[RearRight].rpm;
        float maxBias = Mathf.Min(Mathf.Abs(half), Mathf.Abs(half) * lsdLock + lsdPreload);
        float bias = Mathf.Clamp(rpmDifference * lsdRamp, -maxBias, maxBias);
        wheels[FrontLeft].motorTorque = 0f;
        wheels[FrontRight].motorTorque = 0f;
        wheels[RearLeft].motorTorque = half - bias;
        wheels[RearRight].motorTorque = half + bias;
    }

    // --- steering -------------------------------------------------------------
    private void ApplySteering(float dt)
    {
        // the input ramps toward the key / stick and returns to center on its own.
        // The return gets stronger with speed (self-centering).
        bool rising = Mathf.Abs(steerInput) > Mathf.Abs(steerSmooth) && steerInput * steerSmooth >= 0f;
        float returnBoost = Mathf.Lerp(1f, SteerReturnSpeedBoost, Mathf.Clamp01(SpeedKmh / Mathf.Max(topSpeedKmh, 1f)));
        float inputRate = rising ? steerRiseRate : steerReturnRate * returnBoost;
        steerSmooth = Mathf.MoveTowards(steerSmooth, steerInput, inputRate * dt);

        float speedFactor = Mathf.Lerp(1f, highSpeedSteer,
            Mathf.InverseLerp(steerReduceStartKmh, Mathf.Max(topSpeedKmh, steerReduceStartKmh + 1f), SpeedKmh));
        float roadLimit = Mathf.Atan(23f * wheelbase / Mathf.Max(1f, speedMS * speedMS)) * Mathf.Rad2Deg + 2f;
        // Give the driver more rack travel while steering into a slide.
        if (steerSmooth * slipAngle > 0f)
            roadLimit = Mathf.Lerp(roadLimit, maxSteer, 0.55f * Mathf.InverseLerp(8f, 34f, Mathf.Abs(slipAngle)));
        float target = steerSmooth * Mathf.Min(maxSteer * speedFactor, roadLimit) + CounterSteer();
        target = Mathf.Clamp(target, -maxSteer, maxSteer);

        steerAngle = Mathf.MoveTowards(steerAngle, target, steerSpeed / SteeringRatio * dt);
        float tangent = Mathf.Tan(steerAngle * Mathf.Deg2Rad);
        float trackFactor = frontTrack * 0.4f / wheelbase;
        wheels[FrontLeft].steerAngle = Mathf.Atan(tangent / (1f + tangent * trackFactor)) * Mathf.Rad2Deg;
        wheels[FrontRight].steerAngle = Mathf.Atan(tangent / (1f - tangent * trackFactor)) * Mathf.Rad2Deg;
    }

    // Turns the front wheels toward the direction of travel.
    // The angle that points the wheels exactly along the velocity IS the slip angle,
    // so the assist is simply (slip angle - dead zone) x assist, fading in with speed.
    // The player's own steering is added on top, so steering into the slide still widens the angle.
    // Set counterSteerAssist to 0 to turn it off.
    private float CounterSteer()
    {
        if (counterSteerAssist <= 0f) return 0f;

        float speedGate = Mathf.InverseLerp(counterSteerMinKmh, counterSteerMinKmh * 2f + 1f, SpeedKmh);
        if (speedGate <= 0f) return 0f;

        float beyondDeadZone = Mathf.Max(0f, Mathf.Abs(slipAngle) - counterSteerDeadZone);
        if (beyondDeadZone <= 0f) return 0f;

        float correction = Mathf.Min(8f, beyondDeadZone * counterSteerAssist);
        return Mathf.Sign(slipAngle) * correction * speedGate;
    }

    // --- tire grip: load sensitivity + combined slip ------------------------------

    // Nothing here knows about drifting. Grip only depends on tire load and forward slip.
    private void UpdateFrontGrip(float dt)
    {
        float a = UpdateWheelGrip(0, gripFront, dt);
        float b = UpdateWheelGrip(1, gripFront, dt);
        frontGripFactor = (a + b) * 0.5f;
    }

    private void UpdateRearGrip(float dt)
    {
        float a = UpdateWheelGrip(2, gripRear, dt);
        float b = UpdateWheelGrip(3, gripRear, dt);
        rearGripFactor = (a + b) * 0.5f;
    }

    // Returns the side-grip multiplier of this wheel.
    private float UpdateWheelGrip(int i, float baseGrip, float dt)
    {
        WheelSlipData d = slipData[i];
        float targetSide = 1f;
        float targetForward = 1f;

        if (d.grounded)
        {
            // Weight transfer cost: the WheelCollider already makes grip grow with load. Real tires gain
            // less than that, so load moved between tires (braking, throttle, cornering) costs total grip.
            float loadRatio = Mathf.Clamp(d.load / Mathf.Max(staticLoad[i], 1f), 0f, 3f);
            float loadMul = Mathf.Clamp(1f - loadSensitivity * (loadRatio - 1f), 0.6f, 1.4f);

            // Friction circle: a spinning or locked wheel has less side grip left.
            float u = Mathf.Clamp01(Mathf.Abs(d.forwardSlip) / Mathf.Max(combinedSlipReference, 0.05f));
            float sideKeep = 1f - combinedSlipGripLoss * Mathf.SmoothStep(0f, 1f, u);

            targetSide = loadMul * sideKeep;
            float lateralSlip = Mathf.InverseLerp(sidewaysPeakSlip, sidewaysSlideSlip, Mathf.Abs(d.sidewaysSlip));
            targetForward = loadMul * Mathf.Lerp(1f, 0.75f, lateralSlip);
        }

        // Smooth it, so the grip change does not feed back into the slip and jitter.
        float k = 1f - Mathf.Exp(-gripResponse * dt);
        latGripState[i] = Mathf.Lerp(latGripState[i], targetSide, k);
        fwdGripState[i] = Mathf.Lerp(fwdGripState[i], targetForward, k);

        SetSidewaysGrip(wheels[i], baseGrip * latGripState[i]);
        SetForwardGrip(wheels[i], gripForward * fwdGripState[i]);
        return latGripState[i];
    }

    private void SetSidewaysGrip(WheelCollider wheel, float stiffness)
    {
        WheelFrictionCurve f = wheel.sidewaysFriction;
        f.stiffness = stiffness;
        wheel.sidewaysFriction = f;
    }

    private void SetForwardGrip(WheelCollider wheel, float stiffness)
    {
        WheelFrictionCurve f = wheel.forwardFriction;
        f.stiffness = stiffness;
        wheel.forwardFriction = f;
    }

    // --- resistance & stability ---------------------------------------------------------
    private void ApplyResistanceAndStability()
    {
        Vector3 vel = Velocity;
        float spd = vel.magnitude;

        if (spd > 0.1f)
        {

            float rolling = rollingResistance * (1f + spd * rollingResistanceSpeedGain) * (groundedCount / 4f);
            rb.AddForce(-vel / spd * (rolling + airDrag * spd * spd));
        }

        rb.AddForce(-transform.up * downforce * spd);

        ApplyAntiRoll(FrontLeft, FrontRight, antiRoll);
        ApplyAntiRoll(RearLeft, RearRight, antiRoll * 0.70f);
    }

    private void DampenGrowingSlide()
    {
        if (forwardSpeedMS < 4f || handbrakeHeld || handbrakePedal > 0.2f ||
            !slipData[RearLeft].grounded || !slipData[RearRight].grounded)
            return;

        Vector3 up = transform.up;
        float yawRate = Vector3.Dot(rb.angularVelocity, up);
        if (yawRate * slipAngle >= 0f)
            return;

        float amount = Mathf.InverseLerp(6f, 23f, Mathf.Abs(slipAngle));
        rb.AddTorque(-up * (yawRate * spinDamping * amount), ForceMode.Acceleration);
    }

    private void ApplyAntiRoll(int left, int right, float rate)
    {
        if (!slipData[left].grounded || !slipData[right].grounded)
            return;

        float leftTravel = SuspensionExtension(left);
        float rightTravel = SuspensionExtension(right);
        float force = (leftTravel - rightTravel) * rate;
        rb.AddForceAtPosition(-transform.up * force, WheelCenter(left));
        rb.AddForceAtPosition(transform.up * force, WheelCenter(right));
    }

    private float SuspensionExtension(int index)
    {
        wheels[index].GetWorldPose(out Vector3 hub, out _);
        float extension = Vector3.Dot(WheelCenter(index) - hub, transform.up);
        return Mathf.Clamp01(extension / wheels[index].suspensionDistance);
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
        steerSmooth = 0f;
        appliedThrottle = brakePedal = handbrakePedal = 0f;
        reverseSelected = false;
        Gear = selectedGear = 1;
        queuedShift = 0;
        filteredWheelRpm = 0f;
        shiftTimeRemaining = shiftCooldown = manualShiftHold = 0f;
        limiterActive = false;
        engineOmega = EngineIdleRpm * RpmToRad;
        clutchEngagement = 0f;
        clutchTravel = 1f;
        tractionCut = 0f;
        for (int i = 0; i < 4; i++)
        {
            latGripState[i] = 1f;
            fwdGripState[i] = 1f;
            absPressure[i] = 1f;
            wheels[i].motorTorque = wheels[i].brakeTorque = 0f;
        }
        rearGripFactor = 1f;
        frontGripFactor = 1f;
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

        const float w = 360f;
        const float h = 235f;   // taller, 2 new lines

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
            "Grip front: " + frontGripFactor.ToString("0.00") + "   rear: " + rearGripFactor.ToString("0.00") + "\n" +
            "Throttle: " + throttleInput.ToString("0.00") + "   Handbrake: " + (handbrakeHeld ? "ON" : "off") + "   TC: " + tractionCut.ToString("0.00") + "\n" +
            "Gear: " + (Gear < 0 ? "R" : Gear.ToString()) + "   RPM: " + EngineRpm.ToString("0") + "\n" +
            "Steering wheel: " + (steerAngle * SteeringRatio).ToString("0") + " deg\n" +
            "Wheel slip FL/FR/RL/RR: " +
            slipData[0].slip01.ToString("0.0") + " " + slipData[1].slip01.ToString("0.0") + " " +
            slipData[2].slip01.ToString("0.0") + " " + slipData[3].slip01.ToString("0.0") + "\n" +
            "Load FL/FR/RL/RR (kN): " +
            (slipData[0].load / 1000f).ToString("0.0") + " " + (slipData[1].load / 1000f).ToString("0.0") + " " +
            (slipData[2].load / 1000f).ToString("0.0") + " " + (slipData[3].load / 1000f).ToString("0.0");

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