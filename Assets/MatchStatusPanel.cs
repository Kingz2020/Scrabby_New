using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using System;

public partial class MatchStatusPanel : MonoBehaviour
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

    [SerializeField] private TMP_InputField inviteInput;
    [SerializeField] private Button inviteButton;

    [Header("One-card flow")]
    // Arriving here to resume a match and arriving to start one are different
    // tasks, so they are different tabs. Leave these unassigned and the panel
    // keeps its old three-list layout.
    [SerializeField] private Button matchesTabButton;
    [SerializeField] private Button newMatchTabButton;
    [SerializeField] private GameObject matchesSection;
    [SerializeField] private GameObject newMatchSection;

    // Login, Logout and Switch user duplicate the pregame card, and this panel
    // can only be reached signed in. They are switched off rather than deleted,
    // so the references stay valid.
    [SerializeField] private GameObject[] identityControls;

    [Header("Tab look")]
    [SerializeField] private Sprite tabSelectedSprite;
    [SerializeField] private Sprite tabIdleSprite;
    [SerializeField] private Color tabSelectedColour = new Color(0.945f, 0.878f, 0.733f, 1f);
    [SerializeField] private Color tabIdleColour = new Color(0.039f, 0.149f, 0.267f, 0.30f);
    [SerializeField] private Color tabSelectedTextColour = new Color(0.227f, 0.173f, 0.094f, 1f);
    [SerializeField] private Color tabIdleTextColour = new Color(1f, 1f, 1f, 0.85f);

    private bool showingNewMatch;

    private bool OneCard
    {
        get { return matchesTabButton != null || newMatchTabButton != null; }
    }

    //[SerializeField] private OnlineMatchController onlineMatchController;

    private void Awake()
    {
        if (matchesTabButton != null)
            matchesTabButton.onClick.AddListener(ShowMatchesTab);

        if (newMatchTabButton != null)
            newMatchTabButton.onClick.AddListener(ShowNewMatchTab);

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

        if (inviteButton != null)
            inviteButton.onClick.AddListener(OnInviteButtonPressed);

        WireQuickGame();
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
        HideIdentityControls();
        ShowMatchesTab();

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

        // Leaving the panel stops the search, so nobody is left sitting in the
        // queue from a screen they have walked away from. If a match is already
        // starting this does nothing to it - the room is left alone once
        // somebody has joined.
        CancelQuickGame(false);
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

        //emailInput.SetTextWithoutNotify(signedInUser.Email ?? "");
        //emailInput.ForceLabelUpdate();



        loginnameText.text = "Signed in as: " + shownName;
    }

    public void ShowMatchInfo(string text)
    {
        if (matchInfoText != null)
            matchInfoText.text = text;
    }

    /*public void SetRoomCode(string roomCode)
    {
        if (roomCodeText != null)
            roomCodeText.text = roomCode;
    }*/

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
            return 4;

        switch (roundCountDropdown.value)
        {
            case 0: return 2;
            case 1: return 4;
            case 2: return 6;
            default: return 4;
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

        // Invitations this player sent that were turned down. Taken off the
        // list, and off the player's rooms so they are not fetched again.
        List<string> declinedRooms = new List<string>();
        List<string> declinedBy = new List<string>();

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

            bool hosting = room.hostUid == myUid;

            string opponentName = hosting
                ? room.guestDisplayName
                : room.hostDisplayName;

            // Sent by this player, and nobody has joined yet: say who it is
            // waiting on, rather than a bare "(waiting)".
            bool sentByMe = hosting &&
                            string.IsNullOrEmpty(room.guestUid) &&
                            !string.IsNullOrEmpty(room.invitedDisplayName);

            bool declined = sentByMe && room.status == "declined";
            bool pending = sentByMe && !declined;

            // A declined invitation has nothing left to show. It is dropped
            // here rather than drawn, and said once in the status line, so it
            // does not simply vanish without explanation.
            if (declined)
            {
                declinedRooms.Add(room.code);
                declinedBy.Add(room.invitedDisplayName);
                continue;
            }

            if (string.IsNullOrEmpty(opponentName))
                opponentName = sentByMe ? room.invitedDisplayName : "(waiting)";

            activeItems.Add(
                new MatchListItemData
                {
                    isRoom = true,
                    isPendingInvite = pending,
                    roomCode = room.code,
                    opponentDisplayName = opponentName,
                    status = pending ? "Waiting" : room.status
                });
        }

        //
        // MATCHES
        //
        foreach (string matchId in matchIds)
        {
            Debug.Log(
                "[MATCH LIST] Loading match"
                + " | uid=" + myUid
                + " | matchId=" + matchId
            );

            Firebase.Database.DataSnapshot matchSnapshot = null;
            bool matchFetchFailed = false;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                var matchTask =
                    dbRoot.Child("matches")
                          .Child(matchId)
                          .GetValueAsync();

                yield return new WaitUntil(() => matchTask.IsCompleted);

                if (matchTask.IsFaulted)
                {
                    matchFetchFailed = true;

                    Debug.LogWarning(
                        "[MATCH LIST] Match fetch faulted"
                        + " | matchId=" + matchId
                        + " | attempt=" + (attempt + 1)
                    );

                    break;
                }

                if (matchTask.Result != null && matchTask.Result.Exists)
                {
                    matchSnapshot = matchTask.Result;

                    Debug.Log(
                        "[MATCH LIST] Match became available"
                        + " | matchId=" + matchId
                        + " | attempt=" + (attempt + 1)
                    );

                    break;
                }

                Debug.Log(
                    "[MATCH LIST] Match not available yet; retrying"
                    + " | matchId=" + matchId
                    + " | attempt=" + (attempt + 1)
                );

                yield return new WaitForSeconds(0.25f);
            }

            if (matchFetchFailed || matchSnapshot == null)
            {
                Debug.LogWarning(
                    "[MATCH LIST] Skipping match after retries"
                    + " | matchId=" + matchId
                );

                continue;
            }

            string rawMatchJson = matchSnapshot.GetRawJsonValue();

            Debug.Log(
                "[MATCH LIST] Match JSON"
                + " | matchId=" + matchId
                + " | length=" + (rawMatchJson == null ? 0 : rawMatchJson.Length)
            );

            MatchData match =
                JsonUtility.FromJson<MatchData>(rawMatchJson);

            if (match == null)
            {
                Debug.LogError(
                    "[MATCH LIST] Match JSON failed to parse"
                    + " | matchId=" + matchId
                );

                continue;
            }

            Debug.Log(
                "[MATCH LIST] Match parsed"
                + " | matchId=" + match.matchId
                + " | status=" + match.status
                + " | round=" + match.currentRoundNumber
                + " | player1=" + match.player1Uid
                + " | player2=" + match.player2Uid
            );

        
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

                float timeoutAt = Time.realtimeSinceStartup + 5f;

                yield return new WaitUntil(() =>
                    subTask.IsCompleted || Time.realtimeSinceStartup >= timeoutAt
                );

                bool submissionExists = false;

                if (subTask.IsCompleted &&
                    !subTask.IsFaulted &&
                    subTask.Result != null &&
                    subTask.Result.Exists)
                {
                    submissionExists = true;
                }
                else if (!subTask.IsCompleted)
                {
                    Debug.LogWarning(
                        "[MATCH STATUS] Submission check timed out" +
                        " | uid=" + myUid +
                        " | matchId=" + match.matchId +
                        " | round=" + match.currentRoundNumber
                    );
                }
                else if (subTask.IsFaulted)
                {
                    Debug.LogWarning(
                        "[MATCH STATUS] Submission check faulted" +
                        " | uid=" + myUid +
                        " | matchId=" + match.matchId +
                        " | round=" + match.currentRoundNumber +
                        " | error=" + subTask.Exception
                    );
                }

               /* Debug.Log(
                    "[MATCH STATUS] Per-user submission check" +
                    " | uid=" + myUid +
                    " | matchId=" + match.matchId +
                    " | round=" + match.currentRoundNumber +
                    " | completed=" + subTask.IsCompleted +
                    " | exists=" + submissionExists
                );
               */
                itemData.hasSubmittedThisRound = submissionExists;
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
            Debug.Log("[MATCH STATUS] Invites node exists, children count = " +
              invitesTask.Result.ChildrenCount);

            foreach (var child in invitesTask.Result.Children)
            {
                Debug.Log("[MATCH STATUS] Invite child key = " + child.Key);
                Debug.Log("[MATCH STATUS] Invite raw JSON = " + child.GetRawJsonValue());

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
                    opponentDisplayName = invite.fromDisplayName,
                    isInvite = true
                });
            }
        }
        /*else
        {
            Debug.Log("[MATCH STATUS] No invites found or error: " +
                      (invitesTask.IsFaulted ? invitesTask.Exception?.ToString() : "none"));
        }
        */
        BuildMatchList(activeItems, completedItems, inviteItems);

        int total = activeItems.Count + completedItems.Count + inviteItems.Count;

        if (total == 0)
        {
            ShowStatus("No active games.");
        }
        else
        {
            ShowStatus($"{total} games found");
        }

        if (declinedRooms.Count > 0)
        {
            ShowStatus(declinedRooms.Count == 1
                ? declinedBy[0] + " declined your invitation."
                : declinedRooms.Count + " invitations were declined.");

            RemoveRoomsFromCurrentUser(declinedRooms);
        }
    }

    // The counterpart to AddRoomToCurrentUser. Only the player can write their
    // own rooms, so the one who declined could not tidy this up - it happens
    // here, the next time the sender's list is loaded.
    private void RemoveRoomsFromCurrentUser(List<string> roomCodes)
    {
        if (auth == null || auth.CurrentUser == null || dbRoot == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid).Child("activeRoomIds").GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted || task.Result == null)
                    return;

                List<string> roomIds = new List<string>();

                foreach (DataSnapshot child in task.Result.Children)
                {
                    string code = child.Value != null ? child.Value.ToString() : null;

                    if (!string.IsNullOrEmpty(code) && !roomCodes.Contains(code))
                        roomIds.Add(code);
                }

                dbRoot.Child("users").Child(uid).Child("activeRoomIds")
                    .SetValueAsync(roomIds);
            });
    }


    public void RefreshMatchStateForUser(string uid)
    {
        //ClearRows();
        //ShowStatus("Loading matches...");

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
        Debug.Log(
                    "[MATCH STATUS ROW SELECTED] roomCode=" + roomCode +
                    " | matchId=" + matchId +
                    " | isCompleted=" + isCompleted
                );
        if (isCompleted)
        {
            // Completed match: show final result
            //if (matchStatusPanel.gameobject != null)
            //    matchStatusPanel.gameobject.SetActive(false);

            Singleton.Instance.OnlineMatchController.ShowGameOverForMatchId(matchId);
            return;
        }

        if (!string.IsNullOrEmpty(matchId))
        {
            Debug.Log(
                        "[MATCH STATUS] Calling ResumeMatch for matchId=" + matchId
                    );
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


    private void BuildMatchList(
    List<MatchListItemData> activeItems,
    List<MatchListItemData> completedItems,
    List<MatchListItemData> inviteItems = null)
    {
        Debug.Log(
            "[MATCH LIST BUILD]"
            + " active=" + activeItems.Count
            + " completed=" + completedItems.Count
            + " rowsBeforeClear=" + rows.Count
            + " activeSubmitted="
            + (activeItems.Count > 0
                ? activeItems[0].hasSubmittedThisRound.ToString()
                : "n/a")
        );

        ClearRows();
        ClearInviteRows();

        if (OneCard)
        {
            BuildOneList(activeItems, completedItems, inviteItems);
            return;
        }

        foreach (var item in activeItems)
        {
            Debug.Log(
                "[MATCH LIST CREATE ACTIVE]"
                + " matchId=" + item.matchId
                + " submitted=" + item.hasSubmittedThisRound
            );

            CreateRow(item, false);
        }

        foreach (var item in completedItems)
            CreateRow(item, true);

        if (inviteItems != null)
            BuildInviteRows(inviteItems);

        int total = activeItems.Count + completedItems.Count
                    + (inviteItems != null ? inviteItems.Count : 0);
        ShowStatus(total == 0 ? "No active games." : total + " games found");
    }

    // An invite is a match you have not accepted and a finished game is one you
    // cannot act on, so all three belong in the same list. The order is what
    // needs you first: your move, then theirs, then invites, then done.
    private void BuildOneList(
        List<MatchListItemData> activeItems,
        List<MatchListItemData> completedItems,
        List<MatchListItemData> inviteItems)
    {
        int waiting = 0;

        foreach (var item in activeItems)
        {
            if (!item.hasSubmittedThisRound)
                CreateRow(item, false, contentParent);
            else
                waiting++;
        }

        if (waiting > 0)
        {
            foreach (var item in activeItems)
            {
                if (item.hasSubmittedThisRound)
                    CreateRow(item, false, contentParent);
            }
        }

        if (inviteItems != null)
        {
            foreach (var item in inviteItems)
                CreateRow(item, false, contentParent);
        }

        foreach (var item in completedItems)
            CreateRow(item, true, contentParent);

        int total = activeItems.Count + completedItems.Count
                    + (inviteItems != null ? inviteItems.Count : 0);

        ShowStatus(total == 0
            ? "No games yet."
            : (activeItems.Count - waiting) + " waiting on you, " + total + " in all");
    }

    private void ClearInviteRows()
    {
        foreach (var row in inviteRows)
            if (row != null)
                Destroy(row.gameObject);

        inviteRows.Clear();
    }

    // ------------------------------------------------------------- tabs --
    public void ShowMatchesTab()
    {
        showingNewMatch = false;
        ApplyTab();
    }

    public void ShowNewMatchTab()
    {
        showingNewMatch = true;
        ApplyTab();
    }

    private void ApplyTab()
    {
        if (!OneCard)
            return;

        if (matchesSection != null)
            matchesSection.SetActive(!showingNewMatch);

        if (newMatchSection != null)
            newMatchSection.SetActive(showingNewMatch);

        PaintTab(matchesTabButton, !showingNewMatch);
        PaintTab(newMatchTabButton, showingNewMatch);
    }

    private void PaintTab(Button tab, bool selected)
    {
        if (tab == null)
            return;

        if (tab.image != null)
        {
            if (tabSelectedSprite != null && tabIdleSprite != null)
                tab.image.sprite = selected ? tabSelectedSprite : tabIdleSprite;

            tab.image.color = selected ? tabSelectedColour : tabIdleColour;
        }

        TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.color = selected ? tabSelectedTextColour : tabIdleTextColour;
    }

    // Signing in and out belongs to the pregame card; this panel is only
    // reachable once that is done.
    private void HideIdentityControls()
    {
        if (!OneCard || identityControls == null)
            return;

        foreach (GameObject control in identityControls)
        {
            if (control != null)
                control.SetActive(false);
        }
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
    private void OnInviteButtonPressed()
    {
        if (inviteButton != null)
            inviteButton.interactable = false;

        ShowStatus("Sending invitation...");

        if (inviteInput == null)
        {
            Debug.LogError("[INVITE] inviteInput is not assigned in Inspector.");
            ShowStatus("Invite input is not assigned.");
            if (inviteButton != null)
                inviteButton.interactable = true;
            return;
        }

        string invitedEmail = inviteInput.text.Trim().ToLowerInvariant();

        Debug.Log("[INVITE] Inviter UID = " + auth.CurrentUser.UserId);
        Debug.Log("[INVITE] Target email = " + invitedEmail);
        // targetUid and roomCode are not known yet; log them later in EnsureRoomThenSendInvite / SendInviteToUser

        if (string.IsNullOrWhiteSpace(invitedEmail))
        {
            ShowStatus("Enter the player's email address.");
            if (inviteButton != null)
                inviteButton.interactable = true;
            return;
        }

        if (!IsValidEmail(invitedEmail))
        {
            ShowStatus("Enter a valid email address.");
            if (inviteButton != null)
                inviteButton.interactable = true;
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            ShowStatus("You must be signed in to send an invitation.");
            if (inviteButton != null)
                inviteButton.interactable = true;
            return;
        }

        if (dbRoot == null)
        {
            ShowStatus("Firebase is not ready yet.");
            if (inviteButton != null)
                inviteButton.interactable = true;
            return;
        }

        ShowStatus("Finding player...");

        dbRoot.Child("users")
              .OrderByChild("email")
              .EqualTo(invitedEmail)
              .GetValueAsync()
              .ContinueWithOnMainThread(task =>
              {
                  if (task.IsCanceled || task.IsFaulted)
                  {
                      Debug.LogError("[MATCH STATUS] User lookup failed: " + task.Exception);
                      ShowStatus("Could not find that player.");
                      if (inviteButton != null)
                          inviteButton.interactable = true;
                      return;
                  }

                  DataSnapshot snapshot = task.Result;

                  if (snapshot == null || !snapshot.Exists || !snapshot.Children.GetEnumerator().MoveNext())
                  {
                      ShowStatus("No registered player found with that email.");
                      if (inviteButton != null)
                          inviteButton.interactable = true;
                      return;
                  }

                  string targetUid = null;
                  string targetName = null;

                  foreach (DataSnapshot userSnapshot in snapshot.Children)
                  {
                      targetUid = userSnapshot.Key;

                      // Already fetched with the uid - kept so the sender's
                      // own list can say who the invitation is waiting on.
                      targetName = userSnapshot.Child("displayName").Value as string;
                      break;
                  }

                  if (string.IsNullOrWhiteSpace(targetName))
                      targetName = invitedEmail;

                  if (string.IsNullOrEmpty(targetUid))
                  {
                      ShowStatus("No registered player found with that email.");
                      if (inviteButton != null)
                          inviteButton.interactable = true;
                      return;
                  }

                  if (targetUid == auth.CurrentUser.UserId)
                  {
                      ShowStatus("You cannot invite yourself.");
                      if (inviteButton != null)
                          inviteButton.interactable = true;
                      return;
                  }

                  Debug.Log("[INVITE] Target UID = " + targetUid);
                  EnsureRoomThenSendInvite(targetUid, targetName);
              });
    }

    private void EnsureRoomThenSendInvite(string targetUid, string targetName)
    {
        if (auth == null || auth.CurrentUser == null || dbRoot == null)
        {
            ShowStatus("Firebase is not ready.");

            if (inviteButton != null)
                inviteButton.interactable = true;

            return;
        }

        string roomCode = GenerateRoomCode();
        string myUid = auth.CurrentUser.UserId;

        string displayName = string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName)
            ? auth.CurrentUser.Email
            : auth.CurrentUser.DisplayName;

        RoomData room = new RoomData
        {
            code = roomCode,
            hostUid = myUid,
            hostDisplayName = displayName,
            guestUid = "",
            guestDisplayName = "",
            invitedUid = targetUid,
            invitedDisplayName = targetName,
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),

            playerCount = GetPlayerCount(),
            totalRounds = GetRoundCount(),
            turnTimeMinutes = GetTurnTimeMinutes()
        };

        PreGamePanel.RoomInviteData invite =
            new PreGamePanel.RoomInviteData
            {
                roomCode = roomCode,
                fromUid = myUid,
                fromDisplayName = displayName,
                createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

        Debug.Log("[INVITE] Generated new invitation room code: " + roomCode);
        Debug.Log("[INVITE] Creating room: rooms/" + roomCode);

        dbRoot.Child("rooms")
              .Child(roomCode)
              .SetRawJsonValueAsync(JsonUtility.ToJson(room))
              .ContinueWithOnMainThread(roomTask =>
              {
                  if (roomTask.IsCanceled || roomTask.IsFaulted)
                  {
                      Debug.LogError("[INVITE] Room write failed: " + roomTask.Exception);
                      ShowStatus("Could not create invitation room.");

                      if (inviteButton != null)
                          inviteButton.interactable = true;

                      return;
                  }

                  Debug.Log("[INVITE] Room created. Now writing invite to: users/" +
                            targetUid + "/invites/" + roomCode);

                  dbRoot.Child("users")
                        .Child(targetUid)
                        .Child("invites")
                        .Child(roomCode)
                        .SetRawJsonValueAsync(JsonUtility.ToJson(invite))
                        .ContinueWithOnMainThread(inviteTask =>
                        {
                            if (inviteTask.IsCanceled || inviteTask.IsFaulted)
                            {
                                Debug.LogError("[INVITE] Invite write failed: " +
                                               inviteTask.Exception);

                                ShowStatus("Room created, but invitation could not be sent.");

                                if (inviteButton != null)
                                    inviteButton.interactable = true;

                                return;
                            }

                            Debug.Log("[INVITE] Invite written successfully.");

                            ShowStatus("Invitation sent to " + targetName + ".");

                            if (inviteInput != null)
                                inviteInput.SetTextWithoutNotify("");

                            if (inviteButton != null)
                                inviteButton.interactable = true;

                            // The room was being created and the invitation
                            // written, but never added to the sender's own
                            // rooms - and the list is built from those. So the
                            // invitation reached the other player and left no
                            // trace here, even after a refresh, and the only
                            // way to find out whether it had worked was to
                            // press Invite again.
                            AddRoomToCurrentUser(roomCode, RefreshMatchState);
                        });
              });
    }

    private void SendInviteToUser(string targetUid, string roomCode)
    {
        string displayName = string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName)
            ? auth.CurrentUser.Email
            : auth.CurrentUser.DisplayName;

        PreGamePanel.RoomInviteData invite = new PreGamePanel.RoomInviteData
        {
            roomCode = roomCode,
            fromUid = auth.CurrentUser.UserId,
            fromDisplayName = displayName,
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        Debug.Log("[INVITE] Writing invite to: users/" + targetUid + "/invites/" + roomCode);

        dbRoot.Child("users")
              .Child(targetUid)
              .Child("invites")
              .Child(roomCode)
              .SetRawJsonValueAsync(JsonUtility.ToJson(invite))
              .ContinueWithOnMainThread(task =>
              {
                  if (task.IsCanceled || task.IsFaulted)
                  {
                      Debug.LogError("[INVITE] Write failed: " + task.Exception);
                      ShowStatus("Could not send invitation.");
                      if (inviteButton != null)
                          inviteButton.interactable = true;
                      return;
                  }

                  Debug.Log("[INVITE] Room code = " + roomCode);
                  Debug.Log("[INVITE] Invite written successfully.");

                  ShowStatus("Invitation sent to " + inviteInput.text.Trim() + ".");
                  inviteInput.SetTextWithoutNotify("");
                  if (inviteButton != null)
                      inviteButton.interactable = true;
              });
    }

    private void AddRoomToCurrentUser(string roomCode, Action completed)
    {
        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid).Child("activeRoomIds").GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    ShowStatus("Room created, but your profile could not be updated.");
                    return;
                }

                List<string> roomIds = new List<string>();

                if (task.Result != null && task.Result.Exists)
                {
                    foreach (DataSnapshot child in task.Result.Children)
                        roomIds.Add(child.Value.ToString());
                }

                if (!roomIds.Contains(roomCode))
                    roomIds.Add(roomCode);

                dbRoot.Child("users").Child(uid).Child("activeRoomIds")
                    .SetValueAsync(roomIds)
                    .ContinueWithOnMainThread(writeTask =>
                    {
                        if (writeTask.IsCanceled || writeTask.IsFaulted)
                        {
                            ShowStatus("Room created, but your profile could not be updated.");
                            return;
                        }

                        completed?.Invoke();
                    });
            });
    }

    /*private string FindMyWaitingRoomCode()
    {
        if (auth == null || auth.CurrentUser == null)
            return null;

        string uid = auth.CurrentUser.UserId;

        // This simple version uses the room code currently displayed in the UI,
        // if it belongs to the signed-in user. Otherwise it creates a new room.
        string currentCode = roomCodeText != null ? roomCodeText.text.Trim().ToUpperInvariant() : "";

        if (!string.IsNullOrEmpty(currentCode) && currentCode != "ROOM CODE")
            return currentCode;

        return null;
    }*/

    private bool IsValidEmail(string email)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            return address.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[6];

        for (int i = 0; i < code.Length; i++)
            code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];

        return new string(code);
    }
}