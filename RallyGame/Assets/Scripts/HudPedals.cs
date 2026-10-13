using UnityEngine;

// The four pedal bars: handbrake, clutch, brake, gas.
// Each "Fill" object is stretched inside its bar, and this script changes its height through the anchors.
public class HudPedals : MonoBehaviour
{
    [SerializeField] private CarTelemetry telemetry;

    [Header("Fill objects")]
    [SerializeField] private RectTransform handbrakeFill;
    [SerializeField] private RectTransform clutchFill;
    [SerializeField] private RectTransform brakeFill;
    [SerializeField] private RectTransform gasFill;

    private void Awake()
    {
        if (telemetry == null || handbrakeFill == null || clutchFill == null || brakeFill == null || gasFill == null)
        {
            Debug.LogError("[HudPedals] Some slots are empty. Fill them all in the Inspector.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        SetFill(handbrakeFill, telemetry.Handbrake);
        SetFill(clutchFill, telemetry.Clutch);
        SetFill(brakeFill, telemetry.Brake);
        SetFill(gasFill, telemetry.Throttle);
    }

    private static void SetFill(RectTransform fill, float value)
    {
        fill.anchorMax = new Vector2(1f, Mathf.Clamp01(value));
    }
}
