using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundReplayRow : MonoBehaviour
{
    [SerializeField] private Button replayButton;
    [SerializeField] private TextMeshProUGUI roundText;

    private OnlineRoundHistoryEntry roundData;
    private System.Action<OnlineRoundHistoryEntry> onReplay;

    public void Setup(
        OnlineRoundHistoryEntry entry,
        string rowText,
        System.Action<OnlineRoundHistoryEntry> replayAction)
    {
        roundData = entry;
        onReplay = replayAction;

        if (roundText != null)
            roundText.text = rowText;

        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();

            replayButton.onClick.AddListener(() =>
            {
                if (roundData != null)
                    onReplay?.Invoke(roundData);
            });
        }
    }
}