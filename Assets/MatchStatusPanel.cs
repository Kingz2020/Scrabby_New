using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;

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

    [SerializeField]
    private Transform contentParent;

    [SerializeField]
    private MatchStatusRow rowPrefab;

    private readonly List<MatchStatusRow> rows =
        new List<MatchStatusRow>();

    private DatabaseReference dbRoot;

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

    private void Start()
    {
        if (FirebaseInit.IsReady)
            dbRoot = FirebaseInit.Database.RootReference;
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
        if (!FirebaseInit.IsReady)
        {
            ShowStatus("Firebase not ready.");
            return;
        }

        var auth = FirebaseAuth.DefaultInstance;

        if (auth == null || auth.CurrentUser == null)
        {
            ShowStatus("Not logged in.");
            return;
        }

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    ShowStatus("Failed to load user.");
                    Debug.LogError(task.Exception);
                    return;
                }

                if (!task.Result.Exists)
                {
                    ShowStatus("User profile not found.");
                    return;
                }

                string json = task.Result.GetRawJsonValue();

                PreGamePanel.UserData user = JsonUtility.FromJson<PreGamePanel.UserData>(json);

                if (user == null)
                {
                    ShowStatus("User data invalid.");
                    return;
                }

                Debug.Log($"[MATCH STATUS] state={user.presenceState}");

                List<MatchListItemData> items =
                    new List<MatchListItemData>();

                foreach (string matchId in user.activeMatchIds)
                {
                    items.Add(
                        new MatchListItemData
                        {
                            matchId = matchId,
                            status = "Unknown"
                        });
                }

                BuildMatchList(items);
            });
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

    private void ClearRows()
    {
        foreach (var row in rows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }

        rows.Clear();
    }

    private void CreateRow(MatchListItemData data)
    {
        MatchStatusRow row =
            Instantiate(rowPrefab, contentParent);

        row.Setup(
            data,
            OnRowSelected);

        rows.Add(row);
    }

    private void OnRowSelected(
    string roomCode,
    string matchId)
    {
        Debug.Log(
            "[MATCH STATUS] Selected room=" +
            roomCode +
            " match=" +
            matchId);

        if (!string.IsNullOrEmpty(matchId))
        {
            preGamePanel.WatchMatch(matchId, false);
            return;
        }

        if (!string.IsNullOrEmpty(roomCode))
        {
            preGamePanel.WatchRoom(roomCode);
        }
    }
    private void BuildMatchList(
    List<MatchListItemData> items)
    {
        ClearRows();

        foreach (var item in items)
        {
            CreateRow(item);
        }

        if (items.Count == 0)
        {
            ShowStatus("No active games.");
        }
        else
        {
            ShowStatus(
                items.Count +
                " active matches");
        }
    }
}