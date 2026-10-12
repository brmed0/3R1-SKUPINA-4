using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The top-left column: TIME, BEST TIME and the PENALTY box + bar.
// The penalty part is hidden until you actually get a penalty.
public class HudTimerPanel : MonoBehaviour
{
    [SerializeField] private StageTimer timer;

    [Header("Times")]
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text bestText;

    [Header("Penalty (hidden while there is none)")]
    [SerializeField] private GameObject penaltyRoot;
    [SerializeField] private TMP_Text penaltyText;
    [Tooltip("The bar's Fill object. Its width is changed through its anchors.")]
    [SerializeField] private RectTransform penaltyBarFill;
    [Tooltip("Background image of the penalty box. It flashes when a new penalty arrives.")]
    [SerializeField] private Image penaltyBackground;
    [Tooltip("Penalty (seconds) that fills the bar completely.")]
    [SerializeField] private float penaltyForFullBar = 10f;
    [SerializeField] private Color flashColor = new Color(0.9f, 0.2f, 0.18f, 0.9f);
    [SerializeField] private float flashTime = 0.6f;

    private Color normalColor;
    private float flash;

    private void Awake()
    {
        if (timer == null || timeText == null || bestText == null || penaltyRoot == null ||
            penaltyText == null || penaltyBarFill == null)
        {
            Debug.LogError("[HudTimerPanel] Some slots are empty. Fill them all in the Inspector.", this);
            enabled = false;
            return;
        }

        if (penaltyBackground != null) normalColor = penaltyBackground.color;
        penaltyRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (timer != null) timer.PenaltyAdded += OnPenalty;
    }

    private void OnDisable()
    {
        if (timer != null) timer.PenaltyAdded -= OnPenalty;
    }

    private void OnPenalty(float seconds)
    {
        flash = flashTime;
    }

    private void Update()
    {
        timeText.text = StageTimer.Format(timer.TotalTime);
        bestText.text = "BEST TIME: " + (timer.HasBest ? StageTimer.Format(timer.BestTime) : "--:--:---");

        bool hasPenalty = timer.PenaltySeconds > 0f;
        if (penaltyRoot.activeSelf != hasPenalty) penaltyRoot.SetActive(hasPenalty);
        if (!hasPenalty) return;

        penaltyText.text = "PENALTY " + StageTimer.FormatPenalty(timer.PenaltySeconds);
        penaltyBarFill.anchorMax = new Vector2(Mathf.Clamp01(timer.PenaltySeconds / penaltyForFullBar), 1f);

        if (penaltyBackground != null)
        {
            flash = Mathf.Max(0f, flash - Time.deltaTime);
            penaltyBackground.color = Color.Lerp(normalColor, flashColor, flash / Mathf.Max(flashTime, 0.01f));
        }
    }
}
