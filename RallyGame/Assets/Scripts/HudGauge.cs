
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Rally HUD")]
[RequireComponent(typeof(CanvasRenderer))]
public class HudGauge : MaskableGraphic
{
    [Header("Required: drag your car's telemetry here")]
    [SerializeField] private CarTelemetry telemetry;

    [Header("Curved RPM Bar")]
    [SerializeField] private float radius = 160f;
    [SerializeField] private float thickness = 11f;
    [SerializeField] private float startAngle = -135f;
    [SerializeField] private float sweepAngle = 270f;
    [SerializeField] private float smoothTime = 0.045f;

    [Header("Shift Points")]
    [SerializeField, Range(0f, 1f)] private float amberStart = 0.65f;
    [SerializeField, Range(0f, 1f)] private float redStart = 0.88f;
    [SerializeField] private float redlineRpm = 7000f;
    [SerializeField] private bool flashAtRedline = true;
    [SerializeField] private float flashSpeed = 14f;

    [Header("Colors")]
    [SerializeField] private Color trackColor = new(0.55f, 0.75f, 0.85f, 0.20f);

    [SerializeField] private Color cyan = new(0.05f, 0.85f, 1f, 1f);

    [SerializeField] private Color amber = new(1f, 0.55f, 0.04f, 1f);

    [SerializeField] private Color red = new(1f, 0.055f, 0.035f, 1f);

    [SerializeField] private Color displayWhite = new(0.96f, 0.98f, 1f, 1f);

    [SerializeField] private Color muted = new(0.55f, 0.68f, 0.76f, 0.9f);

    [Header("Glow")]
    [SerializeField, Range(1f, 4f)] private float glowWidth = 2.5f;
    [SerializeField, Range(0f, 0.5f)] private float glowOpacity = 0.14f;

    private TMP_Text rpmText;
    private TMP_Text gearText;
    private TMP_Text speedText;

    private float displayedRatio;
    private float ratioVelocity;
    private float flashThreshold = 0.88f;
    private bool flashing;

    private int lastSpeed = int.MinValue;
    private int lastGear = int.MinValue;
    private int lastRpm = int.MinValue;
    private string lastGearLabel;

    private const string Prefix = "__RALLYHUD_ONE_SCRIPT_";

    protected override void Awake()
    {
        base.Awake();

        raycastTarget = false;
        color = Color.white;

        // Transparent HUD: only the curved mesh and text are drawn.
        // Size the mesh so its glow remains inside its RectTransform.
        Vector2 savedPosition = rectTransform.anchoredPosition;

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        float diameter =
            2f * (radius + thickness * glowWidth * 0.5f + 12f);

        rectTransform.sizeDelta = new Vector2(diameter, diameter);
        rectTransform.anchoredPosition = savedPosition;
    }

    protected override void Start()
    {
        base.Start();

        RemoveGeneratedTexts();
        BuildTexts();

        if (telemetry == null)
        {
            Debug.LogError(
                "[Rally HUD] Assign your car's CarTelemetry component " +
                "to the Telemetry field in the Inspector.", this);

            rpmText.text = "RPM  ----";
            gearText.text = "N";
            speedText.text = "000";
        }
        else
        {
            displayedRatio = Mathf.Clamp01(
                telemetry.Rpm / Mathf.Max(1f, telemetry.MaxRpm));

            UpdateHud(true);
        }

        SetVerticesDirty();
    }

    private void Update()
    {
        if (telemetry == null)
            return;

        UpdateHud(false);
    }

    private void UpdateHud(bool force)
    {
        float maxRpm = Mathf.Max(1f, telemetry.MaxRpm);
        float rpm = Mathf.Clamp(telemetry.Rpm, 0f, maxRpm);
        float ratio = Mathf.Clamp01(rpm / maxRpm);

        int speed = Mathf.Max(
            0, Mathf.RoundToInt(telemetry.SpeedKmh));

        int gear = telemetry.Gear;
        int roundedRpm = Mathf.RoundToInt(rpm);
        string gearLabel = telemetry.GearLabel;

        if (force || speed != lastSpeed)
        {
            lastSpeed = speed;
            speedText.text = speed.ToString("000");
        }

        if (force || gear != lastGear ||
            gearLabel != lastGearLabel)
        {
            lastGear = gear;
            lastGearLabel = gearLabel;
            gearText.text = gearLabel;
        }

        if (force || roundedRpm != lastRpm)
        {
            lastRpm = roundedRpm;

            rpmText.text =
                "RPM  " + roundedRpm.ToString("0000") +
                "  /  " + Mathf.RoundToInt(maxRpm).ToString("0000");
        }

        float previousRatio = displayedRatio;
        bool previousFlash = flashing;

        displayedRatio = Mathf.SmoothDamp(
            displayedRatio,
            ratio,
            ref ratioVelocity,
            Mathf.Max(0.001f, smoothTime),
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        flashThreshold = Mathf.Clamp01(
            Mathf.Min(redStart, redlineRpm / maxRpm));

        flashing = flashAtRedline &&
                   ratio >= flashThreshold &&
                   Mathf.Sin(Time.unscaledTime * flashSpeed) > 0f;

        Color activeColor = GetRpmColor(ratio);

        rpmText.color = activeColor;
        speedText.color = displayWhite;

        gearText.color = flashing ? displayWhite : activeColor;

        // Regenerate the mesh only when its visible state changes.
        if (force ||
            Mathf.Abs(displayedRatio - previousRatio) > 0.0001f ||
            flashing != previousFlash)
        {
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int steps = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Abs(sweepAngle) / 1.5f),
            96, 288);

        // Thin, subdued track for the unfilled portion.
        AddStrip(vh, 1f, thickness, steps, false, 1f);

        AddDisc(
            vh, PointOnArc(0f),
            thickness * 0.5f, trackColor);

        AddDisc(
            vh, PointOnArc(1f),
            thickness * 0.5f, trackColor);

        if (displayedRatio <= 0.0001f)
            return;

        // Two soft translucent layers create the neon glow.
        AddStrip(
            vh, displayedRatio, thickness * glowWidth,
            steps, true, glowOpacity);

        AddStrip(
            vh, displayedRatio, thickness * 1.6f,
            steps, true, glowOpacity * 1.5f);

        Color glowTip = GetRpmColor(displayedRatio);
        glowTip.a = glowOpacity;

        AddDisc(
            vh, PointOnArc(displayedRatio),
            thickness * glowWidth * 0.5f, glowTip);

        // Main continuous curved bar.
        AddStrip(
            vh, displayedRatio, thickness,
            steps, true, 1f);

        AddDisc(
            vh, PointOnArc(0f),
            thickness * 0.5f, cyan);

        Color tipColor = flashing
            ? displayWhite
            : GetRpmColor(displayedRatio);

        AddDisc(
            vh, PointOnArc(displayedRatio),
            thickness * 0.5f, tipColor);
    }

