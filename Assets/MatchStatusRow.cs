using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchStatusRow : MonoBehaviour
{
    [SerializeField] private TMP_Text opponentText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionButtonText;
    [SerializeField] private TMP_Text roomCodeText;

    private string roomCode;
    private string matchId;
    private bool isCompleted;

    public void Setup(
    MatchListItemData data,
    System.Action<string, string, bool> onAction)
    {
        roomCode = data.roomCode;
        matchId = data.matchId;
        isCompleted = !data.isRoom && data.status == "completed";

        opponentText.text = data.opponentDisplayName;
        statusText.text = data.status;

        if (roomCodeText != null)
            roomCodeText.text = data.roomCode;

        if (data.isRoom)
        {
            roundText.text = "-";
            scoreText.text = "-";
        }
        else
        {
            roundText.text =
                data.currentRound + "/" + data.totalRounds;

            scoreText.text =
                data.myScore + "-" + data.opponentScore;
        }

        bool canResume = !data.isRoom && !isCompleted && !data.hasSubmittedThisRound;

        actionButtonText.text =
            data.isRoom ? "Open" :
            isCompleted ? "View Results" :
            data.hasSubmittedThisRound ? "Waiting..." :
            "Resume";

        if (data.isRoom || isCompleted)
        {
            // Rooms ("Open") and completed matches ("View Results") are always tappable
            actionButton.interactable = true;
            actionButton.image.color = Color.white;
        }
        else
        {
            actionButton.interactable = canResume;
            actionButton.image.color = canResume ? Color.green : Color.gray;
        }

        actionButton.onClick.RemoveAllListeners();

        actionButton.onClick.AddListener(() =>
        {
            onAction?.Invoke(roomCode, matchId, isCompleted);
        });
    }
}