using System;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Four vertical pedal bars: HANDBRAKE | CLUTCH | BRAKE | GAS (same order as the sketch).
//
// SETUP: put this on any GameObject and drag the car (the one with CarTelemetry)
// into the Telemetry slot. It builds its own Canvas, so there is no UI to set up.
//
// - Visible in the GAME tab even when the game is NOT running (uses the preview sliders).
// - While playing, the bars follow the car and animate smoothly from 0% to 100%.
[ExecuteAlways]
[DisallowMultipleComponent]
public class PedalBarsHUD : MonoBehaviour
{
    [SerializeField] private CarTelemetry telemetry;

    [Header("Position")]
    [Tooltip("Where on the screen to stick to. (1,0) = bottom right, (0,0) = bottom left, (0.5,0) = bottom center.")]
    [SerializeField] private Vector2 screenAnchor = new Vector2(1f, 0f);
    [Tooltip("Distance from that edge. Right side = negative X, top side = negative Y.")]
    [SerializeField] private Vector2 margin = new Vector2(-40f, 40f);

    [Header("Size")]
    [SerializeField] private float barWidth = 38f;
    [SerializeField] private float barHeight = 190f;

    [Header("Animation (higher = faster)")]
    [SerializeField] private float riseSpeed = 12f;
    [SerializeField] private float fallSpeed = 7f;

    [Header("Look")]
    [SerializeField] private bool showPercent = true;
    [SerializeField] private Color panelColor = new Color(0.06f, 0.07f, 0.09f, 0.88f);
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color handbrakeColor = new Color(1f, 0.6f, 0.15f);
    [SerializeField] private Color clutchColor = new Color(0.35f, 0.65f, 1f);
    [SerializeField] private Color brakeColor = new Color(1f, 0.28f, 0.3f);
    [SerializeField] private Color gasColor = new Color(0.25f, 0.9f, 0.45f);

    [Header("Preview (what you see when the game is NOT running)")]
    [SerializeField, Range(0f, 1f)] private float previewHandbrake = 0f;
    [SerializeField, Range(0f, 1f)] private float previewClutch = 0f;
    [SerializeField, Range(0f, 1f)] private float previewBrake = 0.35f;
    [SerializeField, Range(0f, 1f)] private float previewGas = 0.8f;

    private const string CanvasName = "PedalBarsCanvas";

    private class Bar
    {
        public Func<float> live;
        public Func<float> preview;
        public Color color;
        public Image fillImg;
        public RectTransform fill;
        public GameObject cap;
        public Text percent;
        public float shown;
        public int lastPercent = -1;
    }

    private Bar[] bars;
    private GameObject canvasGO;
    private Font font;
    private Texture2D roundedTex;
    private Sprite roundedSprite;

#if UNITY_EDITOR
    private bool rebuildQueued;
#endif

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            QueueRebuild();   // safe way to create objects while editing
            return;
        }
#endif
        Build();
    }

    private void OnDisable()
    {
        Teardown();
    }

#if UNITY_EDITOR
    // Runs whenever you change something in the Inspector -> HUD updates instantly.
    private void OnValidate()
    {
        QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (rebuildQueued) return;
        rebuildQueued = true;

        EditorApplication.delayCall += () =>
        {
            rebuildQueued = false;
            if (this == null || !isActiveAndEnabled) return;
            Build();
        };
    }
