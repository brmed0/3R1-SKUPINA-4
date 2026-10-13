
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudTimerPanel : MonoBehaviour
{
    [Header("Timer")]
    [SerializeField] private bool startOnPlay = true;
    [SerializeField] private bool saveBestTime = true;
    [SerializeField] private string bestTimeKey = "RallyHUD_BestTime";

    [Header("Time Texts")]
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text bestText;

    [Header("Penalty UI")]
    [SerializeField] private GameObject penaltyRoot;
    [SerializeField] private TMP_Text penaltyText;
    [SerializeField] private RectTransform penaltyBarFill;
    [SerializeField] private Image penaltyBackground;

    [Header("Penalty Settings")]
    [SerializeField] private float penaltyForFullBar = 10f;
    [SerializeField] private Color flashColor =
        new Color(1f, 0.08f, 0.05f, 1f);
    [SerializeField] private float flashTime = 0.6f;

    private Color normalColor;

    private float elapsedTime;
    private float bestTime = float.PositiveInfinity;
    private float penaltySeconds;
    private float flashRemaining;

    private bool isRunning;
    private bool hasBestTime;

    public float ElapsedTime => elapsedTime;
    public float PenaltySeconds => penaltySeconds;
    public bool IsRunning => isRunning;

    private void Awake()
    {
        if (timeText == null || bestText == null ||
            penaltyRoot == null || penaltyText == null ||
            penaltyBarFill == null)
        {
            Debug.LogError(
                "[HudTimerPanel] Assign all required UI references " +
                "in the Inspector.", this);

            enabled = false;
            return;
        }

        if (penaltyBackground != null)
            normalColor = penaltyBackground.color;

        // The fill starts empty.
        penaltyBarFill.anchorMin = new Vector2(0f, 0f);
        penaltyBarFill.anchorMax = new Vector2(0f, 1f);

        penaltyRoot.SetActive(false);

        // Load the saved best time.
        if (saveBestTime && !string.IsNullOrWhiteSpace(bestTimeKey))
        {
            string timeKey = bestTimeKey + "_Time";
            string existsKey = bestTimeKey + "_Exists";

            hasBestTime = PlayerPrefs.GetInt(existsKey, 0) == 1;

            if (hasBestTime)
                bestTime = PlayerPrefs.GetFloat(timeKey, float.PositiveInfinity);
        }

        isRunning = startOnPlay;

        RefreshTexts();
    }

    private void Update()
    {
        // Update the stage timer.
        if (isRunning)
            elapsedTime += Time.deltaTime;

        // Animate the penalty warning.
        if (flashRemaining > 0f)
            flashRemaining = Mathf.Max(
                0f, flashRemaining - Time.deltaTime);

        if (penaltyBackground != null)
        {
            float intensity = flashRemaining /
                Mathf.Max(flashTime, 0.001f);

            penaltyBackground.color = Color.Lerp(
                normalColor, flashColor, intensity);
        }

        // Update the penalty display.
        bool hasPenalty = penaltySeconds > 0f;

        if (penaltyRoot.activeSelf != hasPenalty)
            penaltyRoot.SetActive(hasPenalty);

        if (hasPenalty)
        {
            penaltyText.text =
                "PENALTY  +" + penaltySeconds.ToString("F3") + "s";

            float fill = Mathf.Clamp01(
                penaltySeconds / Mathf.Max(0.001f, penaltyForFullBar));

            penaltyBarFill.anchorMax = new Vector2(fill, 1f);
        }

        RefreshTexts();
    }

    private void RefreshTexts()
    {
        if (timeText != null)
            timeText.text = FormatTime(elapsedTime);

        if (bestText != null)
        {
            bestText.text = hasBestTime
                ? "BEST TIME: " + FormatTime(bestTime)
                : "BEST TIME: --:--:---";
        }
    }

    // Call when a stage begins.
    public void StartTimer()
    {
        elapsedTime = 0f;
        penaltySeconds = 0f;
        flashRemaining = 0f;
        isRunning = true;

        if (penaltyRoot != null)
            penaltyRoot.SetActive(false);

        if (penaltyBackground != null)
            penaltyBackground.color = normalColor;

        RefreshTexts();
    }

    // Call when the player finishes a stage.
    public void StopTimer()
    {
        isRunning = false;

        if (elapsedTime > 0f &&
            (!hasBestTime || elapsedTime < bestTime))
        {
            bestTime = elapsedTime;
            hasBestTime = true;
            SaveBestTime();
        }

        RefreshTexts();
    }

    // Adds a penalty from another script.
    public void AddPenalty(float seconds)
    {
        if (seconds <= 0f)
            return;

        penaltySeconds += seconds;
        flashRemaining = Mathf.Max(0f, flashTime);

        if (penaltyRoot != null)
            penaltyRoot.SetActive(true);
    }

    // Removes all currently accumulated penalties.
    public void ClearPenalty()
    {
        penaltySeconds = 0f;
        flashRemaining = 0f;

        if (penaltyRoot != null)
            penaltyRoot.SetActive(false);

        if (penaltyBackground != null)
            penaltyBackground.color = normalColor;
    }

    // Resets the current run without immediately starting it.
    public void ResetTimer()
    {
        isRunning = false;
        elapsedTime = 0f;

        ClearPenalty();
        RefreshTexts();
    }

    private void SaveBestTime()
    {
        if (!saveBestTime || string.IsNullOrWhiteSpace(bestTimeKey))
            return;

        PlayerPrefs.SetFloat(bestTimeKey + "_Time", bestTime);
        PlayerPrefs.SetInt(bestTimeKey + "_Exists", 1);
        PlayerPrefs.Save();
    }

    private static string FormatTime(float time)
    {
        time = Mathf.Max(0f, time);

        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int milliseconds = Mathf.FloorToInt(time * 1000f) % 1000;

        return string.Format(
            "{0:00}:{1:00}:{2:000}",
            minutes, seconds, milliseconds);
    }
}