    private void AddStrip(
        VertexHelper vh,
        float progress,
        float width,
        int totalSteps,
        bool useGradient,
        float opacity)
    {
        float end = useGradient ? progress : 1f;

        if (end <= 0f)
            return;

        int count = Mathf.Max(
            1, Mathf.CeilToInt(end * totalSteps));

        float innerRadius = Mathf.Max(0.1f, radius - width * 0.5f);
        float outerRadius = radius + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float t0 = end * i / count;
            float t1 = end * (i + 1f) / count;

            Color c0 = useGradient ? GetRpmColor(t0) : trackColor;
            Color c1 = useGradient ? GetRpmColor(t1) : trackColor;

            c0.a *= opacity;
            c1.a *= opacity;

            if (useGradient && flashing)
            {
                if (t0 >= flashThreshold)
                    c0 = new Color(1f, 1f, 1f, opacity);

                if (t1 >= flashThreshold)
                    c1 = new Color(1f, 1f, 1f, opacity);
            }

            float angle0 = startAngle + sweepAngle * t0;
            float angle1 = startAngle + sweepAngle * t1;

            Vector2 outer0 = PointOnArc(angle0, outerRadius);
            Vector2 inner0 = PointOnArc(angle0, innerRadius);
            Vector2 outer1 = PointOnArc(angle1, outerRadius);
            Vector2 inner1 = PointOnArc(angle1, innerRadius);

            AddQuad(
                vh, outer0, outer1, inner1, inner0,
                c0, c1, c1, c0);
        }
    }

    private Color GetRpmColor(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);

        if (ratio >= redStart)
        {
            return Color.Lerp(
                amber, red,
                Mathf.InverseLerp(redStart, 1f, ratio));
        }

        if (ratio >= amberStart)
        {
            return Color.Lerp(
                cyan, amber,
                Mathf.InverseLerp(amberStart, redStart, ratio));
        }

        return cyan;
    }

    private Vector2 PointOnArc(float ratio)
    {
        return PointOnArc(
            startAngle + sweepAngle * ratio, radius);
    }

    // Zero degrees is at the top; the arc passes over the gear.
    private static Vector2 PointOnArc(float angleDegrees, float r)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Sin(radians),
            Mathf.Cos(radians)) * r;
    }

    private static void AddQuad(
        VertexHelper vh,
        Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color ca, Color cb, Color cc, Color cd)
    {
        int index = vh.currentVertCount;

        AddVertex(vh, a, ca);
        AddVertex(vh, b, cb);
        AddVertex(vh, c, cc);
        AddVertex(vh, d, cd);

        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    private static void AddDisc(
        VertexHelper vh, Vector2 center, float r, Color discColor)
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

            int index = vh.currentVertCount;

            AddVertex(vh, center, discColor);
            AddVertex(vh, p0, discColor);
            AddVertex(vh, p1, discColor);

            vh.AddTriangle(index, index + 1, index + 2);
        }
    }

    private static void AddVertex(
        VertexHelper vh, Vector2 position, Color vertexColor)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = vertexColor;

        vh.AddVert(vertex);
    }

    private void BuildTexts()
    {
        rpmText = CreateText(
            "RPMText", "RPM  0000  /  7000",
            new Vector2(340f, 28f),
            new Vector2(0f, 107f),
            18f, muted);

        gearText = CreateText(
            "GearText", "N",
            new Vector2(180f, 80f),
            new Vector2(0f, 12f),
            76f, displayWhite);

        speedText = CreateText(
            "SpeedText", "000",
            new Vector2(190f, 54f),
            new Vector2(0f, -63f),
            48f, displayWhite);

        CreateText(
            "SpeedUnit", "KM/H",
            new Vector2(120f, 18f),
            new Vector2(0f, -99f),
            13f, muted);
    }

    private TMP_Text CreateText(
        string objectName,
        string initialText,
        Vector2 size,
        Vector2 position,
        float fontSize,
        Color textColor)
    {
        GameObject go = new GameObject(
            Prefix + objectName,
            typeof(RectTransform));

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        rt.localScale = Vector3.one;

        TMP_Text text = go.AddComponent<TextMeshProUGUI>();

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        text.text = initialText;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.Center;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        return text;
    }

    private void RemoveGeneratedTexts()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child.name.StartsWith(Prefix))
                DestroyImmediate(child.gameObject);
        }
    }
}
