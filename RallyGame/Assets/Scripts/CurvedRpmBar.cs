
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Rally/Standalone Curved RPM HUD")]
[RequireComponent(typeof(CanvasRenderer))]
public class CurvedRpmBar : MaskableGraphic
{
    [Header("Required")]
    [SerializeField] private CarTelemetry telemetry;

    [Header("Optional Text References")]
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text gearText;
    [SerializeField] private TMP_Text rpmText;

    [Header("Curved Bar")]
    [SerializeField] private float radius = 165f;
    [SerializeField] private float thickness = 11f;
    [SerializeField] private float startAngle = -135f;
    [SerializeField] private float sweepAngle = 270f;
    [SerializeField] private float smoothTime = 0.045f;

    [Header("Text Layout")]
    [SerializeField] private float rpmTextY = 95f;
    [SerializeField] private float gearTextY = 5f;
    [SerializeField] private float speedTextY = -76f;
    [SerializeField] private float unitTextY = -105f;

    [Header("Text Sizes")]
    [SerializeField] private float rpmFontSize = 18f;
    [SerializeField] private float gearFontSize = 82f;
    [SerializeField] private float speedFontSize = 50f;
    [SerializeField] private float unitFontSize = 13f;

    [Header("RPM Thresholds")]
    [SerializeField, Range(0f, 1f)] private float amberStart = 0.65f;
    [SerializeField, Range(0f, 1f)] private float redStart = 0.88f;
    [SerializeField] private float redlineRpm = 7000f;
    [SerializeField] private bool flashAtRedline = true;
    [SerializeField] private float flashSpeed = 14f;

    [Header("Rally Colors")]
    [SerializeField] private Color trackColor =
        new Color(0.55f, 0.75f, 0.85f, 0.20f);

    [SerializeField] private Color cyan =
        new Color(0.05f, 0.85f, 1f, 1f);

    [SerializeField] private Color amber =
        new Color(1f, 0.55f, 0.04f, 1f);

    [SerializeField] private Color red =
        new Color(1f, 0.055f, 0.035f, 1f);

    [SerializeField] private Color displayWhite =
        new Color(0.96f, 0.98f, 1f, 1f);

    [SerializeField] private Color muted =
        new Color(0.55f, 0.68f, 0.76f, 0.9f);

    [Header("Glow")]
    [SerializeField, Range(1f, 4f)] private float glowWidth = 2.5f;
    [SerializeField, Range(0f, 0.5f)] private float glowOpacity = 0.14f;

    private float displayedRatio;
    private float ratioVelocity;
    private bool currentlyFlashing;

    private int lastSpeed = int.MinValue;
    private int lastGear = int.MinValue;
    private int lastRpm = int.MinValue;
    private string lastGearLabel;

    private const string GeneratedPrefix = "__StandaloneRallyHUD_";

    protected override void Awake()
    {
        base.Awake();

        raycastTarget = false;
        color = Color.white;

        // Keep the whole curved mesh inside its RectTransform.
        float diameter = 2f * (radius + thickness * glowWidth + 4f);

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(diameter, diameter);
    }

    protected override void Start()
    {
        base.Start();

        if (telemetry == null)
        {
            Debug.LogError(
                "[CurvedRpmBar] Assign your car's CarTelemetry component.",
                this);

            enabled = false;
            return;
        }

        // Create any missing text fields automatically.
        if (rpmText == null)
            rpmText = CreateText("RPM", new Vector2(340f, 28f),
                new Vector2(0f, rpmTextY), rpmFontSize, muted);

        if (gearText == null)
            gearText = CreateText("Gear", new Vector2(180f, 100f),
                new Vector2(0f, gearTextY), gearFontSize, displayWhite);
        else
            ConfigureText(gearText, new Vector2(180f, 100f),
                new Vector2(0f, gearTextY), gearFontSize);

        if (speedText == null)
            speedText = CreateText("Speed", new Vector2(190f, 62f),
                new Vector2(0f, speedTextY), speedFontSize, displayWhite);
        else
            ConfigureText(speedText, new Vector2(190f, 62f),
                new Vector2(0f, speedTextY), speedFontSize);

        CreateText("SpeedUnit", new Vector2(120f, 22f),
            new Vector2(0f, unitTextY), unitFontSize, muted)
            .text = "KM/H";

        displayedRatio = Mathf.Clamp01(
            telemetry.Rpm / Mathf.Max(1f, telemetry.MaxRpm));

        UpdateHud(true);
        SetVerticesDirty();
    }

    private void Update()
    {
        UpdateHud(false);
    }

