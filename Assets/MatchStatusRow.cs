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

    [SerializeField] private Button declineButton;

    private string roomCode;
    private string matchId;
    private bool isCompleted;

    public void Setup(
    MatchListItemData data,
    System.Action<string, string, bool> onAction,
    System.Action<string> onDecline = null)
    {

        roomCode = data.roomCode;
        matchId = data.matchId;
        isCompleted = !data.isRoom && data.status == "completed";

        opponentText.text = data.isInvite ? (data.opponentDisplayName + " invited you") : data.opponentDisplayName;
        statusText.text = data.isInvite ? "Invite" : data.status;

        if (data.isRoom || data.isInvite)
        {
            roundText.text = "-";
            scoreText.text = "-";
        }
        else
        {
            roundText.text = data.currentRound + "/" + data.totalRounds;
            scoreText.text = data.myScore + "-" + data.opponentScore;
        }

        if (roomCodeText != null)
            roomCodeText.text = data.roomCode;

        bool canResume = !data.isRoom && !data.isInvite && !isCompleted && !data.hasSubmittedThisRound;
        //bool canResume = !data.isRoom && !data.isInvite && !isCompleted;


        actionButtonText.text =
            data.isInvite ? "Accept" :
            data.isRoom ? "Open" :
            isCompleted ? "View Results" :
            data.hasSubmittedThisRound ? "Waiting..." :
            "Resume";


        if (data.isRoom || isCompleted || data.isInvite)
        {
            actionButton.interactable = true;
            actionButton.image.color = data.isInvite ? Color.green : Color.white;
        }
        else
        {
            actionButton.interactable = canResume;
            actionButton.image.color = canResume ? Color.green : Color.gray;
        }


        actionButton.onClick.RemoveAllListeners();

        actionButton.onClick.AddListener(() =>
        {
            Debug.Log(
                "[MATCH ROW CLICK] roomCode=" + roomCode +
                " | matchId=" + matchId +
                " | isCompleted=" + isCompleted
            );

            onAction?.Invoke(roomCode, matchId, isCompleted);
        });

        if (declineButton != null)
        {
            declineButton.gameObject.SetActive(data.isInvite);
            declineButton.onClick.RemoveAllListeners();
            if (data.isInvite)
                declineButton.onClick.AddListener(() => onDecline?.Invoke(roomCode));
        }
    }
}