using UnityEngine;

// Puts everything the HUD needs in one place: speed, gear, RPM, pedals, distance.
// Put it on the car root, next to WheelControll and the Rigidbody.
//
// Tuned for the BMW E36 M3 3.2 (EU, 1995-1998): S50B32 engine + Getrag S6S 420G 6-speed.
//
// Your car script has no gearbox, so gear / RPM / clutch are FAKED here from the
// car's speed. This is only for the HUD, it does not change how the car drives.
// RPM is now calculated from real gear ratios, final drive and tyre size, so the
// numbers behave like the real car instead of using made-up top speeds per gear.
[RequireComponent(typeof(Rigidbody))]
public class CarTelemetry : MonoBehaviour
{
    [SerializeField] private WheelControll car;

    [Header("Engine - S50B32 (EU)")]
    [Tooltip("Not confirmed from a source I could find. Typical for this engine, change if you know better.")]
    [SerializeField] private float idleRpm = 800f;
    [Tooltip("EU instrument cluster redline / limiter: 7600 rpm. (Peak power 321 PS / 236 kW @ 7400 rpm, peak torque 350 Nm @ 3250 rpm.)")]
    [SerializeField] private float maxRpm = 7600f;

    [Header("Gearbox - Getrag S6S 420G (6-speed)")]
    [SerializeField] private float[] gearRatios = { 4.23f, 2.51f, 1.67f, 1.23f, 1.00f, 0.83f };
    [Tooltip("Reverse ratio of the 420G family (3.746). Not confirmed specifically for the E36.")]
    [SerializeField] private float reverseRatio = 3.746f;
    [Tooltip("3.23:1 with 25% limited slip diff (3.2 Evolution). The 3.0 M3 used 3.15.")]
    [SerializeField] private float finalDrive = 3.23f;

    [Header("Driven wheels - rear tyres 245/40 ZR17")]
    [SerializeField] private float tyreWidthMm = 245f;
    [SerializeField] private float tyreAspectPercent = 40f;
    [SerializeField] private float rimDiameterInches = 17f;
    [Tooltip("A loaded tyre rolls slightly smaller than its nominal size. 1 = nominal, ~0.98 = realistic.")]
    [SerializeField] private float rollingFactor = 0.98f;

    [Header("Fake auto-shift (HUD only, not a real spec)")]
    [SerializeField] private float upshiftRpm = 6500f;
    [SerializeField] private float downshiftRpm = 3000f;
    [Tooltip("RPM the engine revs to when you floor it from standstill (clutch slipping).")]
    [SerializeField] private float launchRpm = 4500f;
    [SerializeField] private float shiftTime = 0.25f;
    [SerializeField] private float rpmSmoothing = 12f;

    [Header("Pedals")]
    [Tooltip("How fast the pedal bars move (full travel per second).")]
    [SerializeField] private float pedalSpeed = 8f;

    public float SpeedKmh { get; private set; }
    public float Rpm { get; private set; }
    public float MaxRpm => maxRpm;
    public float IdleRpm => idleRpm;
    public int Gear { get; private set; } = 1;          // -1 = reverse
    public string GearLabel => Gear < 0 ? "R" : Gear.ToString();
    public float Throttle { get; private set; }         // 0..1
    public float Brake { get; private set; }            // 0..1
    public float Clutch { get; private set; }           // 0..1 (only dips during gear changes)
    public float Handbrake { get; private set; }        // 0..1
    public float DistanceMeters { get; private set; }

    public void ResetDistance() => DistanceMeters = 0f;

    private Rigidbody rb;
    private float shiftTimer;
    private float wheelCircumference;   // metres

    private Vector3 Velocity =>
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity;
#else
        rb.velocity;
#endif

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (car == null) car = GetComponent<WheelControll>();

        if (car == null)
        {
            Debug.LogError("[CarTelemetry] No WheelControll found. Drag it into the Car slot.", this);
            enabled = false;
            return;
        }

