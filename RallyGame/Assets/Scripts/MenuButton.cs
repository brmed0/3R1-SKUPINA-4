using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A menu button: three bars (hamburger icon) that morph into an X when you click it,
// and back into three bars on the next click.
//
// HOW TO USE
//   1. Add this script to an empty GameObject.
//      (If the scene has no Canvas yet, one is created for you.)
//   2. The button shows up right away in the Scene / Game view - no need to press Play.
//   3. Move and resize it with the Rect Tool or the Rect Transform.
//      Colors, sprites, bar shape and animation are all in the Inspector.
//   4. For a transparent background, set the alpha (A) of "Background Color" to 0.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SketchButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Background  (alpha 0 = transparent)")]
    [SerializeField] private Color backgroundColor = new Color(0.20f, 0.19f, 0.18f, 1f);
    [Tooltip("Optional. Leave empty for a plain square, or use a rounded / custom sprite.")]
    [SerializeField] private Sprite backgroundSprite;

    [Header("Bars")]
    [SerializeField] private Color barColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [Tooltip("Optional. A rounded sprite (for example the built-in UISprite) gives rounded bars.")]
    [SerializeField] private Sprite barSprite;
    [Tooltip("Bar length as a fraction of the button size.")]
    [SerializeField, Range(0.1f, 1f)] private float barLength = 0.8f;
    [Tooltip("Bar thickness as a fraction of the button size.")]
    [SerializeField, Range(0.02f, 0.4f)] private float barThickness = 0.11f;
    [Tooltip("Distance between the bars (center to center) as a fraction of the button size.")]
    [SerializeField, Range(0.05f, 0.45f)] private float barSpacing = 0.30f;

    [Header("X Shape")]
    [Tooltip("Tick this to preview the X in the editor, and to start the game as an X.")]
    [SerializeField] private bool startAsX;
    [Tooltip("Length of the two X bars compared to the normal bars.")]
    [SerializeField, Range(0.5f, 1.3f)] private float xLength = 1f;

    [Header("Click Animation")]
    [Tooltip("How long the bars take to turn into the X (and back).")]
    [SerializeField, Min(0f)] private float morphTime = 0.35f;
    [Tooltip("How much the whole button squashes on click (0.14 = 14%). 0 = off.")]
    [SerializeField, Range(0f, 0.4f)] private float squash = 0.14f;
    [Tooltip("Small wobble in degrees on click. 0 = off.")]
    [SerializeField, Range(0f, 20f)] private float tilt = 6f;

    [Header("Events")]
    public UnityEvent onClick = new UnityEvent();
    [Tooltip("Called after every toggle. true = the button is now an X.")]
    public UnityEvent<bool> onToggled = new UnityEvent<bool>();

    // Generated child objects (real objects, so they are saved with the scene).
    [SerializeField, HideInInspector] private RectTransform background;
    [SerializeField, HideInInspector] private RectTransform[] bars;

    private bool isX;
    private float current;          // 0 = three bars, 1 = X
    private Coroutine running;

    public bool IsX => isX;

#if UNITY_EDITOR
    private bool setupQueued;
#else
    private bool started;
#endif

    // ---------------------------------------------------------------- lifecycle

    private void OnEnable()
    {
        isX = startAsX;
        current = isX ? 1f : 0f;
#if UNITY_EDITOR
        QueueEditorSetup();
#else
        if (started) Setup();
#endif
    }

    private void Start()
    {
#if !UNITY_EDITOR
        started = true;
        Setup();
#endif
    }

    private void OnDisable()
    {
        if (running == null) return;
        StopCoroutine(running);
        running = null;
        current = isX ? 1f : 0f;
        ApplyPose(current, current, current);
        ResetSquash();
    }

    // Keeps the layout right when you resize the button with the Rect Tool.
    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled && running == null) ApplyPose(current, current, current);
    }

#if UNITY_EDITOR
    // Creating objects is not allowed inside OnEnable / OnValidate in the editor,
    // so it is postponed by a moment. This is what makes the button appear before Play.
    private void QueueEditorSetup()
    {
        if (setupQueued) return;
        setupQueued = true;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            setupQueued = false;
            if (this == null || !isActiveAndEnabled) return;

            if (!Application.isPlaying)
            {
                isX = startAsX;
                current = isX ? 1f : 0f;
            }
            Setup();
        };
    }

    // Runs whenever you change something in the Inspector.
    private void OnValidate() { QueueEditorSetup(); }
