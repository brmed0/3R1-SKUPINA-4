using UnityEngine;

// Puts everything the HUD needs in one place: speed, gear, RPM, pedals, distance.
// Put it on the car root, next to WheelControll and the Rigidbody.
//
// Your car script has no gearbox, so gear / RPM / clutch are FAKED here from the
// car's speed. This is only for the HUD, it does not change how the car drives.
[RequireComponent(typeof(Rigidbody))]
public class CarTelemetry : MonoBehaviour
{
      [SerializeField] private WheelControll car;

    [Header("Fake gearbox (HUD only)")]
    [SerializeField] private float idleRpm = 900f;
    [SerializeField] private float maxRpm = 8000f;
    [SerializeField] private float upshiftRpm = 7200f;
    [SerializeField] private float downshiftRpm = 3200f;
    [Tooltip("Speed (km/h) the car reaches at max RPM in gear 1, 2, 3...")]
    [SerializeField] private float[] gearTopSpeeds = { 45f, 75f, 105f, 135f, 160f, 190f };
    [SerializeField] private float reverseTopSpeed = 35f;
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

        if (gearTopSpeeds == null || gearTopSpeeds.Length == 0)
        {
            Debug.LogError("[CarTelemetry] Gear Top Speeds is empty.", this);
            enabled = false;
            return;
        }

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

    private void UpdateGearbox(float forwardSpeed, float dt)
    {
        float input = car.ThrottleInput;
        bool reversing = forwardSpeed < -0.5f || (SpeedKmh < 2f && input < -0.1f);

        float geared;   // the RPM the engine would have with the clutch fully engaged

        if (reversing)
        {
            Gear = -1;
            shiftTimer = 0f;
            geared = SpeedKmh / reverseTopSpeed * maxRpm;
        }
        else
        {
            Gear = Mathf.Clamp(Gear, 1, gearTopSpeeds.Length);

            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
            }
            else
            {
                float inCurrentGear = SpeedKmh / gearTopSpeeds[Gear - 1] * maxRpm;

                if (Gear < gearTopSpeeds.Length && inCurrentGear > upshiftRpm)
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

            geared = SpeedKmh / gearTopSpeeds[Gear - 1] * maxRpm;
        }

        bool shifting = shiftTimer > 0f;

        // Floor it from a standstill: the clutch slips, so revs go up before the car moves.
        float launch = Mathf.Lerp(idleRpm, launchRpm, Throttle * Mathf.InverseLerp(30f, 0f, SpeedKmh));

        float target = Mathf.Max(geared, launch);
        if (shifting) target = Mathf.Lerp(idleRpm, target, 0.5f);   // revs dip while changing gear
        target = Mathf.Clamp(target, idleRpm, maxRpm);

        Rpm = Mathf.Lerp(Rpm, target, 1f - Mathf.Exp(-rpmSmoothing * dt));
        Clutch = Mathf.MoveTowards(Clutch, shifting ? 1f : 0f, 10f * dt);
    }
}