        if (gearRatios == null || gearRatios.Length == 0)
        {
            Debug.LogError("[CarTelemetry] Gear Ratios is empty.", this);
            enabled = false;
            return;
        }

        // Tyre diameter = rim + 2 x sidewall (sidewall = width x aspect ratio)
        float sidewallM = tyreWidthMm / 1000f * (tyreAspectPercent / 100f);
        float diameterM = rimDiameterInches * 0.0254f + 2f * sidewallM;
        wheelCircumference = Mathf.PI * diameterM * rollingFactor;   // ~1.93 m for 245/40 R17

        Rpm = idleRpm;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 velocity = Velocity;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);   // m/s, negative = rolling backwards
        SpeedKmh = velocity.magnitude * 3.6f;
        DistanceMeters += velocity.magnitude * dt;

        UpdatePedals(forwardSpeed, dt);
        UpdateGearbox(forwardSpeed, dt);
    }

    private void UpdatePedals(float forwardSpeed, float dt)
    {
        float input = car.ThrottleInput;

        // Pushing the opposite way to where the car is rolling = braking.
        bool againstMotion = (input > 0f && forwardSpeed < -0.5f) || (input < 0f && forwardSpeed > 0.5f);

        float targetBrake = againstMotion ? Mathf.Abs(input) : 0f;
        float targetThrottle = againstMotion ? 0f : Mathf.Abs(input);
        float targetHandbrake = car.HandbrakeHeld ? 1f : 0f;

        float step = pedalSpeed * dt;
        Throttle = Mathf.MoveTowards(Throttle, targetThrottle, step);
        Brake = Mathf.MoveTowards(Brake, targetBrake, step);
        Handbrake = Mathf.MoveTowards(Handbrake, targetHandbrake, step * 2f);
    }

    // Engine RPM you would see at the current speed with the clutch fully engaged in the given gear.
    private float RpmInGear(int gear)
    {
        float ratio = (gear < 0 ? reverseRatio : gearRatios[gear - 1]) * finalDrive;
        float wheelRps = (SpeedKmh / 3.6f) / wheelCircumference;   // wheel revolutions per second
        return wheelRps * 60f * ratio;
    }

    private void UpdateGearbox(float forwardSpeed, float dt)
    {
        float input = car.ThrottleInput;
        bool reversing = forwardSpeed < -0.5f || (SpeedKmh < 2f && input < -0.1f);

        float geared;   // the RPM the engine would have with the clutch fully engaged

        if (reversing)
        {
            Gear = -1;
            shiftTimer = 0f;
            geared = RpmInGear(-1);
        }
        else
        {
            if (Gear < 1) Gear = 1;
            Gear = Mathf.Clamp(Gear, 1, gearRatios.Length);

            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
            }
            else
            {
                float inCurrentGear = RpmInGear(Gear);

                if (Gear < gearRatios.Length && inCurrentGear > upshiftRpm)
                {
                    Gear++;
                    shiftTimer = shiftTime;
                }
                else if (Gear > 1 && inCurrentGear < downshiftRpm)
                {
                    Gear--;
                    shiftTimer = shiftTime;
                }
            }

            geared = RpmInGear(Gear);
        }

        bool shifting = shiftTimer > 0f;

        // Floor it from a standstill: the clutch slips, so revs go up before the car moves.
        float launch = Mathf.Lerp(idleRpm, launchRpm, Throttle * Mathf.InverseLerp(30f, 0f, SpeedKmh));

        float target = Mathf.Max(geared, launch);
        if (shifting) target = Mathf.Lerp(idleRpm, target, 0.5f);   // revs dip while changing gear
        target = Mathf.Clamp(target, idleRpm, maxRpm);              // rev limiter

        Rpm = Mathf.Lerp(Rpm, target, 1f - Mathf.Exp(-rpmSmoothing * dt));
        Clutch = Mathf.MoveTowards(Clutch, shifting ? 1f : 0f, 10f * dt);
    }
}
