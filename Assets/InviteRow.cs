using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InviteRow : MonoBehaviour
{
    [SerializeField] private TMP_Text fromText;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;

    private string roomCode;

    public void Setup(
        MatchListItemData data,
        System.Action<string> onAccept,
        System.Action<string> onDecline)
    {
        roomCode = data.roomCode;

        fromText.text = data.opponentDisplayName + " invited you";
        if (roomCodeText != null)
            roomCodeText.text =  data.roomCode;

        acceptButton.onClick.RemoveAllListeners();
        acceptButton.onClick.AddListener(() => onAccept?.Invoke(roomCode));

        declineButton.onClick.RemoveAllListeners();
        declineButton.onClick.AddListener(() => onDecline?.Invoke(roomCode));
    }
}