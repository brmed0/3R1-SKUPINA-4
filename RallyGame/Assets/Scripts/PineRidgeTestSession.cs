using UnityEngine;

[DefaultExecutionOrder(90)]
public sealed class PineRidgeTestSession : MonoBehaviour
{
    [System.Serializable]
    public struct Station
    {
        public string name;
        public string instructions;
        public Transform spawn;
    }

    [SerializeField] private WhellContoll car;
    [SerializeField] private FollowCamera chaseCamera;
    [SerializeField] private Transform hub;
    [SerializeField] private Station[] stations;
    [SerializeField] private WheelCollider[] wheels;

    private int station = -1;
    private readonly float[] travel = new float[4];
    private readonly bool[] contact = new bool[4];
    private static readonly string[] WheelLabels = { "FL", "FR", "RL", "RR" };
    private float recordSpeed, peakSlide, airborne, longestFlight;
    private float brakeStartSpeed, brakeDistance, lastBrakeDistance, lastBrakeSpeed;
    private bool braking, hasBrakeResult;
    private float waitForContact;
    private GUIStyle title, detail, mini;
    private Texture2D pixel;

    private void Awake()
    {
        if (car == null || hub == null || stations == null || wheels == null || wheels.Length != 4)
            enabled = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6)) Restart(-1);
        if (Input.GetKeyDown(KeyCode.T)) Restart(station);
        for (int i = 0; i < Mathf.Min(stations.Length, 6); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) ||
                Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i)))
                Restart(i);
    }

    private void Restart(int index)
    {
        Transform point = index < 0 ? hub : stations[index].spawn;
        if (point == null) return;
        station = index;
        car.ResetVehiclePose(point.position, point.rotation);
        if (chaseCamera != null) chaseCamera.SnapToTarget();
        recordSpeed = peakSlide = airborne = longestFlight = 0f;
        brakeStartSpeed = brakeDistance = lastBrakeDistance = lastBrakeSpeed = 0f;
        braking = hasBrakeResult = false;
        waitForContact = 0.5f;
    }

    private void FixedUpdate()
    {
        int grounded = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wheel = wheels[i];
            if (wheel == null) continue;
            contact[i] = wheel.isGrounded;
            if (contact[i]) grounded++;
            wheel.GetWorldPose(out Vector3 pose, out _);
            float extension = Vector3.Dot(wheel.transform.TransformPoint(wheel.center) - pose, wheel.transform.up);
            travel[i] = 1f - Mathf.Clamp01(extension / Mathf.Max(.01f, wheel.suspensionDistance));
        }

        waitForContact = Mathf.Max(0f, waitForContact-Time.fixedDeltaTime);
        float speed = car.SpeedKmh;
        recordSpeed = Mathf.Max(recordSpeed, speed);
        if (grounded > 0 && speed > 12f) peakSlide = Mathf.Max(peakSlide, car.AbsSlipAngle);
        if (grounded == 0 && speed > 8f && waitForContact == 0f)
        {
            airborne += Time.fixedDeltaTime;
            longestFlight = Mathf.Max(longestFlight, airborne);
        }
        else airborne = 0f;

        if (!braking && car.Brake > .1f && car.ForwardSpeedKmh > 12f)
        {
            braking = true;
            brakeStartSpeed = speed;
            brakeDistance = 0f;
        }
        if (!braking) return;
        brakeDistance += (speed / 3.6f) * Time.fixedDeltaTime;
        if (car.ForwardSpeedKmh <= 2.5f)
        {
            lastBrakeSpeed = brakeStartSpeed;
            lastBrakeDistance = brakeDistance;
            hasBrakeResult = true;
            braking = false;
        }
        else if (car.Brake < .03f) braking = false;
    }

    private void OnGUI()
    {
        if (!enabled) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, richText = false };
            detail = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
            mini = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false };
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
        Matrix4x4 matrix = GUI.matrix;
        Color color = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float y = Screen.height / scale - 225f;
        Draw(new Rect(22,y,390,199), new Color(.025f,.05f,.065f,.89f));
        Draw(new Rect(22,y,390,3), new Color(.20f,.80f,.86f));
        GUI.Label(new Rect(36,y+10,355,30), station < 0 ? "PINE RIDGE  /  PROVING GROUND" : $"{station + 1:00}  /  {stations[station].name}", title);
        GUI.Label(new Rect(36,y+43,354,44), station < 0 ?
            "1 Brakes   2 Suspension   3 Jump\n4 Slalom   5 Hairpin   6 Hill" : stations[station].instructions, detail);
        for (int i = 0; i < 4; i++)
        {
            float x = 36 + i * 91;
            GUI.Label(new Rect(x,y+88,70,18), WheelLabels[i], mini);
            Draw(new Rect(x,y+109,72,5), new Color(.18f,.23f,.25f));
            Draw(new Rect(x,y+109,72 * travel[i],5), contact[i] ? new Color(.29f,.86f,.79f) : new Color(1f,.62f,.29f));
        }
        string brake = braking ? $"Brake run   {brakeDistance:F1} m" :
            hasBrakeResult ? $"Last stop   {lastBrakeDistance:F1} m from {lastBrakeSpeed:F0} km/h" : "Brake run   waiting";
        GUI.Label(new Rect(36,y+120,360,19), $"Peak {recordSpeed:F0} km/h    Slide {peakSlide:F0}°    Air {longestFlight:F2} s", detail);
        GUI.Label(new Rect(36,y+141,360,19), brake, detail);
        GUI.Label(new Rect(36,y+169,357,20), "1–6  Select test     T  Repeat     F6  Hub", mini);
        GUI.color = color;
        GUI.matrix = matrix;
    }

    private void Draw(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect,pixel);
        GUI.color = old;
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }
}
