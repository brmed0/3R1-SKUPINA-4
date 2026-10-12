using UnityEngine;
using TMPro;

// Counts up and displays time as MM:SS:mmm (e.g. 00:25:275)
public class StopwatchTimer : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;   // drag your TextMeshPro text here
    [SerializeField] private bool startOnAwake = true;

    private float elapsedTime;
    private bool running;

    void Start()
    {
        running = startOnAwake;
        UpdateDisplay();
    }

    void Update()
    {
        if (!running) return;

        elapsedTime += Time.deltaTime;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        int minutes = (int)(elapsedTime / 60f);
        int seconds = (int)(elapsedTime % 60f);
        int milliseconds = (int)((elapsedTime * 1000f) % 1000f);

        timerText.text = string.Format("{0:00}:{1:00}:{2:000}", minutes, seconds, milliseconds);
    }

    public void StartTimer() { running = true; }
    public void StopTimer() { running = false; }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateDisplay();
    }
}