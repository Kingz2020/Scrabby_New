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

    // Opening a match takes a moment - it reads the match, then asks whether
    // this round has already been played. A button that looks untouched for
    // those seconds invites a second tap, so it says what it is doing.
    public void ShowOpening()
    {
        if (actionButton != null)
        {
            actionButton.interactable = false;
            actionButton.image.color = new Color(0.98f, 0.82f, 0.36f);
        }

        if (actionButtonText != null)
            actionButtonText.text = "Opening...";
    }

    public void Setup(
    MatchListItemData data,
    System.Action<string, string, bool> onAction,
    System.Action<string> onDecline = null)
    {

        roomCode = data.roomCode;
        matchId = data.matchId;
        isCompleted = !data.isRoom && data.status == "completed";

        // Just the name. "Invited you" said again what the status column beside
        // it already says, and on a phone it was the part that ran out of room.
        opponentText.text = data.opponentDisplayName;
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
            data.isPendingInvite ? "Waiting..." :
            data.isInvite ? "Accept" :
            data.isRoom ? "Open" :
            isCompleted ? "View Results" :
            data.hasSubmittedThisRound ? "Waiting..." :
            "Resume";


        if (data.isPendingInvite)
        {
            // Nothing to do but wait for them. Shown, so it is plain the
            // invitation went through; not pressable, because there is nothing
            // behind it yet.
            actionButton.interactable = false;
            actionButton.image.color = Color.gray;
        }
        else if (data.isRoom || isCompleted || data.isInvite)
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
            ScrabbyLog.Trace(
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