#endif

    private void Update()
    {
        ApplyValues(false);
    }

    // ------------------------------------------------------------------
    // Build / teardown
    // ------------------------------------------------------------------

    private void Build()
    {
        Teardown();

        if (Application.isPlaying && telemetry == null)
        {
#if UNITY_2022_2_OR_NEWER
            telemetry = FindFirstObjectByType<CarTelemetry>();
#else
            telemetry = FindObjectOfType<CarTelemetry>();
#endif
            if (telemetry == null)
                Debug.LogError("[PedalBarsHUD] No CarTelemetry found in the scene. Drag your car into the Telemetry slot.", this);
        }

        font = GetFont();
        MakeRoundedSprite();

        // ---- canvas ----
        canvasGO = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
        canvasGO.hideFlags = HideFlags.DontSave;     // never saved into your scene
        canvasGO.transform.SetParent(transform, false);

        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ---- layout numbers ----
        const float border = 2f;
        const float pad = 16f;
        const float colW = 80f;
        const float gap = 6f;
        const float labelH = 24f;
        const float pctH = 28f;
        const float space = 6f;

        float innerW = pad * 2f + colW * 4f + gap * 3f;
        float innerH = pad * 2f + pctH + space + barHeight + space + labelH;

        float pctY = pad;
        float barY = pctY + pctH + space;
        float labelY = barY + barHeight + space;

        // ---- panel (outer = thin border, inner = dark background) ----
        RectTransform outer = NewRect("Panel", canvasGO.transform);
        outer.anchorMin = screenAnchor;
        outer.anchorMax = screenAnchor;
        outer.pivot = screenAnchor;
        outer.anchoredPosition = margin;
        outer.sizeDelta = new Vector2(innerW + border * 2f, innerH + border * 2f);
        AddRounded(outer, borderColor, 1f);

        RectTransform inner = NewRect("Inner", outer);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(border, border);
        inner.offsetMax = new Vector2(-border, -border);
        AddRounded(inner, panelColor, 1.1f);

        // ---- the four bars ----
        string[] names = { "HANDBRAKE", "CLUTCH", "BRAKE", "GAS" };
        Color[] colors = { handbrakeColor, clutchColor, brakeColor, gasColor };
        Func<float>[] live =
        {
            () => telemetry != null ? telemetry.Handbrake : 0f,
            () => telemetry != null ? telemetry.Clutch : 0f,
            () => telemetry != null ? telemetry.Brake : 0f,
            () => telemetry != null ? telemetry.Throttle : 0f
        };
        Func<float>[] preview =
        {
            () => previewHandbrake,
            () => previewClutch,
            () => previewBrake,
            () => previewGas
        };

        bars = new Bar[4];

        for (int i = 0; i < 4; i++)
        {
            float colX = pad + i * (colW + gap);
            float barX = colX + (colW - barWidth) * 0.5f;

            // name above
            Text label = MakeText("Label", inner, names[i], 11, FontStyle.Bold, new Color(1f, 1f, 1f, 0.8f));
            Place(label.rectTransform, colX, labelY, colW, labelH);

            // soft outline around the bar
            RectTransform frame = NewRect("Frame", inner);
            Place(frame, barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f);
            AddRounded(frame, new Color(1f, 1f, 1f, 0.16f), 1.6f);

            // dark rounded track. The Mask clips the fill to the rounded shape.
            RectTransform back = NewRect("Track", inner);
            Place(back, barX, barY, barWidth, barHeight);
            AddRounded(back, new Color(0.13f, 0.14f, 0.17f, 1f), 1.6f);
            Mask mask = back.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // colored fill: grows from the bottom, its height is driven by anchorMax.y
            RectTransform fill = NewRect("Fill", back);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(1f, 0f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImg = AddFlat(fill, colors[i]);

            // glossy highlight on the left side of the fill
            RectTransform shine = NewRect("Shine", fill);
            shine.anchorMin = new Vector2(0.14f, 0f);
            shine.anchorMax = new Vector2(0.38f, 1f);
            shine.offsetMin = Vector2.zero;
            shine.offsetMax = Vector2.zero;
            AddFlat(shine, new Color(1f, 1f, 1f, 0.20f));

            // bright line on top of the fill
            RectTransform cap = NewRect("Cap", fill);
            cap.anchorMin = new Vector2(0f, 1f);
            cap.anchorMax = new Vector2(1f, 1f);
            cap.pivot = new Vector2(0.5f, 1f);
            cap.sizeDelta = new Vector2(0f, 3f);
            cap.anchoredPosition = Vector2.zero;
            AddFlat(cap, new Color(1f, 1f, 1f, 0.6f));

            // tick marks at 25 / 50 / 75 %
            foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
            {
                RectTransform tick = NewRect("Tick", back);
                tick.anchorMin = new Vector2(0f, t);
                tick.anchorMax = new Vector2(1f, t);
                tick.pivot = new Vector2(0.5f, 0.5f);
                tick.sizeDelta = new Vector2(0f, 1.5f);
                tick.anchoredPosition = Vector2.zero;
                AddFlat(tick, new Color(1f, 1f, 1f, 0.10f));
            }

            // percent under the bar
            Text pct = MakeText("Percent", inner, "0%", 15, FontStyle.Bold, Color.white);
            Place(pct.rectTransform, colX, pctY, colW, pctH);
            pct.gameObject.SetActive(showPercent);

            bars[i] = new Bar
            {
                live = live[i],
                preview = preview[i],
                color = colors[i],
                fillImg = fillImg,
                fill = fill,
                cap = cap.gameObject,
                percent = pct
            };
        }

        ApplyValues(true);   // show something right away, even before the first Update
    }

    private void Teardown()
    {
        // Remove the canvas we made, plus any leftover one (e.g. after a script recompile).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c != null && c.name == CanvasName)
            {
                c.gameObject.SetActive(false);
                SafeDestroy(c.gameObject);
            }
        }

        canvasGO = null;
        bars = null;

        if (roundedSprite != null) SafeDestroy(roundedSprite);
        if (roundedTex != null) SafeDestroy(roundedTex);
        roundedSprite = null;
        roundedTex = null;
    }

    private static void SafeDestroy(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ------------------------------------------------------------------
    // Updating the bars
    // ------------------------------------------------------------------

    private void ApplyValues(bool instant)
    {
        if (bars == null) return;

        bool playing = Application.isPlaying;
        float dt = Time.unscaledDeltaTime;

        foreach (Bar b in bars)
        {
            if (b == null || b.fill == null) continue;

            float target = Mathf.Clamp01(playing ? b.live() : b.preview());

            if (instant || !playing)
            {
                b.shown = target;
            }
            else
            {
                // Smooth, frame-rate independent. Rises faster than it falls.
                float speed = target > b.shown ? riseSpeed : fallSpeed;
                b.shown = Mathf.Lerp(b.shown, target, 1f - Mathf.Exp(-speed * dt));
                if (Mathf.Abs(target - b.shown) < 0.002f) b.shown = target;
            }

            b.fill.anchorMax = new Vector2(1f, b.shown);
            b.fillImg.color = Color.Lerp(Dim(b.color), b.color, b.shown);

            bool capOn = b.shown > 0.02f;
            if (b.cap.activeSelf != capOn) b.cap.SetActive(capOn);

            if (showPercent)
            {
                int p = Mathf.RoundToInt(b.shown * 100f);
                if (p != b.lastPercent)
                {
                    b.lastPercent = p;
                    b.percent.text = p + "%";
                }
                // grey when idle, takes the bar's color when pressed
                b.percent.color = Color.Lerp(new Color(1f, 1f, 1f, 0.5f), b.color, Mathf.Clamp01(b.shown * 4f));
            }
        }
    }

    private static Color Dim(Color c)
    {
        return new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 1f);
    }

    // ------------------------------------------------------------------
    // Small UI helpers
    // ------------------------------------------------------------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    // Positions a rect by its bottom-left corner, measured from its parent's bottom-left.
    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // Plain rectangle.
    private static Image AddFlat(RectTransform rt, Color color)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // Rounded rectangle (9-sliced). Higher multiplier = smaller corner radius.
    private Image AddRounded(RectTransform rt, Color color, float radiusDivider)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = roundedSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = radiusDivider;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text MakeText(string name, Transform parent, string content, int size, FontStyle style, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static Font GetFont()
    {
        Font f;
#if UNITY_2022_2_OR_NEWER
        f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        f = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return f;
    }

    // Generates a small rounded-corner texture in code, so you don't need any image files.
    private void MakeRoundedSprite()
    {
        const int size = 64;
        const float radius = 20f;

        roundedTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        roundedTex.hideFlags = HideFlags.HideAndDontSave;
        roundedTex.wrapMode = TextureWrapMode.Clamp;
        roundedTex.filterMode = FilterMode.Bilinear;

        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f;
                float fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, radius, size - radius);
                float cy = Mathf.Clamp(fy, radius, size - radius);
                float dist = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                float a = Mathf.Clamp01(radius - dist + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        roundedTex.SetPixels32(px);
        roundedTex.Apply();

        roundedSprite = Sprite.Create(
            roundedTex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        roundedSprite.hideFlags = HideFlags.HideAndDontSave;
    }
}