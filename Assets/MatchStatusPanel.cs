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

    [SerializeField] private Transform contentParent;
    [SerializeField] private Transform invitesContentParent;

    [SerializeField] private MatchStatusRow rowPrefab;
    [SerializeField] private InviteRow inviteRowPrefab;

    private readonly List<MatchStatusRow> rows = new List<MatchStatusRow>();

    private readonly List<InviteRow> inviteRows = new List<InviteRow>();

    private DatabaseReference dbRoot;
    private FirebaseAuth auth;

    private DatabaseReference watchedRoomRef;
    private EventHandler<ValueChangedEventArgs> roomWatcher;
    private string currentlyWatchedMatchId;
    [SerializeField] private Transform completedContentParent;
    private DatabaseReference watchedUserRef;
    private EventHandler<ValueChangedEventArgs> userWatcher;

    //[SerializeField] private OnlineMatchController onlineMatchController;

    private void Awake()
    {
        Debug.Log("[WIRING CHECK] loginButton=" + (loginButton != null ? loginButton.name : "NULL") +
               " | logoutbutton=" + (logoutbutton != null ? logoutbutton.name : "NULL"));

        //if (onlineMatchController == null)
         //   onlineMatchController = Singleton.Instance.OnlineMatchController;

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

        StopWatchingUser();
    }

    private void OnAuthStateChanged(object sender, System.EventArgs e)
    {
        UpdateLoginNameDisplay();
    }

    private void OnEnable()
    {
        ShowStatus("Checking for active matches...");
        UpdateLoginNameDisplay();

        var authInstance = FirebaseAuth.DefaultInstance;

        if (authInstance != null && authInstance.CurrentUser != null)
        {
            string uid = authInstance.CurrentUser.UserId;

            // Start watching this user's record for changes
            WatchCurrentUser(uid);

            // Refresh matches for this user once
            RefreshMatchStateForUser(uid);
        }
        else
        {
            // No user signed in yet — fall back to legacy refresh
            RefreshMatchState();
        }
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

    private void OnDisable()
    {
        StopWatchingUser();
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

        WatchCurrentUser(uid);

        dbRoot.Child("users")
              .Child(uid)
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

    private IEnumerator LoadMatchList(
    string myUid,
    List<string> roomIds,
    List<string> matchIds)
    {
        List<MatchListItemData> activeItems = new List<MatchListItemData>();
        List<MatchListItemData> completedItems = new List<MatchListItemData>();
        List<MatchListItemData> inviteItems = new List<MatchListItemData>();

        //
        // ROOMS
        //
        foreach (string roomCode in roomIds)
        {
            var roomTask =
                dbRoot.Child("rooms")
                      .Child(roomCode)
                      .GetValueAsync();

            yield return new WaitUntil(() => roomTask.IsCompleted);

            if (roomTask.IsFaulted ||
                roomTask.Result == null ||
                !roomTask.Result.Exists)
            {
                continue;
            }

            RoomData room =
                JsonUtility.FromJson<RoomData>(
                    roomTask.Result.GetRawJsonValue());

            if (room == null)
                continue;

            string opponentName =
                room.hostUid == myUid
                ? room.guestDisplayName
                : room.hostDisplayName;

            if (string.IsNullOrEmpty(opponentName))
                opponentName = "(waiting)";

            activeItems.Add(
                new MatchListItemData
                {
                    isRoom = true,
                    roomCode = room.code,
                    opponentDisplayName = opponentName,
                    status = room.status
                });
        }

        //
        // MATCHES
        //
        foreach (string matchId in matchIds)
        {
            var matchTask =
                dbRoot.Child("matches")
                      .Child(matchId)
                      .GetValueAsync();

            yield return new WaitUntil(() => matchTask.IsCompleted);

            if (matchTask.IsFaulted ||
                matchTask.Result == null ||
                !matchTask.Result.Exists)
            {
                continue;
            }

            MatchData match =
                JsonUtility.FromJson<MatchData>(
                    matchTask.Result.GetRawJsonValue());

            if (match == null)
                continue;

            bool amPlayer1 =
                match.player1Uid == myUid;

            string opponentName =
                amPlayer1
                ? match.player2DisplayName
                : match.player1DisplayName;

            int myScore =
                amPlayer1
                ? match.player1Score
                : match.player2Score;

            int opponentScore =
                amPlayer1
                ? match.player2Score
                : match.player1Score;

            var itemData = new MatchListItemData
            {
                isRoom = false,
                matchId = match.matchId,
                roomCode = match.roomCode,
                opponentDisplayName = opponentName,
                status = match.status,
                currentRound = match.currentRoundNumber,
                totalRounds = match.totalRounds,
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

        //
        // INVITES
        //
        var invitesTask =
            dbRoot.Child("users")
                  .Child(myUid)
                  .Child("invites")
                  .GetValueAsync();

        yield return new WaitUntil(() => invitesTask.IsCompleted);

        if (!invitesTask.IsFaulted &&
            invitesTask.Result != null &&
            invitesTask.Result.Exists)
        {
            foreach (var child in invitesTask.Result.Children)
            {
                string raw = child.GetRawJsonValue();
                if (string.IsNullOrEmpty(raw))
                    continue;

                PreGamePanel.RoomInviteData invite =
                    JsonUtility.FromJson<PreGamePanel.RoomInviteData>(raw);

                if (invite == null)
                    continue;

                inviteItems.Add(new MatchListItemData
                {
                    roomCode = invite.roomCode,
                    opponentDisplayName = invite.fromDisplayName
                });
            }
        }

        BuildMatchList(activeItems, completedItems);
        BuildInviteRows(inviteItems);

        int total = activeItems.Count + completedItems.Count + inviteItems.Count;

        if (total == 0)
        {
            ShowStatus("No active games.");
        }
        else
        {
            ShowStatus($"{total} games found");
        }
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

        WatchCurrentUser(uid);

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
                  Debug.Log("[MATCH STATUS] user.activeRoomIds=" +
          (user.activeRoomIds == null ? "NULL" : string.Join(",", user.activeRoomIds)));

                  Debug.Log("[MATCH STATUS] user.activeMatchIds=" +
                            (user.activeMatchIds == null ? "NULL" : string.Join(",", user.activeMatchIds)));
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
            // Completed match: show final result
            Singleton.Instance.OnlineMatchController.ShowGameOverForMatch(matchId);
            return;
        }

        if (!string.IsNullOrEmpty(matchId))
        {
            // Active match: resume gameplay flow
            Singleton.Instance.OnlineMatchController.ResumeMatch(matchId);
            return;
        }

        // No match yet, but room exists: just watch the room as before
        if (!string.IsNullOrEmpty(roomCode))
        {
            preGamePanel.WatchRoom(roomCode);
        }
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

    private void CreateRow(MatchListItemData data, bool isCompleted, Transform parent)
    {
        MatchStatusRow row = Instantiate(rowPrefab, parent);
        row.Setup(data, OnRowSelected, OnInviteDeclined);
        rows.Add(row);
    }

    private void BuildInviteRows(List<MatchListItemData> inviteItems)
    {
        foreach (var row in inviteRows)
            if (row != null) Destroy(row.gameObject);
        inviteRows.Clear();

        foreach (var item in inviteItems)
        {
            InviteRow row = Instantiate(inviteRowPrefab, invitesContentParent);
            row.Setup(item, OnInviteAccepted, OnInviteDeclined);
            inviteRows.Add(row);
        }
    }

    private void OnInviteAccepted(string roomCode)
    {
        preGamePanel.AcceptRoomInvite(roomCode);
    }

    private void OnInviteDeclined(string roomCode)
    {
        preGamePanel.DeclineRoomInvite(roomCode);
    }

    public void ForceRefresh()
    {
        RefreshMatchState();
    }

    private void WatchCurrentUser(string uid)
    {
        StopWatchingUser();

        if (string.IsNullOrEmpty(uid) || dbRoot == null)
            return;

        watchedUserRef = dbRoot.Child("users").Child(uid);

        userWatcher = (sender, args) =>
        {
            if (!isActiveAndEnabled)      // or !gameObject.activeInHierarchy
                return;

            if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists)
                return;

            string json = args.Snapshot.GetRawJsonValue();
            PreGamePanel.UserData user = JsonUtility.FromJson<PreGamePanel.UserData>(json);
            if (user == null)
                return;

            StartCoroutine(LoadMatchList(uid, user.activeRoomIds, user.activeMatchIds));
        };

        watchedUserRef.ValueChanged += userWatcher;
    }

    private void StopWatchingUser()
    {
        if (watchedUserRef != null && userWatcher != null)
            watchedUserRef.ValueChanged -= userWatcher;

        watchedUserRef = null;
        userWatcher = null;
    }
}