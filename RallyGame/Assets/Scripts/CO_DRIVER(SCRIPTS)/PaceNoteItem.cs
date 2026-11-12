using UnityEngine;
using UnityEngine.UI;

// One pace-note tile on screen. Created by PaceNoteHUD, you don't add this yourself.
// It never "plays" an animation: it just chases a target position / scale / alpha,
// so any time the target changes it glides there on its own.
[RequireComponent(typeof(CanvasGroup))]
public class PaceNoteItem : MonoBehaviour
{
    [Header("References (filled in by PaceNoteHUD)")]
    public Image icon;
    public Image badge;

    [Header("Feel")]
    [Tooltip("Higher = snappier. 30 settles in about a quarter of a second.")]
    public float speed = 30f;
    [Tooltip("1 = smooth, no bounce. Lower it (0.5 - 0.7) for a bouncy feel.")]
    [Range(0.2f, 1f)]
    public float dampingRatio = 1f;
    [Tooltip("How fast the tile fades in/out (alpha per second)")]
    public float fadeSpeed = 4f;
    [Tooltip("Scale the tile shrinks to while it slides out")]
    public float exitScale = 0.8f;

    RectTransform rt;
    CanvasGroup group;

    Vector2 targetPos;
    float velX, velY;
    float targetScale = 1f;
    float velScale;
    float targetAlpha = 1f;
    bool dismissed;

    void Awake()
    {
        rt = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
    }

    // Put the tile at its starting point (invisible)
    public void Spawn(Sprite sprite, Sprite badgeSprite, Vector2 startPos, float startScale)
    {
        icon.sprite = sprite;
        icon.enabled = sprite != null; // no sprite = nothing, instead of a white square

        if (badge != null)
        {
            badge.sprite = badgeSprite;
            badge.gameObject.SetActive(badgeSprite != null);
        }

        rt.anchoredPosition = startPos;
        rt.localScale = Vector3.one * startScale;
        group.alpha = 0f;

        targetPos = startPos;
        targetScale = startScale;
        targetAlpha = 0f;
    }

    // Tell the tile where it should be
    public void SetTarget(Vector2 pos, float scale, float alpha)
    {
        if (dismissed) return;
        targetPos = pos;
        targetScale = scale;
        targetAlpha = alpha;
    }

    // The car passed this note: slide out, shrink a little, fade, then delete
    public void Dismiss(Vector2 exitPos)
    {
        dismissed = true;
        targetPos = exitPos;
        targetScale = exitScale;
        targetAlpha = 0f;
    }

    void Update()
    {
        // Unscaled time, so the HUD still animates if your game sets Time.timeScale to 0 (menus, pause).
        // The clamp only stops one huge hitch (like a scene load) from skipping the whole animation.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        Vector2 pos = rt.anchoredPosition;
        Spring(ref pos.x, ref velX, targetPos.x, speed, dampingRatio, dt);
        Spring(ref pos.y, ref velY, targetPos.y, speed, dampingRatio, dt);
        rt.anchoredPosition = pos;

        float s = rt.localScale.x;
        Spring(ref s, ref velScale, targetScale, speed, dampingRatio, dt);
        rt.localScale = Vector3.one * Mathf.Max(0f, s);

        // Plain fade
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * dt);

        if (dismissed && group.alpha <= 0.01f)
            Destroy(gameObject);
    }

    // Moves x toward target like a damped spring. This is the exact solution of the spring
    // equation for a time step of dt, so it behaves the same at 20 fps and at 240 fps
    // (a simple "add force, multiply by drag" spring gets slower and slower at low frame rates).
    static void Spring(ref float x, ref float v, float target, float omega, float zeta, float dt)
    {
        float d = x - target;

        if (zeta >= 0.999f)
        {
            // Critically damped: fastest approach with no overshoot
            float e = Mathf.Exp(-omega * dt);
            float k = v + omega * d;
            x = target + (d + k * dt) * e;
            v = (v - k * omega * dt) * e;
        }
        else
        {
            // Underdamped: overshoots a little and wobbles into place
            float za = zeta * omega;
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float e = Mathf.Exp(-za * dt);
            float cos = Mathf.Cos(wd * dt);
            float sin = Mathf.Sin(wd * dt);
            x = target + e * (d * cos + ((v + za * d) / wd) * sin);
            v = e * (v * cos - ((za * v + omega * omega * d) / wd) * sin);
        }
    }
}