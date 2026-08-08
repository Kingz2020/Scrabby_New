using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using System;

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
    [SerializeField] private Button switchUserButton;

    [SerializeField] private Button loginButton;
    [SerializeField] private Button logoutbutton;
    [SerializeField] private TMP_Text loginnameText;

    [SerializeField] private PreGamePanel preGamePanel;

    [SerializeField]
    private Transform contentParent;

    [SerializeField]
    private MatchStatusRow rowPrefab;

    private readonly List<MatchStatusRow> rows =
        new List<MatchStatusRow>();

    private DatabaseReference dbRoot;
    private FirebaseAuth auth;

    private DatabaseReference watchedRoomRef;
    private EventHandler<ValueChangedEventArgs> roomWatcher;
    private string currentlyWatchedMatchId;
    [SerializeField] private Transform completedContentParent;
    private void Awake()
    {
        Debug.Log("[WIRING CHECK] loginButton=" + (loginButton != null ? loginButton.name : "NULL") +
               " | logoutbutton=" + (logoutbutton != null ? logoutbutton.name : "NULL"));

        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateMatchPressed);

        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinMatchPressed);

        if (resumeMatchButton != null)
            resumeMatchButton.onClick.AddListener(OnResumePressed);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(OnRefreshPressed);

        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginButtonPressed);

        if (logoutbutton != null)
            logoutbutton.onClick.AddListener(OnLogoutButtonPressed);

        if (switchUserButton != null)
            switchUserButton.onClick.AddListener(() => preGamePanel.OnSwitchTestUserPressed());
    }

    private void Start()
    {
        StartCoroutine(WaitForFirebaseThenInit());
    }

    private IEnumerator WaitForFirebaseThenInit()
    {
        yield return new WaitUntil(() => FirebaseInit.IsReady);
        dbRoot = FirebaseInit.Database.RootReference;
        auth = FirebaseInit.Auth;

        auth.StateChanged += OnAuthStateChanged;
        UpdateLoginNameDisplay();
    }

    private void OnDestroy()
    {
        if (auth != null)
            auth.StateChanged -= OnAuthStateChanged;
    }

    private void OnAuthStateChanged(object sender, System.EventArgs e)
    {
        UpdateLoginNameDisplay();
    }

    private void OnEnable()
    {
        ShowStatus("Checking for active matches...");
        UpdateLoginNameDisplay();
        RefreshMatchState();
    }

    public void ShowStatus(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }

    public void OnLoginButtonPressed()
    {
        if (preGamePanel == null)
        {
            Debug.LogWarning("[MATCH STATUS] preGamePanel reference not assigned.");
            return;
        }

        preGamePanel.OnLoginPressed();
    }

    public void OnLogoutButtonPressed()
    {
        Debug.Log("[MATCH STATUS] Logout button pressed");

        if (preGamePanel == null)
        {
            Debug.LogWarning("[MATCH STATUS] preGamePanel reference not assigned.");
            return;
        }

        preGamePanel.OnLogoutPressed();
        UpdateLoginNameDisplay();
    }

    public void UpdateLoginNameDisplay()
    {
        if (loginnameText == null)
            return;

        var user = auth != null ? auth.CurrentUser : null;

        if (user == null)
        {
            loginnameText.text = "Not signed in";
            return;
        }

        string shownName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email
            : user.DisplayName;

        loginnameText.text = "Signed in as: " + shownName;
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
        if (roomCodeText == null)
        {
            Debug.LogWarning("[MATCH STATUS] roomCodeText not assigned.");
            return;
        }

        string roomCode = roomCodeText.text.Trim().ToUpper();

        Debug.Log("[MATCH STATUS] OnJoinRoomPressed — raw input='" + roomCodeText.text +
                  "' | normalized roomCode='" + roomCode + "' | length=" + roomCode.Length);

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            ShowStatus("Enter a room code.");
            return;
        }

        if (preGamePanel == null)
        {
            Debug.LogWarning("[MATCH STATUS] preGamePanel reference not assigned.");
            return;
        }

        // Sync the code into PreGamePanel's input field
        preGamePanel.SetRoomCodeInput(roomCode);

        // Now call the actual join logic
        preGamePanel.OnJoinRoomPressed();
    }

    private void OnResumeMatchPressed()
    {
        Debug.Log("[MATCH STATUS] Resume Match");

        ShowStatus("Loading match...");
    }


    private void RefreshMatchState()
    {
        Debug.Log("[MATCH STATUS] RefreshMatchState (legacy) called");
        // Self-heal: OnEnable can fire before Start()'s Firebase-wait coroutine finishes
        // (e.g. this panel gets enabled the same frame the scene loads).
        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
            }
            else
            {
                ShowStatus("Firebase not ready.");
                StartCoroutine(RetryRefreshWhenReady());
                return;
            }
        }

        UpdateLoginNameDisplay();

        var auth = FirebaseAuth.DefaultInstance;

        if (auth == null || auth.CurrentUser == null)
        {
            ShowStatus("Not logged in.");
            return;
        }

        string uid = auth.CurrentUser.UserId;

        Debug.Log("[MATCH STATUS] uid=" + uid);
        Debug.Log("[MATCH STATUS] path=users/" + uid);

        dbRoot.Child("users")
              .Child(uid)
              .GetValueAsync()
              .ContinueWithOnMainThread(task =>
              {
                  Debug.Log("[MATCH STATUS] Exists = " + (task.Result != null && task.Result.Exists));

                  if (task.IsFaulted)
                  {
                      ShowStatus("Failed to load user.");
                      Debug.LogError(task.Exception);
                      return;
                  }

                  Debug.Log(
                    "[MATCH STATUS] Exists = " +
                        task.Result.Exists);

                  Debug.Log(
                      "[MATCH STATUS] Key = " +
                      task.Result.Key);

                  Debug.Log(
                      "[MATCH STATUS] Raw JSON = " +
                      task.Result.GetRawJsonValue());


                  if (!task.Result.Exists)
                  {


                      ShowStatus("User profile not found.");
                      return;
                  }

                  string json = task.Result.GetRawJsonValue();

                  PreGamePanel.UserData user =
                      JsonUtility.FromJson<PreGamePanel.UserData>(json);

                  if (user == null)
                  {
                      ShowStatus("User data invalid.");
                      return;
                  }

                  StartCoroutine(
                      LoadMatchList(
                          uid,
                          user.activeRoomIds,
                          user.activeMatchIds));
              });
    }

    private IEnumerator RetryRefreshWhenReady()
    {
        yield return new WaitUntil(() => FirebaseInit.IsReady && FirebaseInit.Database != null);
        dbRoot = FirebaseInit.Database.RootReference;
        RefreshMatchState();
    }
    private IEnumerator LoadMatchList(string myUid, List<string> roomIds, List<string> matchIds)
    {
        List<MatchListItemData> activeItems = new List<MatchListItemData>();
        List<MatchListItemData> completedItems = new List<MatchListItemData>();

        // ROOMS
        foreach (string roomCode in roomIds)
        {
            var roomTask = dbRoot.Child("rooms").Child(roomCode).GetValueAsync();
            yield return new WaitUntil(() => roomTask.IsCompleted);

            if (roomTask.IsFaulted || roomTask.Result == null || !roomTask.Result.Exists)
                continue;

            RoomData room = JsonUtility.FromJson<RoomData>(roomTask.Result.GetRawJsonValue());
            if (room == null) continue;

            string opponentName = room.hostUid == myUid ? room.guestDisplayName : room.hostDisplayName;
            if (string.IsNullOrEmpty(opponentName)) opponentName = "(waiting)";

            activeItems.Add(new MatchListItemData
            {
                isRoom = true,
                roomCode = room.code,
                opponentDisplayName = opponentName,
                status = room.status
            });
        }

        // MATCHES
        foreach (string matchId in matchIds)
        {
            var matchTask = dbRoot.Child("matches").Child(matchId).GetValueAsync();
            yield return new WaitUntil(() => matchTask.IsCompleted);

            if (matchTask.IsFaulted || matchTask.Result == null || !matchTask.Result.Exists)
                continue;

            MatchData match = JsonUtility.FromJson<MatchData>(matchTask.Result.GetRawJsonValue());
            if (match == null) continue;

            bool amPlayer1 = match.player1Uid == myUid;
            string opponentName = amPlayer1 ? match.player2DisplayName : match.player1DisplayName;
            int myScore = amPlayer1 ? match.player1Score : match.player2Score;
            int opponentScore = amPlayer1 ? match.player2Score : match.player1Score;

            var itemData = new MatchListItemData
            {
                isRoom = false,
                matchId = match.matchId,
                roomCode = match.roomCode,
                opponentDisplayName = opponentName,
                status = match.status,
                currentRound = match.currentRoundNumber,
                myScore = myScore,
                opponentScore = opponentScore
            };

            if (match.status != "completed")
            {
                var subTask = dbRoot.Child("matches").Child(match.matchId)
                    .Child("rounds").Child(match.currentRoundNumber.ToString())
                    .Child("submissions").Child(myUid)
                    .GetValueAsync();

                yield return new WaitUntil(() => subTask.IsCompleted);

                itemData.hasSubmittedThisRound =
                    !subTask.IsFaulted && subTask.Result != null && subTask.Result.Exists;
            }

            if (match.status == "completed")
                completedItems.Add(itemData);
            else
                activeItems.Add(itemData);
        }

        BuildMatchList(activeItems, completedItems);
    }
    public void RefreshMatchStateForUser(string uid)
    {
        Debug.Log("[MATCH STATUS] RefreshMatchStateForUser called with uid=" + uid);

        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
            }
            else
            {
                ShowStatus("Firebase not ready.");
                StartCoroutine(RetryRefreshForUserWhenReady(uid));
                return;
            }
        }

        UpdateLoginNameDisplay();

        if (string.IsNullOrEmpty(uid))
        {
            ShowStatus("Not logged in.");
            return;
        }

        dbRoot.Child("users")
              .Child(uid)
              .GetValueAsync()
              .ContinueWithOnMainThread(task =>
              {
                  if (task.IsFaulted || task.Result == null || !task.Result.Exists)
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

                  StartCoroutine(LoadMatchList(uid, user.activeRoomIds, user.activeMatchIds));
              });
    }

    private IEnumerator RetryRefreshForUserWhenReady(string uid)
    {
        yield return new WaitUntil(() => FirebaseInit.IsReady && FirebaseInit.Database != null);
        dbRoot = FirebaseInit.Database.RootReference;
        RefreshMatchStateForUser(uid);
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
        ShowStatus("Checking for active matches...");
        RefreshMatchState();
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

    private void OnRowSelected(string roomCode, string matchId, bool isCompleted)
    {
        if (isCompleted)
        {
            preGamePanel.ShowGameOverForMatch(matchId);
            return;
        }

        if (!string.IsNullOrEmpty(matchId))
        {
            preGamePanel.WatchMatch(matchId, true);
            return;
        }

        if (!string.IsNullOrEmpty(roomCode))
            preGamePanel.WatchRoom(roomCode);
    }

    private void BuildMatchList(List<MatchListItemData> activeItems, List<MatchListItemData> completedItems)
    {
        ClearRows();

        foreach (var item in activeItems)
            CreateRow(item, false);

        foreach (var item in completedItems)
            CreateRow(item, true);

        int total = activeItems.Count + completedItems.Count;
        ShowStatus(total == 0 ? "No active games." : total + " games found");
    }

    private void CreateRow(MatchListItemData data, bool isCompleted)
    {
        MatchStatusRow row = Instantiate(rowPrefab, isCompleted ? completedContentParent : contentParent);
        row.Setup(data, OnRowSelected);
        rows.Add(row);
    }
}