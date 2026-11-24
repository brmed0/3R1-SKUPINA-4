using System;
using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// BMW E36 M3 (rear-wheel drive) built on WheelColliders, with an arcade-friendly drift layer on top.
///
/// REAL CAR: mass, weight distribution, wheelbase/track, tire sizes, torque curve (peak power and torque
/// match the published figures), real gearbox + final drive, 25% limited-slip differential, brake bias, drag.
///
/// PER-WHEEL PHYSICS: every wheel is its own object with its own corner mass, spring + damper (computed from
/// that corner's weight), wheel inertia, tire radius, measured load, load-sensitive grip, slip, brake torque,
/// drive torque (from the differential) and Ackermann steering angle.
///
/// DRIVETRAIN: engine torque curve -> gearbox (auto or manual) -> final drive -> LSD -> the two REAR wheels.
///
/// DRIFT LAYER (unchanged idea): the slip angle drives everything. The only grip value changed at runtime is
/// the rear sideways grip: handbrake / power oversteer / feint loosen it, counter-steer assist holds and catches,
/// the max drift angle blends grip back in, and a small forward push keeps speed in a drift.
///
/// Setup: put this on the car root (needs a Rigidbody), assign the 4 WheelColliders (+ optional meshes).
/// Input: works with BOTH the old Input Manager and the new Input System package (chosen automatically from
/// Project Settings > Player > Active Input Handling). WASD / arrows / gamepad.
///
/// FIX NOTES (car did not move):
///  - Input.GetAxis threw an exception every frame when the project uses the new Input System only,
///    so throttle stayed 0. Input is now read through whichever backend is active.
///  - Startup checks fix a kinematic / frozen Rigidbody, high damping, sleeping body and rotated WheelColliders
///    (a collider whose forward axis is not the car's forward axis drives the car sideways or not at all).
///  - ROOT CAUSE of "wheels spin, car stays put": the spring targetPosition was computed in the wrong direction.
///    In Unity 0 = fully extended and 1 = fully compressed. The old value (about 0.97) made the springs push back
///    only when the suspension was already bottomed out, so the car rested on its body and each tire carried ~0 N.
///    The new "rideCompression" setting says how far the springs sink under the car's weight, and the target
///    is derived from it.
///  - A "stuck detector" prints the exact state to the Console if you hold the throttle and nothing moves.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class WheelControll : MonoBehaviour
{
    // =====================================================================
    //  BMW E36 M3 DATA
    //  Published / researched figures unless marked (estimate).
    //  Edit the numbers here to build your own variant.
    // =====================================================================

    public enum Variant
    {
        Euro_3_2_321PS = 0,   // 1995-99 S50B32, 6-speed Getrag
        Euro_3_0_286PS = 1,   // 1992-95 S50B30, 5-speed ZF
        US_3_2_240hp = 2      // 1996-99 S52B32, 5-speed ZF
    }

    private sealed class CarSpec
    {
        public string name;

        // Mass
        public float curbMassKg;          // ready to drive, without driver
        public float frontWeightShare;    // 0..1
        public float comHeight;           // m above ground (estimate)

        // Geometry (m)
        public float wheelbase, trackFront, trackRear;

        // Tires / wheels
        public float frontTireRadius, rearTireRadius;   // unloaded radius, m
        public float frontWheelMass, rearWheelMass;     // wheel + tire + share of brake disc, kg (estimate)

        // Engine
        public float[] rpm;               // torque curve points (rpm)
        public float[] torque;            // torque curve points (Nm), shaped to hit the published peak power/torque
        public float idleRpm, limiterRpm;

        // Gearbox
        public float[] gears;             // forward ratios
        public float reverseRatio;        // (approximate)
        public float finalDrive;
    }

    private static readonly CarSpec[] Specs =
    {
        // 0: Euro 3.2 - 321 PS (236 kW) @ 7400, 350 Nm @ 3250, Getrag 6-speed, 3.15 final drive
        new CarSpec
        {
            name = "BMW E36 M3 3.2 (Euro, 321 PS)",
            curbMassKg = 1460f, frontWeightShare = 0.497f, comHeight = 0.50f,
            wheelbase = 2.700f, trackFront = 1.422f, trackRear = 1.444f,
            frontTireRadius = 0.317f, rearTireRadius = 0.314f,       // 225/45 R17 front, 245/40 R17 rear
            frontWheelMass = 22f, rearWheelMass = 23f,
            rpm    = new[] { 800f, 1000f, 1500f, 2000f, 2500f, 3250f, 4000f, 5000f, 6000f, 6500f, 7000f, 7400f, 7600f },
            torque = new[] { 150f,  190f,  255f,  300f,  330f,  350f,  348f,  344f,  338f,  328f,  316f,  305f,  290f },
            idleRpm = 800f, limiterRpm = 7600f,
            gears = new[] { 4.23f, 2.51f, 1.67f, 1.23f, 1.00f, 0.83f },
            reverseRatio = 3.90f, finalDrive = 3.15f
        },
        // 1: Euro 3.0 - 286 PS (210 kW) @ 7000, 296 Nm @ 3600, ZF 5-speed, 3.15 final drive
        new CarSpec
        {
            name = "BMW E36 M3 3.0 (Euro, 286 PS)",
            curbMassKg = 1460f, frontWeightShare = 0.497f, comHeight = 0.50f,
            wheelbase = 2.700f, trackFront = 1.422f, trackRear = 1.444f,
            frontTireRadius = 0.310f, rearTireRadius = 0.310f,       // 235/40 R17 all round
            frontWheelMass = 21f, rearWheelMass = 21f,
            rpm    = new[] { 800f, 1000f, 1500f, 2000f, 2500f, 3600f, 4500f, 5500f, 6000f, 6500f, 7000f, 7250f, 7300f },
            torque = new[] { 130f,  165f,  225f,  260f,  280f,  296f,  295f,  293f,  291f,  289f,  286.5f, 270f, 262f },
            idleRpm = 800f, limiterRpm = 7300f,
            gears = new[] { 4.20f, 2.49f, 1.66f, 1.24f, 1.00f },
            reverseRatio = 3.90f, finalDrive = 3.15f
        },
        // 2: US 3.2 - 240 hp (179 kW) @ 6000, 320 Nm (236 lb-ft) @ 3800, ZF 5-speed, 3.23 final drive
        new CarSpec
        {
            name = "BMW E36 M3 3.2 (US, 240 hp)",
            curbMassKg = 1440f, frontWeightShare = 0.497f, comHeight = 0.50f,
            wheelbase = 2.700f, trackFront = 1.422f, trackRear = 1.444f,
            frontTireRadius = 0.317f, rearTireRadius = 0.314f,
            frontWheelMass = 22f, rearWheelMass = 23f,
            rpm    = new[] { 800f, 1000f, 1500f, 2000f, 2500f, 3800f, 4500f, 5000f, 5500f, 6000f, 6300f, 6500f },
            torque = new[] { 140f,  180f,  240f,  280f,  300f,  320f,  316f,  305f,  296f,  285f,  265f,  250f },
            idleRpm = 800f, limiterRpm = 6500f,
            gears = new[] { 4.20f, 2.49f, 1.66f, 1.24f, 1.00f },
            reverseRatio = 3.90f, finalDrive = 3.23f
        }
    };

    // =====================================================================
    //  DEFAULT TUNING VALUES
    //  Every Inspector default lives in this block. Change a number here and
    //  use "Reset" on the component (or add the script to a new car) to pick
    //  it up. Components already in a scene keep the values serialized there.
    // =====================================================================
    private static class Defaults
    {
        // Car model
        public const float ExtraMassKg = 75f;
        public const float FrontWeightShareOffset = 0f;
        public const float PowerMultiplier = 1f;
        public const float ComHeightOffset = 0f;
        public const bool UseSpecTireSize = true;
        public const bool UseRealInertia = true;

        // Suspension
        public const float SuspensionTravel = 0.30f;
        public const float FrontRideFreq = 1.4f;
        public const float RearRideFreq = 1.55f;
        public const float DampingRatio = 0.35f;
        public const float RideCompression = 0.60f;
        public const float AntiRollFront = 5000f;
        public const float AntiRollRear = 3500f;

        // Tires (stiffness multipliers; ~1.0 = road tire, higher = more grip)
        public const float TireGripFront = 1.05f;
        public const float TireGripRear = 1.10f;
        public const float TireGripForward = 1.10f;
        public const float TireLoadSensitivity = 0.15f;

        // Brakes
        public const float BrakeTorqueFront = 1700f;
        public const float BrakeTorqueRear = 750f;
        public const float HandbrakePower = 2400f;
        public const float BrakeBuildTime = 0.2f;

        // Steering
        public const float MaxSteer = 32f;
        public const float SteerSpeed = 160f;
        public const float HighSpeedSteer = 0.35f;
        public const float SteerReduceStartKmh = 30f;
        public const float Ackermann = 0.9f;

        // Drivetrain
        public const bool AutoGearbox = true;
        public const float ShiftTime = 0.2f;
        public const float LaunchRpm = 3500f;
        public const float DrivelineEfficiency = 0.90f;
        public const float EngineBraking = 1f;
        public const float SpeedLimiterKmh = 250f;
        public const float DiffLockPower = 0.25f;
        public const float DiffLockCoast = 0.25f;
        public const float DiffFullLockRadPerSec = 6f;

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
        public const float FeintSteerThreshold = 0.7f;
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
        public const float DriftSpeedBoost = 2.5f;
        public const float DriftBoostFullAngle = 30f;
        public const float DriftHoldGrip = 0.8f;

        // Stability / resistance
        public const float Downforce = 8f;
        public const float DragCd = 0.32f;
        public const float FrontalArea = 1.95f;
        public const float RollingResistanceCoeff = 0.013f;

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

    [Header("Wheel Colliders (front wheels must be ahead of the rear wheels)")]
    [SerializeField] private WheelCollider frontRight;
    [SerializeField] private WheelCollider frontLeft;
    [SerializeField] private WheelCollider backRight;
    [SerializeField] private WheelCollider backLeft;

    [Header("Wheel Meshes (optional)")]
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform backRightMesh;
    [SerializeField] private Transform backLeftMesh;

    [Header("Input (old Input Manager OR new Input System, picked automatically)")]
    [Tooltip("Handbrake key. WASD / arrows / left stick are always covered.")]
    [SerializeField] private KeyCode handbrakeKey = KeyCode.Space;
    [Tooltip("Gamepad handbrake button (JoystickButton1 = B on an Xbox pad). With the new Input System the East button is used.")]
    [SerializeField] private KeyCode handbrakePadButton = KeyCode.JoystickButton1;
    [Tooltip("Puts the car back on its wheels.")]
    [SerializeField] private KeyCode resetKey = KeyCode.R;
    [Tooltip("Manual upshift (also works with the automatic gearbox as an override).")]
    [SerializeField] private KeyCode shiftUpKey = KeyCode.E;
    [Tooltip("Manual downshift.")]
    [SerializeField] private KeyCode shiftDownKey = KeyCode.Q;

    [Header("Car Model (BMW E36 M3)")]
    [Tooltip("Which E36 M3 to build: engine, gearbox, final drive, tire size, curb weight.")]
    [SerializeField] private Variant variant = Variant.Euro_3_2_321PS;
    [Tooltip("Driver, passengers and cargo added to the curb weight (kg). The EU weight figure includes a 75 kg driver.")]
    [SerializeField, Range(0f, 400f)] private float extraMassKg = Defaults.ExtraMassKg;
    [Tooltip("Added to the real front weight share (49.7%). +0.03 = nose-heavy, -0.03 = tail-heavy.")]
    [SerializeField, Range(-0.1f, 0.1f)] private float frontWeightShareOffset = Defaults.FrontWeightShareOffset;
    [Tooltip("Scales the whole torque curve. 1 = stock.")]
    [SerializeField, Range(0.3f, 2.5f)] private float powerMultiplier = Defaults.PowerMultiplier;
    [Tooltip("Moves the center of mass up (+) or down (-) from the real height (about 0.50 m). Lower = harder to flip.")]
    [SerializeField, Range(-0.25f, 0.25f)] private float comHeightOffset = Defaults.ComHeightOffset;
    [Tooltip("ON: the WheelCollider radii are set to the real E36 tire sizes (resize your wheel meshes to match). OFF: keep your own radii.")]
    [SerializeField] private bool useSpecTireSize = Defaults.UseSpecTireSize;
    [Tooltip("ON: realistic yaw / pitch / roll inertia instead of whatever your body collider implies.")]
    [SerializeField] private bool useRealInertia = Defaults.UseRealInertia;

    [Header("Suspension (computed per corner from that corner's weight)")]
    [SerializeField, Range(0.15f, 0.5f)] private float suspensionTravel = Defaults.SuspensionTravel;
    [Tooltip("How far the springs sink under the car's own weight, as a fraction of the suspension travel (0..1, where 1 = bottomed out). " +
             "Lower = the car sits higher. 0.6 is a good start. If the car body scrapes the ground, lower this value.")]
    [SerializeField, Range(0.3f, 0.9f)] private float rideCompression = Defaults.RideCompression;
    [Tooltip("Front ride frequency in Hz. Higher = stiffer springs.")]
    [SerializeField, Range(0.8f, 3f)] private float frontRideFreq = Defaults.FrontRideFreq;
    [Tooltip("Rear ride frequency in Hz.")]
    [SerializeField, Range(0.8f, 3f)] private float rearRideFreq = Defaults.RearRideFreq;
    [Tooltip("Damper strength as a fraction of critical damping.")]
    [SerializeField, Range(0.1f, 1.2f)] private float dampingRatio = Defaults.DampingRatio;
    [Tooltip("Front anti-roll bar stiffness. Higher = less body roll, more understeer.")]
    [SerializeField, Range(0f, 20000f)] private float antiRollFront = Defaults.AntiRollFront;
    [Tooltip("Rear anti-roll bar stiffness. Higher = less body roll, more oversteer.")]
    [SerializeField, Range(0f, 20000f)] private float antiRollRear = Defaults.AntiRollRear;

    [Header("Tires (per wheel; load-sensitive)")]
    [Tooltip("Front sideways grip. About 1.0 = road tire. Raise to 1.3-1.6 for arcade grip.")]
    [SerializeField, Range(0.5f, 3f)] private float tireGripFront = Defaults.TireGripFront;
    [Tooltip("Rear sideways grip. Everything drift-related scales this value.")]
    [SerializeField, Range(0.5f, 3f)] private float tireGripRear = Defaults.TireGripRear;
    [Tooltip("Grip in the rolling direction (traction and braking).")]
    [SerializeField, Range(0.5f, 3f)] private float tireGripForward = Defaults.TireGripForward;
    [Tooltip("How much grip per newton drops as a wheel gets more load. 0 = none. Higher = outer wheel loses more grip in corners.")]
    [SerializeField, Range(0f, 0.5f)] private float tireLoadSensitivity = Defaults.TireLoadSensitivity;

    [Header("Brakes")]
    [Tooltip("Brake torque per front wheel (front-biased, like the real car).")]
    [SerializeField, Range(0f, 4000f)] private float brakeTorqueFront = Defaults.BrakeTorqueFront;
    [Tooltip("Brake torque per rear wheel.")]
    [SerializeField, Range(0f, 4000f)] private float brakeTorqueRear = Defaults.BrakeTorqueRear;
    [Tooltip("Brake torque on the rear wheels while the handbrake is held.")]
    [SerializeField, Range(0f, 6000f)] private float handbrakePower = Defaults.HandbrakePower;
    [Tooltip("Seconds for the brakes to build from 0 to full after you press brake, and again after landing.")]
    [SerializeField, Range(0.02f, 1f)] private float brakeBuildTime = Defaults.BrakeBuildTime;

    [Header("Steering")]
    [Tooltip("Average front wheel angle at full lock, in degrees. The inner wheel turns more (Ackermann).")]
    [SerializeField, Range(10f, 45f)] private float maxSteer = Defaults.MaxSteer;
    [Tooltip("How fast the wheels turn, in degrees per second.")]
    [SerializeField, Range(30f, 400f)] private float steerSpeed = Defaults.SteerSpeed;
    [Tooltip("Steering left at top speed, as a fraction of maxSteer. Lower = calmer at speed.")]
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteer = Defaults.HighSpeedSteer;
    [Tooltip("Speed where steering starts to reduce.")]
    [SerializeField, Range(0f, 100f)] private float steerReduceStartKmh = Defaults.SteerReduceStartKmh;
    [Tooltip("0 = both front wheels parallel, 1 = full Ackermann (inner wheel turns more than the outer).")]
    [SerializeField, Range(0f, 1f)] private float ackermann = Defaults.Ackermann;

    [Header("Drivetrain (RWD: engine -> gearbox -> final drive -> LSD -> rear wheels)")]
    [SerializeField] private bool autoGearbox = Defaults.AutoGearbox;
    [Tooltip("Seconds of torque cut during a gear change.")]
    [SerializeField, Range(0.05f, 0.6f)] private float shiftTime = Defaults.ShiftTime;
    [Tooltip("Engine rpm held while the clutch slips in 1st / reverse at full throttle.")]
    [SerializeField, Range(1500f, 5500f)] private float launchRpm = Defaults.LaunchRpm;
    [Tooltip("Fraction of engine torque that reaches the wheels (gearbox, propshaft, differential losses).")]
    [SerializeField, Range(0.7f, 1f)] private float drivelineEfficiency = Defaults.DrivelineEfficiency;
    [Tooltip("Engine braking strength when you lift off the throttle. 0 = freewheel.")]
    [SerializeField, Range(0f, 2f)] private float engineBraking = Defaults.EngineBraking;
    [Tooltip("Electronic speed limiter. The real Euro E36 M3 is limited to 250 km/h. 0 = no limiter.")]
    [SerializeField, Range(0f, 400f)] private float speedLimiterKmh = Defaults.SpeedLimiterKmh;
    [Tooltip("Limited-slip differential lock under power. 0 = open diff, 0.25 = the real M3 (25%).")]
    [SerializeField, Range(0f, 1f)] private float diffLockPower = Defaults.DiffLockPower;
    [Tooltip("Limited-slip differential lock when off the throttle.")]
    [SerializeField, Range(0f, 1f)] private float diffLockCoast = Defaults.DiffLockCoast;
    [Tooltip("Speed difference between the rear wheels (rad/s) at which the differential reaches its full lock.")]
    [SerializeField, Range(1f, 30f)] private float diffFullLockRadPerSec = Defaults.DiffFullLockRadPerSec;

    [Header("Drift Entry: Handbrake & Grip Transitions")]
    [Tooltip("Rear grip multiplier while the handbrake is held (lower = slides more).")]
    [SerializeField, Range(0.1f, 1f)] private float handbrakeGrip = Defaults.HandbrakeGrip;
    [Tooltip("How fast rear grip is lost (grip factor per second). High = the slide starts immediately.")]
    [SerializeField, Range(1f, 40f)] private float gripLossRate = Defaults.GripLossRate;
    [Tooltip("How fast rear grip comes back (grip factor per second). Low = softer, no snap-back.")]
    [SerializeField, Range(0.2f, 8f)] private float gripRecovery = Defaults.GripRecovery;

    [Header("Drift Entry: Power Oversteer")]
    [Tooltip("How much throttle + steering at speed loosens the rear on top of the real wheelspin. 0 = off.")]
    [SerializeField, Range(0f, 0.8f)] private float powerOversteer = Defaults.PowerOversteer;
    [Tooltip("Power oversteer fades in from this speed and reaches full strength at 1.5x this speed.")]
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

    [Header("Stability & Resistance")]
    [Tooltip("Downward force per m/s of speed. The real E36 M3 makes almost none; a little helps stability in Unity.")]
    [SerializeField, Range(0f, 150f)] private float downforce = Defaults.Downforce;
    [Tooltip("Aerodynamic drag coefficient (E36 M3: about 0.32).")]
    [SerializeField, Range(0.2f, 0.6f)] private float dragCd = Defaults.DragCd;
    [Tooltip("Frontal area in m^2.")]
    [SerializeField, Range(1.5f, 3f)] private float frontalArea = Defaults.FrontalArea;
    [Tooltip("Rolling resistance coefficient of the tires.")]
    [SerializeField, Range(0.005f, 0.05f)] private float rollingResistanceCoeff = Defaults.RollingResistanceCoeff;

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

    /// <summary>Per-wheel info for smoke / skid marks / sound / telemetry.</summary>
    public struct WheelSlipData
    {
        public bool grounded;
        public float forwardSlip;
        public float sidewaysSlip;
        /// <summary>0 = gripping, 1 = fully sliding. Use this for smoke, skid marks and sound.</summary>
        public float slip01;
        /// <summary>Vertical load on this wheel in newtons (computed from the spring compression).</summary>
        public float load;
        /// <summary>Wheel rotation speed in rpm (signed).</summary>
        public float rpm;
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

    /// <summary>Engine speed in rpm (smoothed for gauges).</summary>
    public float EngineRpm => engineRpmDisplay;
    public float RedlineRpm => spec != null ? spec.limiterRpm : 0f;
    /// <summary>Current gear: -1 = reverse, 0 = neutral, 1..N = forward gears.</summary>
    public int Gear => gear;
    public float EngineTorqueNm => engineTorqueNm;
    public float EnginePowerKw => enginePowerKw;
    public float PeakPowerKw => peakPowerKw;
    public string CarName => spec != null ? spec.name : "";
    public float TotalMassKg => totalMass;
    /// <summary>Share of the car's weight on the front axle (0..1).</summary>
    public float FrontWeightShare => frontShare;

    // =====================================================================
    //  PRIVATE STATE
    // =====================================================================

    private const float RadPerRpm = 0.10471976f;     // 2*pi/60
    private const float MinRearGripFactor = 0.2f;    // floor so stacked effects can never remove all grip
    private const float SlipMinSpeed = 2f;           // m/s: below this the slip angle is meaningless
    private const float MinShiftInterval = 0.4f;
    private const float ShiftUpMarginRpm = 400f;     // auto upshift this far below the limiter at full throttle
    private const float LightThrottleUpshiftRpm = 4200f;
    private const float AutoDownshiftRpm = 2300f;
    private const float KickdownRpm = 3800f;
    private const float EngineBrakeBaseNm = 20f;     // crank-side engine friction: base + perRpm * rpm
    private const float EngineBrakePerRpm = 0.01f;
    private const float InertiaPitchRadius = 1.30f;  // radius of gyration (m): inertia = mass * radius^2
    private const float InertiaYawRadius = 1.28f;
    private const float InertiaRollRadius = 0.58f;
    private const float StuckReportDelay = 2f;       // seconds of throttle without movement before the Console report

    private sealed class WheelState
    {
        public WheelCollider col;
        public Transform mesh;
        public bool front;
        public bool right;
        public bool driven;
        public float cornerMass;        // kg carried by this corner (static)
        public float staticLoad;        // N at rest
        public float load;              // N measured this step
        public float rpm;
        public float spring;            // N/m of this corner's spring
        public float targetPos;         // Unity targetPosition (0 = extended, 1 = compressed)
        public float compression;       // 0..1 measured this step (1 = bottomed out)
        public Quaternion meshOffset = Quaternion.identity;
        public Vector3 meshCenterOffset;
    }

    private Rigidbody rb;
    private CarSpec spec;
    private WheelState[] ws;                        // FL, FR, RL, RR
    private readonly WheelSlipData[] slipData = new WheelSlipData[4];

    // geometry (measured from your WheelColliders)
    private float wheelbase, trackFront, trackRear, avgWheelLocalY, driveRadius;

    // mass
    private float totalMass, frontShare;
    private bool comDirty = true;

    // input
    private float throttleInput, steerInput;
    private bool handbrakeHeld;
    private bool shiftUpRequest, shiftDownRequest;
    private float appliedThrottle;                  // throttle after brake-first logic (negative = reverse)
    private float brakeApplied;                     // 0..1, how much brake is currently applied
    private int groundedWheels;                     // 0 = fully airborne

    // motion
    private float speedMS, forwardSpeedMS, slipAngle;
    private float steerAngle;

    // engine / gearbox
    private int gear = 1;
    private float shiftTimer, timeSinceShift;
    private float rawEngineRpm, engineRpmDisplay;
    private float engineTorqueNm, enginePowerKw;
    private bool limiterActive;
    private float peakPowerKw, peakPowerRpm;

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

    // diagnostics
    private float stuckTimer, noGroundTimer, bottomTimer;
    private bool stuckReported, noGroundReported, bottomReported, suspensionWarned;
    private Vector3 stuckAnchor;

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

        WheelCollider[] cols = { frontLeft, frontRight, backLeft, backRight };
        Transform[] meshArr = { frontLeftMesh, frontRightMesh, backLeftMesh, backRightMesh };
        ws = new WheelState[4];
        for (int i = 0; i < 4; i++)
        {
            ws[i] = new WheelState
            {
                col = cols[i],
                mesh = meshArr[i],
                front = i < 2,
                right = (i & 1) == 1,
                driven = i >= 2
            };
        }

        if (!WheelsAreValid() || !MeasureGeometry())
        {
            enabled = false;
            return;
        }

        CheckRigidbody();
        AlignWheelColliders();
        CacheInputKeys();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        ws[0].col.ConfigureVehicleSubsteps(5f, 12, 15);

        ApplyCarSetup();
        CacheMeshOffsets();
        gear = 1;
        LogCarSummary();
    }

    // Live tuning: changing mass / suspension / grip in the Inspector during Play takes effect.
    private void OnValidate()
    {
        driftExitAngle = Mathf.Min(driftExitAngle, driftEnterAngle);
        maxDriftAngle = Mathf.Max(maxDriftAngle, driftEnterAngle + 5f);
        skidFullSlip = Mathf.Max(skidFullSlip, skidSlipThreshold + 0.1f);

        if (Application.isPlaying && enabled && rb != null && ws != null)
            ApplyCarSetup();
    }

    private bool WheelsAreValid()
    {
        for (int i = 0; i < 4; i++)
        {
            if (ws[i].col == null)
            {
                Debug.LogError("[WheelControll] A WheelCollider slot is empty. Assign all 4 WheelColliders. The script is DISABLED until you do.", this);
                return false;
            }
            for (int j = i + 1; j < 4; j++)
            {
                if (ws[i].col == ws[j].col)
                {
                    Debug.LogError("[WheelControll] Two slots use the same WheelCollider. Check Front/Back Left and Right. The script is DISABLED until you fix it.", this);
                    return false;
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (!ws[i].col.enabled)
            {
                Debug.LogWarning("[WheelControll] WheelCollider " + ws[i].col.name + " was disabled. Enabled it.", this);
                ws[i].col.enabled = true;
            }
            if (ws[i].mesh == null)
                Debug.LogWarning("[WheelControll] Wheel mesh " + i + " is not assigned. That wheel will not be animated (optional).", this);
        }
        return true;
    }

    // Makes sure the Rigidbody can actually be driven. These are the usual "car just sits there" causes.
    private void CheckRigidbody()
    {
        if (rb.isKinematic)
        {
            Debug.LogWarning("[WheelControll] The Rigidbody had 'Is Kinematic' ON, so physics could not move the car. Turned it OFF.", this);
            rb.isKinematic = false;
        }
        if (!rb.useGravity)
        {
            Debug.LogWarning("[WheelControll] The Rigidbody had 'Use Gravity' OFF. Turned it ON.", this);
            rb.useGravity = true;
        }
        if (rb.constraints != RigidbodyConstraints.None)
        {
            Debug.LogWarning("[WheelControll] The Rigidbody had Constraints (frozen position/rotation). Cleared them.", this);
            rb.constraints = RigidbodyConstraints.None;
        }

#if UNITY_6000_0_OR_NEWER
        if (rb.linearDamping > 0.1f)
        {
            Debug.LogWarning("[WheelControll] Rigidbody Linear Damping was " + rb.linearDamping.ToString("0.00") + " (the script applies its own drag). Set to 0.", this);
            rb.linearDamping = 0f;
        }
        if (rb.angularDamping > 0.5f)
        {
            Debug.LogWarning("[WheelControll] Rigidbody Angular Damping was " + rb.angularDamping.ToString("0.00") + ". Set to 0.05.", this);
            rb.angularDamping = 0.05f;
        }
#else
        if (rb.drag > 0.1f)
        {
            Debug.LogWarning("[WheelControll] Rigidbody Drag was " + rb.drag.ToString("0.00") + " (the script applies its own drag). Set to 0.", this);
            rb.drag = 0f;
        }
        if (rb.angularDrag > 0.5f)
        {
            Debug.LogWarning("[WheelControll] Rigidbody Angular Drag was " + rb.angularDrag.ToString("0.00") + ". Set to 0.05.", this);
            rb.angularDrag = 0.05f;
        }
#endif

        // A resting Rigidbody can fall asleep and ignore wheel torque. Never let a car sleep.
        rb.sleepThreshold = 0f;

        Vector3 s = transform.lossyScale;
        if (Mathf.Abs(s.x - 1f) > 0.05f || Mathf.Abs(s.y - 1f) > 0.05f || Mathf.Abs(s.z - 1f) > 0.05f)
            Debug.LogWarning("[WheelControll] The car root has scale " + s + " (should be 1,1,1). WheelCollider radius, suspension and mass " +
                             "are affected by scale; put the scale on the mesh child instead.", this);
    }

    // A WheelCollider drives along ITS OWN forward (Z) axis and steers around ITS OWN up (Y) axis.
    // If the collider object is rotated (common with imported wheel pivots) the car drives sideways, backwards,
    // or not at all (forward pointing up). Align every collider to the car root. The mesh keeps its visual pose.
    private void AlignWheelColliders()
    {
        for (int i = 0; i < 4; i++)
        {
            Transform t = ws[i].col.transform;
            if (Quaternion.Angle(t.rotation, transform.rotation) < 2f) continue;

            Debug.LogWarning("[WheelControll] WheelCollider '" + t.name + "' was rotated " +
                             Quaternion.Angle(t.rotation, transform.rotation).ToString("0") +
                             " deg away from the car's axes, so the car could not drive forward. Aligned it to the car root " +
                             "(wheel mesh keeps its look).", this);

            Transform mesh = ws[i].mesh;
            bool meshIsChild = mesh != null && mesh != t && mesh.IsChildOf(t);
            Vector3 meshPos = Vector3.zero;
            Quaternion meshRot = Quaternion.identity;
            if (meshIsChild)
            {
                meshPos = mesh.position;
                meshRot = mesh.rotation;
            }

            t.rotation = transform.rotation;

            if (meshIsChild) mesh.SetPositionAndRotation(meshPos, meshRot);
        }
    }

    // Reads the real geometry from your WheelColliders and compares it with the E36 M3.
    private bool MeasureGeometry()
    {
        Vector3 fl = ws[FrontLeft].col.transform.position;
        Vector3 fr = ws[FrontRight].col.transform.position;
        Vector3 rl = ws[RearLeft].col.transform.position;
        Vector3 rr = ws[RearRight].col.transform.position;

        wheelbase = Vector3.Dot((fl + fr) * 0.5f - (rl + rr) * 0.5f, transform.forward);
        trackFront = Mathf.Abs(Vector3.Dot(fl - fr, transform.right));
        trackRear = Mathf.Abs(Vector3.Dot(rl - rr, transform.right));

        if (wheelbase < 0.5f)
        {
            Debug.LogError("[WheelControll] The front WheelColliders must be ahead of the rear ones along the car's forward (+Z) axis. " +
                           "Check the Front/Back assignments and that the car root's blue arrow points to the front. The script is DISABLED.", this);
            return false;
        }
        if (trackFront < 0.3f || trackRear < 0.3f)
        {
            Debug.LogError("[WheelControll] Left and right WheelColliders are on top of each other. Check the Left/Right assignments. The script is DISABLED.", this);
            return false;
        }

        CarSpec s = Specs[(int)variant];
        WarnIfFar("wheelbase", wheelbase, s.wheelbase);
        WarnIfFar("front track", trackFront, s.trackFront);
        WarnIfFar("rear track", trackRear, s.trackRear);
        return true;
    }

    private void WarnIfFar(string what, float actual, float expected)
    {
        if (Mathf.Abs(actual - expected) / expected > 0.08f)
            Debug.LogWarning("[WheelControll] Your " + what + " is " + actual.ToString("0.00") + " m but the E36 M3 is " +
                             expected.ToString("0.00") + " m. Physics uses your model's real positions; scale the model if you want exact E36 proportions.", this);
    }

    // Mass, weight distribution, inertia, per-wheel suspension and tires. Safe to call again at runtime.
    private void ApplyCarSetup()
    {
        spec = Specs[(int)variant];
        frontShare = Mathf.Clamp(spec.frontWeightShare + frontWeightShareOffset, 0.35f, 0.65f);
        totalMass = spec.curbMassKg + extraMassKg;
        rb.mass = totalMass;

        if (useRealInertia)
        {
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.inertiaTensor = new Vector3(
                totalMass * InertiaPitchRadius * InertiaPitchRadius,   // X: pitch
                totalMass * InertiaYawRadius * InertiaYawRadius,       // Y: yaw
                totalMass * InertiaRollRadius * InertiaRollRadius);    // Z: roll
        }
        else
        {
            rb.ResetInertiaTensor();
        }

        // Per-wheel setup.
        float g = Physics.gravity.magnitude;
        float frontCorner = totalMass * frontShare * 0.5f;
        float rearCorner = totalMass * (1f - frontShare) * 0.5f;
        float radiusSum = 0f;
        avgWheelLocalY = 0f;

        for (int i = 0; i < 4; i++)
        {
            WheelState w = ws[i];
            float wheelMass = w.front ? spec.frontWheelMass : spec.rearWheelMass;
            w.cornerMass = w.front ? frontCorner : rearCorner;
            w.staticLoad = w.cornerMass * g;

            // Spring and damper from this corner's own sprung weight and the ride frequency.
            float sprung = Mathf.Max(w.cornerMass - wheelMass, 50f);
            float omega = 2f * Mathf.PI * (w.front ? frontRideFreq : rearRideFreq);
            float spring = sprung * omega * omega;
            float damper = 2f * dampingRatio * Mathf.Sqrt(spring * sprung);
            float staticDeflection = sprung * g / spring;
            // Unity's targetPosition: 0 = fully extended, 1 = fully compressed. It is the point where the wheel would
            // rest with NO load. Under the car's weight the spring sinks a further staticDeflection below it, so
            //   rideCompression = target + staticDeflection / travel   =>   target = rideCompression - staticDeflection / travel
            float target = Mathf.Clamp01(rideCompression - staticDeflection / suspensionTravel);
            w.spring = spring;
            w.targetPos = target;

            float restCompression = target + staticDeflection / suspensionTravel;
            if (restCompression > 0.9f && !suspensionWarned)
            {
                suspensionWarned = true;
                Debug.LogWarning("[WheelControll] With this suspension travel (" + suspensionTravel.ToString("0.00") + " m) and ride frequency the springs " +
                                 "would sink to " + (restCompression * 100f).ToString("0") + "% of their travel under the car's weight. Increase " +
                                 "Suspension Travel or the ride frequencies, otherwise the suspension bottoms out.", this);
            }

            w.col.mass = wheelMass;
            if (useSpecTireSize) w.col.radius = w.front ? spec.frontTireRadius : spec.rearTireRadius;
            w.col.suspensionDistance = suspensionTravel;
            w.col.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = target };

            radiusSum += w.col.radius;
            avgWheelLocalY += transform.InverseTransformPoint(w.col.transform.position).y;
        }
        avgWheelLocalY *= 0.25f;
        driveRadius = (ws[RearLeft].col.radius + ws[RearRight].col.radius) * 0.5f;

        SetupTireCurves();

        // Center of mass: along the car from the weight distribution, height from the spec.
        // (Height is refined once all four wheels touch the ground, see UpdateCenterOfMass.)
        Vector3 frontMid = (ws[FrontLeft].col.transform.position + ws[FrontRight].col.transform.position) * 0.5f;
        Vector3 rearMid = (ws[RearLeft].col.transform.position + ws[RearRight].col.transform.position) * 0.5f;
        Vector3 comWorld = Vector3.Lerp(frontMid, rearMid, 1f - frontShare);
        Vector3 comLocal = transform.InverseTransformPoint(comWorld);
        float groundGuess = avgWheelLocalY - suspensionTravel * 0.5f - radiusSum * 0.25f;
        comLocal.y = groundGuess + spec.comHeight + comHeightOffset;
        rb.centerOfMass = comLocal;
        comDirty = true;

        ComputePeakPower();
    }

    // Tire friction curves. Stiffness is then updated every physics step per wheel.
    private void SetupTireCurves()
    {
        for (int i = 0; i < 4; i++)
        {
            ws[i].col.forwardFriction = MakeCurve(0.4f, 1f, 0.8f, 0.85f, tireGripForward);
            ws[i].col.sidewaysFriction = MakeCurve(0.25f, 1f, 0.6f, 0.8f, ws[i].front ? tireGripFront : tireGripRear);
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

    private void ComputePeakPower()
    {
        peakPowerKw = 0f;
        peakPowerRpm = 0f;
        for (float r = 500f; r <= spec.limiterRpm; r += 25f)
        {
            float kw = TorqueAt(r) * powerMultiplier * r * RadPerRpm * 0.001f;
            if (kw > peakPowerKw)
            {
                peakPowerKw = kw;
                peakPowerRpm = r;
            }
        }
    }

    private void LogCarSummary()
    {
        Debug.Log("[WheelControll] " + spec.name + ": " + totalMass.ToString("0") + " kg, " +
                  (frontShare * 100f).ToString("0.0") + "/" + ((1f - frontShare) * 100f).ToString("0.0") + " front/rear, " +
                  "wheelbase " + wheelbase.ToString("0.00") + " m, peak " + peakPowerKw.ToString("0") + " kW (" +
                  (peakPowerKw * 1.35962f).ToString("0") + " PS) @ " + peakPowerRpm.ToString("0") + " rpm. Input: " + InputModeName + ".", this);
    }

    // Remembers each mesh's rotation and visual center, so the wheel
    // lines up with its collider even if the model's pivot is off to the side.
    private void CacheMeshOffsets()
    {
        for (int i = 0; i < 4; i++)
        {
            WheelState w = ws[i];
            w.meshOffset = Quaternion.identity;
            w.meshCenterOffset = Vector3.zero;
            if (w.mesh == null) continue;

            w.meshOffset = Quaternion.Inverse(w.col.transform.rotation) * w.mesh.rotation;

            Renderer[] renderers = w.mesh.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            w.meshCenterOffset = Quaternion.Inverse(w.mesh.rotation) * (bounds.center - w.mesh.position);
        }
    }

    // =====================================================================
    //  INPUT (Update)
    //  Works with the old Input Manager and the new Input System package.
    //  Legacy is used whenever it is enabled ("Both" mode too), otherwise the new system.
    // =====================================================================

    private void Update()
    {
        ReadInput();
        DetectFeint();
    }

#if ENABLE_LEGACY_INPUT_MANAGER

    private const string InputModeName = "old Input Manager";
    private bool legacyAxesBroken;

    private void CacheInputKeys() { }

    private void ReadInput()
    {
        float v = 0f, h = 0f;

        if (!legacyAxesBroken)
        {
            try
            {
                v = Input.GetAxis("Vertical");
                h = Input.GetAxis("Horizontal");
            }
            catch (ArgumentException)
            {
                legacyAxesBroken = true;
                Debug.LogError("[WheelControll] The Input Manager axes 'Vertical' / 'Horizontal' do not exist " +
                               "(Edit > Project Settings > Input Manager). Falling back to raw WASD / arrow keys.", this);
            }
        }
        if (legacyAxesBroken)
        {
            v = KeyAxis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow);
            h = KeyAxis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow);
        }

        throttleInput = Mathf.Clamp(v, -1f, 1f);
        steerInput = Mathf.Clamp(h, -1f, 1f);
        handbrakeHeld = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakePadButton);

        if (Input.GetKeyDown(shiftUpKey)) shiftUpRequest = true;
        if (Input.GetKeyDown(shiftDownKey)) shiftDownRequest = true;
        if (Input.GetKeyDown(resetKey)) ResetUpright();
        if (Input.GetKeyDown(debugToggleKey)) showDebug = !showDebug;
    }

    private static float KeyAxis(KeyCode plus, KeyCode plusAlt, KeyCode minus, KeyCode minusAlt)
    {
        float a = 0f;
        if (Input.GetKey(plus) || Input.GetKey(plusAlt)) a += 1f;
        if (Input.GetKey(minus) || Input.GetKey(minusAlt)) a -= 1f;
        return a;
    }

#elif ENABLE_INPUT_SYSTEM

    private const string InputModeName = "new Input System";
    private const float InputSmoothing = 6f;          // units per second (about 0.17 s from 0 to full)

    private float smoothThrottle, smoothSteer;
    private Key keyHandbrake, keyReset, keyShiftUp, keyShiftDown, keyDebug;

    private void CacheInputKeys()
    {
        keyHandbrake = ToKey(handbrakeKey);
        keyReset = ToKey(resetKey);
        keyShiftUp = ToKey(shiftUpKey);
        keyShiftDown = ToKey(shiftDownKey);
        keyDebug = ToKey(debugToggleKey);
    }

    // Converts the Inspector KeyCode to the new system's Key (letters, Space, F-keys, Shift/Ctrl, arrows ...).
    private static Key ToKey(KeyCode code)
    {
        string n = code.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        else if (n.StartsWith("Keypad")) n = "Numpad" + n.Substring(6);
        else if (n == "Return") n = "Enter";
        return Enum.TryParse(n, true, out Key parsed) ? parsed : Key.None;
    }

    private static bool Held(Keyboard kb, Key key)
    {
        return kb != null && key != Key.None && kb[key].isPressed;
    }

    private static bool Pressed(Keyboard kb, Key key)
    {
        return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
    }

    private void ReadInput()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;

        float v = 0f, h = 0f;
        bool handbrake = Held(kb, keyHandbrake);
        bool up = Pressed(kb, keyShiftUp);
        bool down = Pressed(kb, keyShiftDown);
        bool reset = Pressed(kb, keyReset);
        bool debugToggle = Pressed(kb, keyDebug);

        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        }

        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            h += stick.x;
            v += stick.y + pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            handbrake |= pad.buttonEast.isPressed;
            up |= pad.rightShoulder.wasPressedThisFrame;
            down |= pad.leftShoulder.wasPressedThisFrame;
            reset |= pad.buttonNorth.wasPressedThisFrame;
        }

        v = Mathf.Clamp(v, -1f, 1f);
        h = Mathf.Clamp(h, -1f, 1f);

        // Smoothing similar to the old GetAxis, so keyboard steering does not jump from 0 to 1.
        smoothThrottle = Mathf.MoveTowards(smoothThrottle, v, InputSmoothing * Time.deltaTime);
        smoothSteer = Mathf.MoveTowards(smoothSteer, h, InputSmoothing * Time.deltaTime);

        throttleInput = smoothThrottle;
        steerInput = smoothSteer;
        handbrakeHeld = handbrake;

        if (up) shiftUpRequest = true;
        if (down) shiftDownRequest = true;
        if (reset) ResetUpright();
        if (debugToggle) showDebug = !showDebug;
    }

