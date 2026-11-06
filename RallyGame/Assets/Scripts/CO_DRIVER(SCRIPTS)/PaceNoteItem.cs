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
    [Tooltip("Higher = faster")]
    public float stiffness = 900f;
    [Tooltip("About 2 x sqrt(stiffness) = smooth, no bounce. Lower it for a bouncy feel.")]
    public float damping = 60f;
    [Tooltip("How fast the tile fades in/out (alpha per second)")]
    public float fadeSpeed = 4f;
    [Tooltip("Scale the tile shrinks to while it slides out")]
    public float exitScale = 0.8f;

    RectTransform rt;
    CanvasGroup group;

    Vector2 targetPos;
    Vector2 velPos;
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
        // Clamped so a lag spike can't make the spring jitter.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);

        // Spring for position
        Vector2 pos = rt.anchoredPosition;
        velPos += (targetPos - pos) * stiffness * dt;
        velPos *= Mathf.Exp(-damping * dt);
        rt.anchoredPosition = pos + velPos * dt;

        // Spring for scale
        float s = rt.localScale.x;
        velScale += (targetScale - s) * stiffness * dt;
        velScale *= Mathf.Exp(-damping * dt);
        rt.localScale = Vector3.one * Mathf.Max(0f, s + velScale * dt);

        // Plain fade
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * dt);

        if (dismissed && group.alpha <= 0.01f)
            Destroy(gameObject);
    }
}