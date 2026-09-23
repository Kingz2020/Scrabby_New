using TMPro;
using UnityEngine;

public class Timer : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private float roundDuration = 70f;

    public void SetTextReference(TextMeshProUGUI timer)
    {
        timerText = timer;
    }

    // Raised once, the moment the clock reaches zero while running. Whoever
    // is running the round decides what that costs.
    public System.Action TimeRanOut;

    private float remainingTime;
    private bool timerRunning;

    void Start()
    {
        if (timerText == null)
        {
            timerText = GetComponent<TextMeshProUGUI>();
        }

        ResetTimer();
        // StopTimer();
    }

    void Update()
    {
        if (!timerRunning)
            return;

        remainingTime -= Time.deltaTime;

        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            timerRunning = false;
            ScrabbyLog.Trace("Time is up!");

            // Said out loud rather than only written to the log: running out
            // used to cost nothing at all, so the clock was decoration.
            if (TimeRanOut != null)
                TimeRanOut();
        }

        UpdateTimerDisplay();
    }

    public void StartTimer()
    {
        remainingTime = roundDuration;
        timerRunning = true;
        UpdateTimerDisplay();
    }

    public void StopTimer()
    {
        timerRunning = false;
    }

    public void ResetTimer()
    {
        remainingTime = roundDuration;
        UpdateTimerDisplay();
    }

    public float GetRemainingTime()
    {
        return remainingTime;
    }

    public float GetRoundDuration()
    {
        return roundDuration;
    }

    private void UpdateTimerDisplay()
    {
        int seconds = Mathf.CeilToInt(remainingTime);
        timerText.text = seconds.ToString();
    }

    public void ResumeTimer()
    {
        if (remainingTime <= 0f)
            return;

        timerRunning = true;
        UpdateTimerDisplay();
    }
}