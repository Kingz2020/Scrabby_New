using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchRowUI : MonoBehaviour
{
    public TMP_Text opponentText;
    public TMP_Text statusText;
    public TMP_Text roundText;

    public Button actionButton;
    public TMP_Text actionButtonText;

    private string matchId;

    public void Setup(
        string matchId,
        string opponent,
        string status,
        string roundInfo,
        string buttonText)
    {
        this.matchId = matchId;

        opponentText.text = opponent;
        statusText.text = status;
        roundText.text = roundInfo;

        actionButtonText.text = buttonText;
    }

    public string GetMatchId()
    {
        return matchId;
    }
}