    private void UpdateHud(bool force)
    {
        float maxRpm = Mathf.Max(1f, telemetry.MaxRpm);
        float rpm = Mathf.Clamp(telemetry.Rpm, 0f, maxRpm);
        float ratio = Mathf.Clamp01(rpm / maxRpm);

        int speed = Mathf.Max(0, Mathf.RoundToInt(telemetry.SpeedKmh));
        int gear = telemetry.Gear;
        int roundedRpm = Mathf.RoundToInt(rpm);
        string label = telemetry.GearLabel;

        if (speedText != null && (force || speed != lastSpeed))
        {
            lastSpeed = speed;
            speedText.text = speed.ToString("000");
        }

        if (gearText != null &&
            (force || gear != lastGear || label != lastGearLabel))
        {
            lastGear = gear;
            lastGearLabel = label;
            gearText.text = label;
        }

        if (rpmText != null && (force || roundedRpm != lastRpm))
        {
            lastRpm = roundedRpm;

            rpmText.text = "RPM  " + roundedRpm.ToString("0000")
                + "  /  " + Mathf.RoundToInt(maxRpm).ToString("0000");
        }

        displayedRatio = Mathf.SmoothDamp(
            displayedRatio,
            ratio,
            ref ratioVelocity,
            Mathf.Max(0.001f, smoothTime),
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        float redThreshold = Mathf.Clamp01(
            Mathf.Min(redStart, redlineRpm / maxRpm));

        currentlyFlashing = flashAtRedline &&
            ratio >= redThreshold &&
            Mathf.Sin(Time.unscaledTime * flashSpeed) > 0f;

        Color rpmColor = GetRpmColor(ratio);

        if (rpmText != null)
            rpmText.color = rpmColor;

        if (gearText != null)
        {
            gearText.color = currentlyFlashing
                ? displayWhite
                : rpmColor;
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int steps = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Abs(sweepAngle) / 1.5f),
            96, 288);

        // Background track.
        AddStrip(vh, 1f, thickness, steps, false, 1f);

        // Rounded ends on the track.
        AddDisc(vh, PointOnArc(0f),
            thickness * 0.5f, trackColor);

        AddDisc(vh, PointOnArc(1f),
            thickness * 0.5f, trackColor);

        if (displayedRatio <= 0.0001f)
            return;

        // Two translucent layers create a soft neon glow.
        AddStrip(vh, displayedRatio,
            thickness * glowWidth, steps, true, glowOpacity);

        AddStrip(vh, displayedRatio,
            thickness * 1.6f, steps, true, glowOpacity * 1.5f);

        // One continuous foreground bar.
        AddStrip(vh, displayedRatio,
            thickness, steps, true, 1f);

        // Round the moving end of the bar.
        Color tipColor = currentlyFlashing
            ? displayWhite
            : GetRpmColor(displayedRatio);

        AddDisc(vh, PointOnArc(displayedRatio),
            thickness * 0.5f, tipColor);
    }

    private void AddStrip(
        VertexHelper vh,
        float progress,
        float width,
        int steps,
        bool useGradient,
        float opacity)
    {
        if (useGradient && progress <= 0f)
            return;

        float end = useGradient ? progress : 1f;

        int count = Mathf.Max(
            1, Mathf.CeilToInt(end * steps));

        float inner = Mathf.Max(0.1f, radius - width * 0.5f);
        float outer = radius + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float t0 = end * i / count;
            float t1 = end * (i + 1f) / count;
            float middle = (t0 + t1) * 0.5f;

            Color c0 = useGradient ? GetRpmColor(t0) : trackColor;
            Color c1 = useGradient ? GetRpmColor(t1) : trackColor;

            c0.a *= opacity;
            c1.a *= opacity;

            if (useGradient && currentlyFlashing)
            {
                if (t0 >= redStart)
                    c0 = new Color(1f, 1f, 1f, opacity);

                if (t1 >= redStart)
                    c1 = new Color(1f, 1f, 1f, opacity);
            }

            float a0 = startAngle + sweepAngle * t0;
            float a1 = startAngle + sweepAngle * t1;

            Vector2 outer0 = PointOnArc(a0, outer);
            Vector2 outer1 = PointOnArc(a1, outer);
            Vector2 inner1 = PointOnArc(a1, inner);
            Vector2 inner0 = PointOnArc(a0, inner);

            AddQuad(vh,
                outer0, outer1, inner1, inner0,
                c0, c1, c1, c0);
        }
    }

    private Color GetRpmColor(float ratio)
    {
        if (ratio >= redStart)
        {
            float t = Mathf.InverseLerp(redStart, 1f, ratio);
            return Color.Lerp(amber, red, t);
        }

        if (ratio >= amberStart)
        {
            float t = Mathf.InverseLerp(amberStart, redStart, ratio);
            return Color.Lerp(cyan, amber, t);
        }

        return cyan;
    }

    private Vector2 PointOnArc(float ratio)
    {
        return PointOnArc(
            startAngle + sweepAngle * ratio, radius);
    }

    private static Vector2 PointOnArc(float angleDegrees, float r)
    {
        float angle = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * r;
    }

    private static void AddQuad(
        VertexHelper vh,
        Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color ca, Color cb, Color cc, Color cd)
    {
        int start = vh.currentVertCount;

        AddVertex(vh, a, ca);
        AddVertex(vh, b, cb);
        AddVertex(vh, c, cc);
        AddVertex(vh, d, cd);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddDisc(
        VertexHelper vh, Vector2 center, float r, Color c)
    {
        const int sides = 24;

        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides;
            float a1 = (i + 1) * Mathf.PI * 2f / sides;

            Vector2 p0 = center + new Vector2(
                Mathf.Cos(a0), Mathf.Sin(a0)) * r;

            Vector2 p1 = center + new Vector2(
                Mathf.Cos(a1), Mathf.Sin(a1)) * r;

            int start = vh.currentVertCount;

            AddVertex(vh, center, c);
            AddVertex(vh, p0, c);
            AddVertex(vh, p1, c);

            vh.AddTriangle(start, start + 1, start + 2);
        }
    }

    private static void AddVertex(
        VertexHelper vh, Vector2 position, Color c)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = position;
        v.color = c;
        vh.AddVert(v);
    }

    private TMP_Text CreateText(
        string label,
        Vector2 size,
        Vector2 position,
        float fontSize,
        Color textColor)
    {
        GameObject go = new GameObject(
            GeneratedPrefix + label,
            typeof(RectTransform));

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        TMP_Text text = go.AddComponent<TextMeshProUGUI>();

        if (speedText != null && speedText.font != null)
            text.font = speedText.font;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.Center;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        return text;
    }

    private void ConfigureText(
        TMP_Text text, Vector2 size, Vector2 position, float fontSize)
    {
        RectTransform rt = text.rectTransform;
        rt.SetParent(transform, false);

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
    }
}