#else

    private const string InputModeName = "NONE (enable an input backend in Player Settings > Active Input Handling)";

    private void CacheInputKeys() { }

    private void ReadInput() { }

#endif

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

        // A sleeping Rigidbody can ignore wheel torque: wake it as soon as the driver does something.
        if ((Mathf.Abs(throttleInput) > 0.05f || handbrakeHeld) && rb.IsSleeping()) rb.WakeUp();

        UpdateMotion();                  // speed + slip angle (the core value)
        UpdateWheelStates(dt);           // per-wheel load, rpm, slip; events; squeal
        UpdateDriftState(dt);            // IsDrifting, duration, events
        UpdateGearbox(dt);               // auto / manual gears, reverse
        ApplyDrivetrainAndBrakes(dt);    // engine -> gearbox -> LSD -> rear wheels, per-wheel brakes
        ApplySteering(dt);               // speed-sensitive + counter-steer + Ackermann per wheel
        UpdateTireGrip(dt);              // per-wheel load-sensitive grip + drift model for the rear
        ApplyDriftForces();              // speed retention, spin guard, feint kick
        ApplyResistanceAndStability();   // drag, rolling resistance, downforce, anti-roll bars
        UpdateCenterOfMass();            // sets the real COM height once the car has settled
        DiagnoseProblems(dt);            // Console report if the car does not move
    }

    // Prints what is going on if the throttle is held but the car does not move, or the wheels never touch the ground.
    private void DiagnoseProblems(float dt)
    {
        if (!noGroundReported)
        {
            noGroundTimer = groundedWheels == 0 ? noGroundTimer + dt : 0f;
            if (noGroundTimer > 3f)
            {
                noGroundReported = true;
                Debug.LogWarning("[WheelControll] No wheel has touched the ground for 3 seconds. Make sure the ground has a Collider, " +
                                 "the car starts above it (not inside it) and the WheelColliders' suspension reaches the ground.", this);
            }
        }

        // Suspension fully compressed while standing: the springs do not carry the car, so the tires get no load
        // and just spin. (This was the original bug.)
        if (!bottomReported)
        {
            float meanCompression = 0f;
            for (int i = 0; i < 4; i++) meanCompression += ws[i].compression;
            meanCompression *= 0.25f;

            bool bottomed = groundedWheels == 4 && meanCompression > 0.93f && speedMS < 1f;
            bottomTimer = bottomed ? bottomTimer + dt : 0f;
            if (bottomTimer > 1f)
            {
                bottomReported = true;
                Debug.LogWarning("[WheelControll] The suspension is fully compressed (" + (meanCompression * 100f).ToString("0") + "%) while the car " +
                                 "stands still, so the springs are not carrying the car and the tires get almost no load. Lower 'Ride Compression', " +
                                 "increase 'Suspension Travel', or check that the body collider is not resting on the ground.", this);
            }
        }

        if (stuckReported) return;

        // Throttle held but the car does not change position. Measured as displacement (not speed), so jitter cannot hide it.
        bool wantsToMove = Mathf.Abs(throttleInput) > 0.5f;
        if (!wantsToMove || (rb.position - stuckAnchor).sqrMagnitude > 1f)
        {
            stuckAnchor = rb.position;
            stuckTimer = 0f;
        }
        else
        {
            stuckTimer += dt;
        }
        if (stuckTimer < StuckReportDelay) return;

        stuckReported = true;
        Debug.LogWarning("[WheelControll] Throttle is held but the car does not move. State: input=" + InputModeName +
                         ", throttle=" + throttleInput.ToString("0.00") + ", grounded wheels=" + groundedWheels + "/4, gear=" + GearLabel() +
                         ", engine torque=" + engineTorqueNm.ToString("0") + " Nm, motor torque RL/RR=" +
                         ws[RearLeft].col.motorTorque.ToString("0") + "/" + ws[RearRight].col.motorTorque.ToString("0") +
                         " Nm, brake RL/RR=" + ws[RearLeft].col.brakeTorque.ToString("0") + "/" + ws[RearRight].col.brakeTorque.ToString("0") +
                         " Nm, handbrake=" + handbrakeHeld + ", isKinematic=" + rb.isKinematic + ", mass=" + rb.mass.ToString("0") +
                         ". If torque is high and wheels are grounded, check that the car body collider is not touching the ground " +
                         "and that the ground collider is not a trigger.", this);
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

    // --- per-wheel state --------------------------------------------------
    private void UpdateWheelStates(float dt)
    {
        float maxSlip = 0f;
        groundedWheels = 0;

        for (int i = 0; i < 4; i++)
        {
            WheelState w = ws[i];
            WheelSlipData d = default;

            w.rpm = w.col.rpm;
            d.rpm = w.rpm;

            if (w.col.GetGroundHit(out WheelHit hit))
            {
                d.grounded = true;
                groundedWheels++;

                // Suspension compression 0..1 (1 = bottomed out), measured from the contact point.
                float extension = (-w.col.transform.InverseTransformPoint(hit.point).y - w.col.radius) / w.col.suspensionDistance;
                w.compression = Mathf.Clamp01(1f - extension);

                // Vertical load from the spring: force = k x (compression beyond the rest point). hit.force is not a
                // dependable "load" value, so it is not used for this.
                w.load = Mathf.Max(0f, w.spring * (w.compression - w.targetPos) * w.col.suspensionDistance);
                d.load = w.load;
                d.forwardSlip = hit.forwardSlip;
                d.sidewaysSlip = hit.sidewaysSlip;

                float magnitude = Mathf.Sqrt(hit.forwardSlip * hit.forwardSlip + hit.sidewaysSlip * hit.sidewaysSlip);
                d.slip01 = Mathf.InverseLerp(skidSlipThreshold, skidFullSlip, magnitude);
                d.point = hit.point;
                d.normal = hit.normal;

                maxSlip = Mathf.Max(maxSlip, d.slip01);
            }
            else
            {
                w.load = 0f;
                w.compression = 0f;
            }

            slipData[i] = d;
            OnWheelSlip?.Invoke(i, d);
        }

        // Squeal follows the worst wheel; silent when (almost) parked.
        float movingGate = Mathf.InverseLerp(0.5f, 3f, speedMS);
        float smoothing = 1f - Mathf.Exp(-squealSmoothing * dt);
        squealVolume = Mathf.Lerp(squealVolume, maxSlip * movingGate, smoothing);
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

    // --- gearbox ----------------------------------------------------------
    private float GearRatio(int g)
    {
        if (g == -1) return spec.reverseRatio;
        if (g >= 1 && g <= spec.gears.Length) return spec.gears[g - 1];
        return 0f;
    }

    // Overall ratio crank -> wheels (gear x final drive). 0 in neutral.
    private float CurrentTotalRatio()
    {
        return GearRatio(gear) * spec.finalDrive;
    }

    // Engine rpm that the current ground speed implies in a given gear (ignores wheelspin).
    private float SpeedRpmInGear(int g)
    {
        return Mathf.Abs(forwardSpeedMS) / driveRadius * (1f / RadPerRpm) * GearRatio(g) * spec.finalDrive;
    }

    private void ShiftTo(int newGear)
    {
        gear = newGear;
        shiftTimer = shiftTime;       // torque cut while the gear changes
        timeSinceShift = 0f;
    }

    private void UpdateGearbox(float dt)
    {
        timeSinceShift += dt;
        if (shiftTimer > 0f) shiftTimer -= dt;

        int top = spec.gears.Length;

        // Reverse / drive selection, like a driver pressing S or W at a standstill.
        if (gear >= 1 && throttleInput < -0.1f && forwardSpeedMS < 1.5f)
        {
            gear = -1;
            shiftTimer = 0f;
            timeSinceShift = 0f;
        }
        else if (gear <= 0 && throttleInput > 0.1f && forwardSpeedMS > -1.5f)
        {
            gear = 1;
            shiftTimer = 0f;
            timeSinceShift = 0f;
        }

        if (gear >= 1)
        {
            bool canShift = shiftTimer <= 0f && timeSinceShift > MinShiftInterval;
            bool shifted = false;

            // Manual shifts (always available).
            if (canShift)
            {
                if (shiftUpRequest && gear < top) { ShiftTo(gear + 1); shifted = true; }
                else if (shiftDownRequest && gear > 1) { ShiftTo(gear - 1); shifted = true; }
            }

            // Automatic shifts use the rpm implied by ground speed, so wheelspin never causes an upshift.
            if (!shifted && autoGearbox && canShift)
            {
                float speedRpm = SpeedRpmInGear(gear);
                float absThrottle = Mathf.Abs(throttleInput);
                float shiftUpRpm = spec.limiterRpm - ShiftUpMarginRpm;
                float upThreshold = Mathf.Lerp(LightThrottleUpshiftRpm, shiftUpRpm, Mathf.InverseLerp(0.2f, 0.9f, absThrottle));

                if (gear < top && throttleInput > 0.1f && speedRpm > upThreshold)
                {
                    ShiftTo(gear + 1);
                }
                else if (gear > 1)
                {
                    float lowerRpm = speedRpm * GearRatio(gear - 1) / GearRatio(gear);
                    bool lugging = speedRpm < AutoDownshiftRpm;
                    bool kickdown = absThrottle > 0.85f && speedRpm < KickdownRpm;
                    if ((lugging || kickdown) && lowerRpm < shiftUpRpm - 500f)
                        ShiftTo(gear - 1);
                }
            }
        }

        shiftUpRequest = false;
        shiftDownRequest = false;
    }

    // --- engine ------------------------------------------------------------
    private float TorqueAt(float rpm)
    {
        float[] x = spec.rpm;
        float[] y = spec.torque;

        if (rpm <= x[0]) return y[0] * Mathf.Max(0f, rpm) / x[0];
        for (int i = 1; i < x.Length; i++)
        {
            if (rpm <= x[i])
            {
                float t = (rpm - x[i - 1]) / (x[i] - x[i - 1]);
                return Mathf.Lerp(y[i - 1], y[i], t);
            }
        }
        return y[y.Length - 1];
    }

    // --- drivetrain & brakes -------------------------------------------------
    private void ApplyDrivetrainAndBrakes(float dt)
    {
        // Driver intent: pressing the opposite direction while rolling = brake first, scaled by how far it is pressed.
        float throttle = throttleInput;
        float brakeTarget = 0f;

        bool oppositeInput = throttle * forwardSpeedMS < 0f && Mathf.Abs(forwardSpeedMS) > 1f;
        if (oppositeInput)
        {
            brakeTarget = Mathf.Abs(throttle);
            throttle = 0f;
        }
        appliedThrottle = throttle;

        // Brakes build up over brakeBuildTime and release immediately. Fully airborne: nothing braked, ramp restarts.
        if (groundedWheels == 0) brakeApplied = 0f;
        else if (brakeTarget > brakeApplied) brakeApplied = Mathf.MoveTowards(brakeApplied, brakeTarget, dt / brakeBuildTime);
        else brakeApplied = brakeTarget;

        // ---- engine speed and torque ----
        bool reverseGear = gear == -1;
        float driveThrottle = 0f;
        if ((gear >= 1 && throttle > 0f) || (reverseGear && throttle < 0f))
            driveThrottle = Mathf.Abs(throttle);

        float ratio = CurrentTotalRatio();
        float rearWheelRpm = (Mathf.Abs(ws[RearLeft].rpm) + Mathf.Abs(ws[RearRight].rpm)) * 0.5f;
        float wheelBasedRpm = rearWheelRpm * ratio;                         // rpm if the clutch were locked
        float clutchSlipRpm = (gear == 1 || reverseGear)
            ? Mathf.Lerp(spec.idleRpm, launchRpm, driveThrottle)            // clutch slips on a launch
            : spec.idleRpm;
        rawEngineRpm = Mathf.Max(wheelBasedRpm, clutchSlipRpm);
        engineRpmDisplay = Mathf.Lerp(engineRpmDisplay, rawEngineRpm, 1f - Mathf.Exp(-20f * dt));

        // Rev limiter (fuel cut with hysteresis) and electronic speed limiter.
        if (rawEngineRpm >= spec.limiterRpm) limiterActive = true;
        else if (rawEngineRpm < spec.limiterRpm - 200f) limiterActive = false;
        bool speedLimited = speedLimiterKmh > 0f && SpeedKmh >= speedLimiterKmh;
        bool clutchEngaged = shiftTimer <= 0f && ratio > 0f;

        float engineTq = 0f;
        if (clutchEngaged && !limiterActive && !speedLimited)
            engineTq = TorqueAt(rawEngineRpm) * powerMultiplier * driveThrottle;

        engineTorqueNm = engineTq;
        enginePowerKw = engineTq * rawEngineRpm * RadPerRpm * 0.001f;

        float wheelTorque = engineTq * ratio * drivelineEfficiency * (reverseGear ? -1f : 1f);

        // Engine braking when off the throttle (applied as brake torque so it can never reverse the car).
        float engineBrakeWheel = 0f;
        if (clutchEngaged && driveThrottle <= 0f && wheelBasedRpm > spec.idleRpm)
        {
            float crank = (EngineBrakeBaseNm + EngineBrakePerRpm * wheelBasedRpm) * engineBraking;
            float speedFade = Mathf.InverseLerp(0.3f, 2.5f, Mathf.Abs(forwardSpeedMS));
            engineBrakeWheel = crank * ratio * drivelineEfficiency * speedFade;
        }

        // ---- limited-slip differential splits the torque between the two rear wheels ----
        SplitDifferential(wheelTorque, driveThrottle > 0f, out float torqueL, out float torqueR);

        // ---- per-wheel brakes ----
        float frontBrake = brakeApplied * brakeTorqueFront;
        float rearBrake = brakeApplied * brakeTorqueRear + engineBrakeWheel * 0.5f + (handbrakeHeld ? handbrakePower : 0f);

        SetWheelTorque(FrontLeft, 0f, frontBrake);
        SetWheelTorque(FrontRight, 0f, frontBrake);
        SetWheelTorque(RearLeft, torqueL, rearBrake);
        SetWheelTorque(RearRight, torqueR, rearBrake);
    }

    // Torque-sensing LSD model: the slower rear wheel gets extra torque, up to (lock x torque).
    // With 25% lock the slower wheel gets 62.5% and the faster wheel 37.5% of the torque.
    private void SplitDifferential(float torque, bool powerOn, out float left, out float right)
    {
        float lock01 = powerOn ? diffLockPower : diffLockCoast;
        float wl = Mathf.Abs(ws[RearLeft].rpm) * RadPerRpm;      // rad/s
        float wr = Mathf.Abs(ws[RearRight].rpm) * RadPerRpm;

        float s = Mathf.Clamp((wl - wr) / diffFullLockRadPerSec, -1f, 1f);   // > 0: left wheel is faster
        float bias = lock01 * s * 0.5f;

        left = torque * (0.5f - bias);
        right = torque * (0.5f + bias);
    }

    // A wheel in the air gets no engine or brake torque: with almost no inertia it would
    // spin up (or lock) instantly, then grab or skid the moment it touches down.
    private void SetWheelTorque(int index, float motor, float brake)
    {
        bool grounded = slipData[index].grounded;
        ws[index].col.motorTorque = grounded ? motor : 0f;
        ws[index].col.brakeTorque = grounded ? brake : 0f;
    }

    // --- steering -------------------------------------------------------------
    private void ApplySteering(float dt)
    {
        // Less steering at speed.
        float t = Mathf.InverseLerp(steerReduceStartKmh, Mathf.Max(250f, steerReduceStartKmh + 1f), SpeedKmh);
        float speedFactor = Mathf.Lerp(1f, highSpeedSteer, t);

        float target = steerInput * maxSteer * speedFactor + CounterSteer();
        target = Mathf.Clamp(target, -maxSteer, maxSteer);

        steerAngle = Mathf.MoveTowards(steerAngle, target, steerSpeed * dt);
        ApplyAckermann(steerAngle);
    }

    // Each front wheel gets its own angle: the inner wheel turns more than the outer one.
    private void ApplyAckermann(float averageDeg)
    {
        float a = Mathf.Abs(averageDeg);
        float inner = a;
        float outer = a;

        if (a > 0.01f && ackermann > 0f)
        {
            float radius = wheelbase / Mathf.Tan(a * Mathf.Deg2Rad);    // turn radius at the rear axle
            float halfTrack = trackFront * 0.5f;
            float innerTrue = Mathf.Atan(wheelbase / Mathf.Max(radius - halfTrack, 0.1f)) * Mathf.Rad2Deg;
            float outerTrue = Mathf.Atan(wheelbase / (radius + halfTrack)) * Mathf.Rad2Deg;
            inner = Mathf.Lerp(a, innerTrue, ackermann);
            outer = Mathf.Lerp(a, outerTrue, ackermann);
        }

        float sign = averageDeg >= 0f ? 1f : -1f;
        bool turningRight = averageDeg >= 0f;
        ws[FrontLeft].col.steerAngle = sign * (turningRight ? outer : inner);
        ws[FrontRight].col.steerAngle = sign * (turningRight ? inner : outer);
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

    // --- tires: per-wheel grip + the drift model for the rear ----------------------------
    private void UpdateTireGrip(float dt)
    {
        float throttle = Mathf.Max(0f, appliedThrottle);
        float kmh = SpeedKmh;

        // 1) What does the driver want? (1 = full grip, lower = looser rear)
        float intent = 1f;

        // Handbrake
        if (handbrakeHeld) intent *= handbrakeGrip;

        // Power oversteer: hard throttle + steering at speed (on top of the real wheelspin from the engine)
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

        // 4) Every wheel gets its own stiffness: base grip x load sensitivity (x drift factor on the rear).
        for (int i = 0; i < 4; i++)
        {
            WheelState w = ws[i];

            float loadScale = 1f;
            if (w.load > 1f && w.staticLoad > 1f)
                loadScale = Mathf.Pow(Mathf.Clamp(w.load / w.staticLoad, 0.25f, 3f), -tireLoadSensitivity);

            float sideways = (w.front ? tireGripFront : tireGripRear * rearGripFactor) * loadScale;
            float forward = tireGripForward * loadScale;
            SetStiffness(w.col, forward, sideways);
        }
    }

    private static void SetStiffness(WheelCollider col, float forward, float sideways)
    {
        WheelFrictionCurve f = col.forwardFriction;
        f.stiffness = forward;
        col.forwardFriction = f;

        WheelFrictionCurve s = col.sidewaysFriction;
        s.stiffness = sideways;
        col.sidewaysFriction = s;
    }

    // --- extra forces ---------------------------------------------------------------
    private void ApplyDriftForces()
    {
        if (groundedWheels == 0) return;     // no speed boost, yaw damping or feint kick mid-air

        float throttle = Mathf.Max(0f, appliedThrottle);

        // Speed retention: a small push (half along the nose, half along the velocity) while drifting on throttle.
        // It scales with slip angle, fades near top speed and switches off as the car nears the max angle.
        if (isDrifting && throttle > 0.1f && driftSpeedBoost > 0f)
        {
            float slipScale = Mathf.Clamp01(Mathf.Abs(slipAngle) / driftBoostFullAngle);
            float speedRatio = Mathf.Clamp01(SpeedKmh / Mathf.Max(speedLimiterKmh, 100f));
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

        if (spd > 0.05f)
        {
            float drag = 0.5f * 1.225f * dragCd * frontalArea * spd * spd;
            float rolling = rollingResistanceCoeff * totalMass * Physics.gravity.magnitude
                            * (groundedWheels * 0.25f) * Mathf.Clamp01(spd / 0.5f);
            rb.AddForce(-vel / spd * (drag + rolling));
        }

        rb.AddForce(-transform.up * downforce * spd);

        ApplyAntiRoll(ws[FrontLeft].col, ws[FrontRight].col, antiRollFront);
        ApplyAntiRoll(ws[RearLeft].col, ws[RearRight].col, antiRollRear);
    }

    private void ApplyAntiRoll(WheelCollider left, WheelCollider right, float stiffness)
    {
        float travelL = 1f, travelR = 1f;

        bool groundedL = left.GetGroundHit(out WheelHit hit);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;

        bool groundedR = right.GetGroundHit(out hit);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;

        float force = (travelL - travelR) * stiffness;

        if (groundedL) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (groundedR) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }

    // Once all four wheels touch the ground, put the center of mass at its real height above the road.
    private void UpdateCenterOfMass()
    {
        if (!comDirty || groundedWheels < 4) return;

        float contactY = 0f;
        for (int i = 0; i < 4; i++)
            contactY += transform.InverseTransformPoint(slipData[i].point).y;
        contactY *= 0.25f;

        Vector3 com = rb.centerOfMass;
        com.y = contactY + spec.comHeight + comHeightOffset;
        rb.centerOfMass = com;
        comDirty = false;
    }

    // =====================================================================
    //  VISUALS
    // =====================================================================

    private void LateUpdate()
    {
        for (int i = 0; i < 4; i++)
        {
            WheelState w = ws[i];
            if (w.mesh == null) continue;

            w.col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            Quaternion finalRot = rot * w.meshOffset;
            w.mesh.SetPositionAndRotation(pos - finalRot * w.meshCenterOffset, finalRot);
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
        brakeApplied = 0f;
        gear = 1;
        shiftTimer = 0f;
        stuckTimer = 0f;
        stuckReported = false;
        EndDrift();
    }

    // =====================================================================
    //  DEBUG DISPLAY (toggle with debugToggleKey, default F1)
    // =====================================================================

    private string GearLabel()
    {
        if (gear == -1) return "R";
        if (gear == 0) return "N";
        return gear.ToString();
    }

    private void OnGUI()
    {
        if (!showDebug || spec == null) return;

        if (debugLabel == null)
        {
            debugLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            debugLabel.normal.textColor = Color.white;
        }

        // Scale with screen height so it stays readable on 1440p / 4K.
        float scale = Mathf.Max(0.75f, Screen.height / 1080f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float w = 400f;
        const float h = 345f;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(10f, 10f, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        string state = isDrifting
            ? "<color=#5CFF7A><b>DRIFTING</b></color>"
            : "<color=#BBBBBB>grip</color>";

        string text =
            spec.name + "\n" +
            totalMass.ToString("0") + " kg   " + (frontShare * 100f).ToString("0.0") + "/" + ((1f - frontShare) * 100f).ToString("0.0") + " front/rear\n" +
            "State: " + state + "   Wheels on ground: " + groundedWheels + "/4\n" +
            "Input: " + InputModeName + "\n" +
            "Gear " + GearLabel() + "   " + EngineRpm.ToString("0") + " rpm   " + SpeedKmh.ToString("0") + " km/h\n" +
            "Engine: " + engineTorqueNm.ToString("0") + " Nm   " + enginePowerKw.ToString("0") + " kW (" + (enginePowerKw * 1.35962f).ToString("0") + " PS)\n" +
            "Motor RL/RR: " + ws[RearLeft].col.motorTorque.ToString("0") + " / " + ws[RearRight].col.motorTorque.ToString("0") +
            " Nm   Brake RL: " + ws[RearLeft].col.brakeTorque.ToString("0") + "\n" +
            "Slip angle: " + slipAngle.ToString("0.0") + " deg   Drift: " + DriftDuration.ToString("0.0") + " s (last " + lastDriftDuration.ToString("0.0") + ")\n" +
            "Rear grip: " + rearGripFactor.ToString("0.00") + "   Angle limit: " + limit01.ToString("0.00") + "\n" +
            "Throttle: " + throttleInput.ToString("0.00") + "   Steer: " + steerInput.ToString("0.00") + "   Handbrake: " + (handbrakeHeld ? "ON" : "off") + "\n" +
            "Load N FL/FR/RL/RR: " +
            slipData[0].load.ToString("0") + " " + slipData[1].load.ToString("0") + " " +
            slipData[2].load.ToString("0") + " " + slipData[3].load.ToString("0") + "\n" +
            "Suspension compression % FL/FR/RL/RR: " +
            (ws[0].compression * 100f).ToString("0") + " " + (ws[1].compression * 100f).ToString("0") + " " +
            (ws[2].compression * 100f).ToString("0") + " " + (ws[3].compression * 100f).ToString("0") + "\n" +
            "Slip FL/FR/RL/RR: " +
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