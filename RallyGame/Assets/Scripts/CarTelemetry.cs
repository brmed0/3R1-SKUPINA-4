using UnityEngine;

// Shared feed for the HUD, pedal bars, and engine audio.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class CarTelemetry : MonoBehaviour
{
    [SerializeField] private WhellContoll car;

    private float distanceAtReset;

    public float SpeedKmh => car != null ? car.SpeedKmh : 0f;
    public float Rpm => car != null ? car.EngineRpm : 0f;
    public float MaxRpm => car != null ? car.MaxRpm : 0f;
    public float IdleRpm => car != null ? car.IdleRpm : 0f;
    public int Gear => car != null ? car.Gear : 0;
    public string GearLabel => Gear < 0 ? "R" : Gear == 0 ? "N" : Gear.ToString();
    public bool IsShifting => car != null && car.IsShifting;
    public float Throttle => car != null ? car.Throttle : 0f;
    public float Brake => car != null ? car.Brake : 0f;
    public float Clutch => car != null ? car.Clutch : 0f;
    public float Handbrake => car != null ? car.Handbrake : 0f;
    public float EngineTorqueNm => car != null ? car.EngineTorqueNm : 0f;
    public float DistanceMeters => car != null ? Mathf.Max(0f, car.DistanceMeters - distanceAtReset) : 0f;

    private void Awake()
    {
        if (car == null)
            car = GetComponent<WhellContoll>();

        if (car == null)
            Debug.LogError("[CarTelemetry] Assign the E36 movement controller on the car.", this);
    }

    public void ResetDistance()
    {
        distanceAtReset = car != null ? car.DistanceMeters : 0f;
    }
}
