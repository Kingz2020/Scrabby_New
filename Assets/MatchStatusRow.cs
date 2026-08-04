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

    private string roomCode;
    private string matchId;

    public void Setup(
        MatchListItemData data,
        System.Action<string, string> onAction)
    {
        roomCode = data.roomCode;
        matchId = data.matchId;

        opponentText.text = data.opponentDisplayName;
        statusText.text = data.status;

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

        actionButtonText.text = data.isRoom ? "Open" : "Resume";

        actionButton.onClick.RemoveAllListeners();

        actionButton.onClick.AddListener(() =>
        {
            onAction?.Invoke(roomCode, matchId);
        });
    }
}