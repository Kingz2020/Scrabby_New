using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchStatusPanel : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text matchInfoText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text roomCodeText;

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown playerCountDropdown;
    [SerializeField] private TMP_Dropdown roundCountDropdown;
    [SerializeField] private TMP_Dropdown timeModeDropdown;

    [Header("Buttons")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button resumeMatchButton;
    [SerializeField] private Button refreshButton;

    [SerializeField] private PreGamePanel preGamePanel;

    private void Awake()
    {
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateMatchPressed);

        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinMatchPressed);

        if (resumeMatchButton != null)
            resumeMatchButton.onClick.AddListener(OnResumePressed);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(OnRefreshPressed);
    }

    private void OnEnable()
    {
        ShowStatus("Checking for active matches...");
        RefreshMatchState();
    }

    public void ShowStatus(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }

    public void ShowMatchInfo(string text)
    {
        if (matchInfoText != null)
            matchInfoText.text = text;
    }

    public void SetRoomCode(string roomCode)
    {
        if (roomCodeText != null)
            roomCodeText.text = roomCode;
    }

    public int GetPlayerCount()
    {
        if (playerCountDropdown == null)
            return 2;

        switch (playerCountDropdown.value)
        {
            case 0: return 2;
            case 1: return 3;
            case 2: return 4;
            default: return 2;
        }
    }

    public int GetRoundCount()
    {
        if (roundCountDropdown == null)
            return 5;

        switch (roundCountDropdown.value)
        {
            case 0: return 4;
            case 1: return 5;
            case 2: return 6;
            case 3: return 7;
            default: return 5;
        }
    }

    public int GetTurnTimeMinutes()
    {
        if (timeModeDropdown == null)
            return 5;

        switch (timeModeDropdown.value)
        {
            case 0: return 5;      // Fast
            case 1: return 30;     // Normal
            case 2: return 1440;   // 24 hours
            default: return 5;
        }
    }

    private void OnCreateRoomPressed()
    {
        Debug.Log(
            "[MATCH STATUS] Create Room | Players=" +
            GetPlayerCount() +
            " Rounds=" +
            GetRoundCount() +
            " TurnTime=" +
            GetTurnTimeMinutes()
        );

        ShowStatus("Creating room...");
    }

    private void OnJoinRoomPressed()
    {
        Debug.Log("[MATCH STATUS] Join Room");

        ShowStatus("Joining room...");
    }

    private void OnResumeMatchPressed()
    {
        Debug.Log("[MATCH STATUS] Resume Match");

        ShowStatus("Loading match...");
    }


    private void RefreshMatchState()
    {
        Debug.Log("[MATCH STATUS] Refreshing match state");

        ShowStatus("Checking Firebase...");
    }

    public void OnCreateMatchPressed()
    {
        preGamePanel.OnCreateRoomPressed();
    }

    public void OnJoinMatchPressed()
    {
        preGamePanel.OnJoinRoomPressed();
    }

    public void OnRefreshPressed()
    {
        Debug.Log("[MATCH STATUS] Refresh requested");
        ShowStatus("Refresh not implemented yet");
    }

    public void OnResumePressed()
    {
        preGamePanel.TryResumeActiveMatch();
    }


}