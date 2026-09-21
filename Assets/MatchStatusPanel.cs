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

        ScrabbyLog.Trace("[WIRING CHECK] loginButton=" + (loginButton != null ? loginButton.name : "NULL") +
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

        // This used to sign in as one of two hardcoded test accounts, and the
        // password for both sat in plain text in every APK - readable by
        // anyone who unzipped one. The code is gone; the button is still in
        // the scene, which can only be edited with Unity closed, so it is
        // hidden here until someone deletes it there.
        if (switchUserButton != null)
            switchUserButton.gameObject.SetActive(false);

        if (inviteButton != null)
            inviteButton.onClick.AddListener(OnInviteButtonPressed);

        WireQuickGame();
        WireRecentOpponents();
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
        ScrabbyLog.Trace("[MATCH STATUS] Logout button pressed");

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

    // A match holds two players. The dropdown that once offered three or four
    // never did anything - nothing read the number - and it is now the list of
    // people you have played, so reading a value from it would say a game had
    // four players because you picked the fourth name.
    public int GetPlayerCount()
    {
        return 2;
    }

    // Four rounds, like a solo game and like a quick game. The choice of two
    // or six was useful while testing and confusing to a player, so the
    // dropdown is gone and the number is the same everywhere.
    public int GetRoundCount()
    {
        return 4;
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
        ScrabbyLog.Trace(
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

        ScrabbyLog.Trace("[MATCH STATUS] OnJoinRoomPressed — raw input='" + roomCodeText.text +
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
        ScrabbyLog.Trace("[MATCH STATUS] Resume Match");

        ShowStatus("Loading match...");
    }


    private void RefreshMatchState()
    {
        ScrabbyLog.Trace("[MATCH STATUS] RefreshMatchState (legacy) called");

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

        ScrabbyLog.Trace("[MATCH STATUS] uid=" + uid);
        ScrabbyLog.Trace("[MATCH STATUS] path=users/" + uid);

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

    // Only ever one of these at a time. The list is reloaded from several
    // places - the refresh button, the profile watcher, the end of a submitted
    // round - and they arrive together, so two loaders were reading the same
    // matches and building and destroying the same rows at once. The log showed
    // every match twice; the rows are Unity objects, and tearing them down from
    // under another coroutine is the sort of thing that ends in a native crash.
    private Coroutine listLoader;

    // Stopping the previous loader is not enough on its own: one that is past
    // its last wait cannot be stopped and runs to the end, so every refresh
    // built the list twice, destroying and rebuilding the rows under whoever
    // was about to tap one. Each load takes a number, and only the newest is
    // allowed to build.
    private int listLoadGeneration;

    private void StartLoadingMatchList(
        string myUid, List<string> roomIds, List<string> matchIds)
    {
        if (listLoader != null)
            StopCoroutine(listLoader);

        listLoadGeneration++;
        listLoader = StartCoroutine(LoadMatchList(myUid, roomIds, matchIds));
    }

    private IEnumerator LoadMatchList(
    string myUid,
    List<string> roomIds,
    List<string> matchIds)
    {
        int myGeneration = listLoadGeneration;

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
            ScrabbyLog.Trace(
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

                    ScrabbyLog.Trace(
                        "[MATCH LIST] Match became available"
                        + " | matchId=" + matchId
                        + " | attempt=" + (attempt + 1)
                    );

                    break;
                }

                ScrabbyLog.Trace(
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

            ScrabbyLog.Trace(
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

            ScrabbyLog.Trace(
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
                ? BotOpponent.Label(match.player2Uid, match.player2DisplayName)
                : BotOpponent.Label(match.player1Uid, match.player1DisplayName);

            // A quick game waiting for someone to take its second seat. Said
            // plainly, so it reads differently from "Waiting..." on the button,
            // which means an opponent is there but has not played yet.
            if (amPlayer1 && string.IsNullOrEmpty(match.player2Uid))
                opponentName = "Waiting for opponent";

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

               /* ScrabbyLog.Trace(
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
            ScrabbyLog.Trace("[MATCH STATUS] Invites node exists, children count = " +
              invitesTask.Result.ChildrenCount);

            foreach (var child in invitesTask.Result.Children)
            {
                ScrabbyLog.Trace("[MATCH STATUS] Invite child key = " + child.Key);
                ScrabbyLog.Trace("[MATCH STATUS] Invite raw JSON = " + child.GetRawJsonValue());

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
            ScrabbyLog.Trace("[MATCH STATUS] No invites found or error: " +
                      (invitesTask.IsFaulted ? invitesTask.Exception?.ToString() : "none"));
        }
        */
        if (myGeneration != listLoadGeneration)
        {
            ScrabbyLog.Trace("[MATCH LIST] A newer refresh has taken over; " +
                             "this one stops here.");
            yield break;
        }

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

        ScrabbyLog.Trace("[MATCH STATUS] RefreshMatchStateForUser called with uid=" + uid);

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

                  ScrabbyLog.Trace("[MATCH STATUS] user.activeRoomIds=" +
          (user.activeRoomIds == null ? "NULL" : string.Join(",", user.activeRoomIds)));

                  ScrabbyLog.Trace("[MATCH STATUS] user.activeMatchIds=" +
                            (user.activeMatchIds == null ? "NULL" : string.Join(",", user.activeMatchIds)));

                  StartLoadingMatchList(uid, user.activeRoomIds, user.activeMatchIds);
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
        ScrabbyLog.Trace("[MATCH STATUS] Refresh requested");
        ShowStatus("Checking for active matches...");

        // Reading the matches takes a moment, and a button that does not
        // change invites another press.
        if (refreshButton != null)
        {
            refreshButton.interactable = false;
            StartCoroutine(FreeRefreshButtonShortly());
        }

        RefreshMatchState();
    }

    private IEnumerator FreeRefreshButtonShortly()
    {
        yield return new WaitForSeconds(1.2f);

        if (refreshButton != null)
            refreshButton.interactable = true;
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
            (roomCode, matchId, isCompleted) =>
            {
                // The tap is answered at once, whatever the database takes.
                row.ShowOpening();
                OnRowSelected(roomCode, matchId, isCompleted);
            });

        rows.Add(row);
    }

    private void OnRowSelected(string roomCode, string matchId, bool isCompleted)
    {
        ScrabbyLog.Trace(
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
            ScrabbyLog.Trace(
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
        ScrabbyLog.Trace(
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
            ScrabbyLog.Trace(
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

        // The list of people you have played is read here rather than at
        // startup: at startup nobody is signed in yet, so it stayed as the
        // scene left it - which is why it still read "2 Players".
        LoadRecentOpponents();
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

            StartLoadingMatchList(uid, user.activeRoomIds, user.activeMatchIds);
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

        ScrabbyLog.Trace("[INVITE] Inviter UID = " + auth.CurrentUser.UserId);
        ScrabbyLog.Trace("[INVITE] Target email = " + invitedEmail);

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

        // The server finds the player and delivers the invitation; the game
        // is not allowed to read other people's profiles any more, and does
        // not need to. See MatchStatusPanel.Invites.cs.
        CreateRoomThenAskForInvite(invitedEmail);
    }

    // The room is ours to make - we host it. Only the delivery needs the
    // server.
    private void CreateRoomThenAskForInvite(string invitedEmail)
    {
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
            // Filled in by the server once it knows who the address belongs
            // to; the address stands in until then, so the sender's own list
            // has something to show.
            invitedUid = "",
            invitedDisplayName = invitedEmail,
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),

            playerCount = GetPlayerCount(),
            totalRounds = GetRoundCount(),
            turnTimeMinutes = GetTurnTimeMinutes()
        };

        dbRoot.Child("rooms").Child(roomCode)
              .SetRawJsonValueAsync(JsonUtility.ToJson(room))
              .ContinueWithOnMainThread(roomTask =>
        {
            if (roomTask.IsCanceled || roomTask.IsFaulted)
            {
                Debug.LogError("[INVITE] Room write failed: " + roomTask.Exception);
                ShowStatus("Could not create the invitation.");

                if (inviteButton != null)
                    inviteButton.interactable = true;

                return;
            }

            SendInviteRequest(roomCode, invitedEmail, null, (ok, message, invitedName) =>
            {
                ShowStatus(message);

                if (inviteButton != null)
                    inviteButton.interactable = true;

                if (!ok)
                {
                    // Nothing came of it, so the room should not sit in
                    // anybody's list.
                    dbRoot.Child("rooms").Child(roomCode).RemoveValueAsync();
                    return;
                }

                if (inviteInput != null)
                    inviteInput.SetTextWithoutNotify("");

                // The sender's own list is built from their rooms; without
                // this the invitation left no trace at this end.
                AddRoomToCurrentUser(roomCode, RefreshMatchState);
            });
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