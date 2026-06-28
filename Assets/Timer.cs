using TMPro;
using UnityEngine;

public class Timer : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private float roundDuration = 70f;

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
            Debug.Log("Time is up!");
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
}