#endif

    // ---------------------------------------------------------------- building

    private void Setup()
    {
        EnsureCanvas();
        EnsureEventSystem();
        EnsureParts();
        ApplyLook();
        ApplyPose(current, current, current);
    }

    private void EnsureCanvas()
    {
        if (GetComponentInParent<Canvas>(true) != null) return;

        GameObject go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(transform.parent, false);

        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        transform.SetParent(go.transform, false);

        // Brand new button: start in the top-left corner. Move it wherever you like afterwards.
        RectTransform rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(120f, 120f);
        rt.anchoredPosition = new Vector2(100f, -100f);

        MarkDirty();
    }

    private static void EnsureEventSystem()
    {
        if (!Application.isPlaying) return;

#if UNITY_2023_1_OR_NEWER
        if (FindFirstObjectByType<EventSystem>() != null) return;
#else
        if (FindObjectOfType<EventSystem>() != null) return;
#endif

        GameObject go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    private void EnsureParts()
    {
        bool created = false;

        // The background fills the whole button and catches the clicks.
        if (background == null)
        {
            background = NewRect("Background", transform);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;
            created = true;
        }

        // Three bars: [0] top, [1] middle, [2] bottom. Centered, so they can rotate around their middle.
        if (bars == null || bars.Length != 3) bars = new RectTransform[3];
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] != null) continue;

            RectTransform bar = NewRect("Bar " + (i + 1), background);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bars[i] = bar;
            created = true;
        }

        if (created) MarkDirty();
    }

    private RectTransform NewRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.layer = gameObject.layer;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image GetImage(RectTransform rt)
    {
        Image img = rt.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        return img;
    }

    private static bool UsesSlices(Sprite sprite)
    {
        return sprite != null && sprite.border.sqrMagnitude > 0f;
    }

    private void MarkDirty()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    // Colors and sprites.
    private void ApplyLook()
    {
        Image bg = GetImage(background);
        bg.color = backgroundColor;
        bg.sprite = backgroundSprite;
        bg.type = UsesSlices(backgroundSprite) ? Image.Type.Sliced : Image.Type.Simple;
        bg.raycastTarget = true;                        // the whole area is clickable...
        bg.canvasRenderer.cullTransparentMesh = false;  // ...even when the background is fully transparent

        foreach (RectTransform bar in bars)
        {
            Image img = GetImage(bar);
            img.color = barColor;
            img.sprite = barSprite;
            img.type = UsesSlices(barSprite) ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
        }
    }

    // ---------------------------------------------------------------- the morph

    // move: 0 = bars apart,   1 = top and bottom bars on top of each other (in the middle)
    // spin: 0 = flat bars,    1 = tilted 45 degrees (can go past 1 for a little bounce)
    // fade: 0 = middle bar visible, 1 = middle bar gone
    private void ApplyPose(float move, float spin, float fade)
    {
        if (background == null || bars == null) return;

        Rect rect = ((RectTransform)transform).rect;
        float size = Mathf.Min(rect.width, rect.height);
        float length = size * barLength;
        float thickness = size * barThickness;
        float gap = size * barSpacing;

        for (int i = 0; i < bars.Length; i++)
        {
            RectTransform bar = bars[i];
            if (bar == null) continue;

            bar.sizeDelta = new Vector2(length, thickness);

            if (i == 1)
            {
                // Middle bar: just shrinks away.
                bar.anchoredPosition = Vector2.zero;
                bar.localRotation = Quaternion.identity;
                bar.localScale = new Vector3(Mathf.Max(0f, 1f - fade), 1f, 1f);
            }
            else
            {
                // Top bar (dir = +1) and bottom bar (dir = -1): slide to the middle and tilt into an X.
                float dir = (i == 0) ? 1f : -1f;
                bar.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(dir * gap, 0f, move));
                bar.localRotation = Quaternion.Euler(0f, 0f, dir * 45f * spin);
                bar.localScale = new Vector3(Mathf.LerpUnclamped(1f, xLength, move), 1f, 1f);
            }
        }
    }

    // ---------------------------------------------------------------- clicking

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        onClick.Invoke();
        Toggle();
    }

    // Switch between bars and X. You can also call this from other scripts.
    public void Toggle()
    {
        SetX(!isX);
    }

    // true = X, false = three bars.
    public void SetX(bool x, bool animate = true)
    {
        bool changed = (x != isX);
        isX = x;
        float target = x ? 1f : 0f;

        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        if (animate && Application.isPlaying && isActiveAndEnabled && background != null)
        {
            running = StartCoroutine(Animate(current, target));
        }
        else
        {
            current = target;
            ApplyPose(target, target, target);
            ResetSquash();
        }

        if (changed) onToggled.Invoke(isX);
    }

    private IEnumerator Animate(float from, float to)
    {
        float total = Mathf.Max(0.6f, morphTime);   // long enough for the bounce to settle

        for (float t = 0f; t < total; t += Time.unscaledDeltaTime)   // unscaled: still plays when paused
        {
            float u = morphTime > 0f ? Mathf.Clamp01(t / morphTime) : 1f;

            // Bars slide together and the middle one disappears (smooth),
            // while the tilt overshoots a little and settles (bouncy).
            current = Mathf.LerpUnclamped(from, to, EaseOutCubic(u));
            float spin = Mathf.LerpUnclamped(from, to, EaseOutBack(u));
            ApplyPose(current, spin, current);

            // Whole button: squashes, then springs back with a damped bounce and a small tilt.
            float decay = Mathf.Exp(-8f * t);
            float s = 1f - squash * decay * Mathf.Cos(20f * t);
            background.localScale = new Vector3(s, s, 1f);
            background.localRotation = Quaternion.Euler(0f, 0f, tilt * decay * Mathf.Sin(22f * t));

            yield return null;
        }

        current = to;
        ApplyPose(to, to, to);
        ResetSquash();
        running = null;
    }

    private void ResetSquash()
    {
        if (background == null) return;
        background.localScale = Vector3.one;
        background.localRotation = Quaternion.identity;
    }

    // ---------------------------------------------------------------- easing

    private static float EaseOutCubic(float x)
    {
        float p = 1f - x;
        return 1f - p * p * p;
    }

    // 0 -> 1 with a small overshoot past 1 before settling.
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}