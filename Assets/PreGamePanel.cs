using System;
using TMPro;
using UnityEngine;
using Firebase.Auth;
using Firebase.Database;
using System.Collections.Generic;
using Firebase.Extensions;
using System.Threading.Tasks;
using UnityEngine.UI;
using System.Collections;

public class PreGamePanel : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField displayNameInput;
    [SerializeField] private TMP_InputField roomCodeInput;

    [Header("UI")]
    [SerializeField] private GameObject authSection;
    [SerializeField] private GameObject lobbySection;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI signedInAsText;
    [SerializeField] private GameObject pregamePanelRoot;


    [SerializeField] private GameObject pregamePanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject optionPanel;

    [SerializeField] private MatchStatusPanel matchStatusPanel;

    private DatabaseReference dbRoot;

    private FirebaseAuth auth;
    private FirebaseUser user;
    public Button startGameButton;

    private DatabaseReference currentMatchRef;
    private DatabaseReference currentRoomRef;
    private string watchedRoomCode = "";
    private string watchedMatchId = "";
    private bool firebaseInitialized = false;

    [SerializeField] private GameLogic gameLogic;
    private bool hasInitializedMatch = false;
    private MatchData currentMatch;

    private int matchTraceSeq = 0;

    private DatabaseReference currentSubmissionsRef;
    private int watchedRoundNumber = -1;
    private int lastProcessedRound = 0;

    private const string TestUserA_Email = "sexy@bikini.com";
    private const string TestUserB_Email = "zia@far.com";
    private const string TestUserPassword = "Scrabby1234";
    private DatabaseReference watchedRoomRef;

    private EventHandler<ValueChangedEventArgs> roomWatcher;

    private bool pendingEnterGameplay = false;

    private string pendingResolutionMatchId = null;

    [Serializable]
    public class RoomPlayerData
    {
        public string uid;
        public string displayName;
    }

    [Serializable]
    public class StringListWrapper
    {
        public List<string> items = new List<string>();
    }

    [System.Serializable]
    public class UserData
    {
        public string email;
        public string displayName;

        public string avatarId;

        public long createdAt;
        public long lastSeenAt;

        public string presenceState;

        public List<string> activeRoomIds = new List<string>();
        public List<string> activeMatchIds = new List<string>();
    }

    [Serializable]
    public class RoomInviteData
    {
        public string roomCode;
        public string fromUid;
        public string fromDisplayName;
        public long createdAtUnix;
    }

    private void Awake()
    {
        if (pregamePanel != null) pregamePanel.SetActive(true);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;
    }


    private void Start()
    {
        Debug.Log("[PreGamePanel] Start() running on GameObject: " + gameObject.name + " (EntityId: " + gameObject.GetEntityId() + ")");
        Debug.Log("[PreGamePanel] Start running");
        //Debug.Log("[PreGamePanel] START instance = " + GetInstanceID());

        if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;

        StartCoroutine(WaitForFirebaseThenInit());
    }

    
    private void OnEnable()
    {
        if (!firebaseInitialized)
        {
            Debug.Log("[PreGamePanel] ENABLED");
            StartCoroutine(WaitForFirebaseThenInit());
        }
    }

    private IEnumerator WaitForFirebaseThenInit()
    {
        Debug.Log("[PreGamePanel] WaitForFirebaseThenInit started");

        yield return new WaitUntil(() => FirebaseInit.IsReady);

        Debug.Log("[PreGamePanel] Firebase became ready");

        if (firebaseInitialized)
            yield break; // an earlier run already finished this

        auth = FirebaseInit.Auth;

        Debug.Log("FirebaseInit.Database = " + FirebaseInit.Database);
        Debug.Log("FirebaseInit.Auth = " + FirebaseInit.Auth);

        dbRoot = FirebaseInit.Database.RootReference;

        Debug.Log("[PreGamePanel] dbRoot assigned = " + dbRoot);

        firebaseInitialized = true;

        

        Debug.Log("[PreGamePanel] Firebase init complete. dbRoot assigned: " + (dbRoot != null));

        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    public void OnRefreshPressed()
    {
        Debug.Log("[MATCH STATUS] Refresh requested");
        SetStatus("Refresh not implemented yet");
    }
    private void TraceMatch(string label)
    {
        matchTraceSeq++;

        string currentMatchId = currentMatch == null ? "NULL" : currentMatch.matchId;
        string currentStatus = currentMatch == null ? "NULL" : currentMatch.status;
        string currentTurn = currentMatch == null ? "NULL" : currentMatch.currentRoundNumber.ToString();

        Debug.Log(
            $"[MATCHTRACE #{matchTraceSeq}] {label} | " +
            $"watchedMatchId={watchedMatchId} | " +
            $"currentMatchId={currentMatchId} | " +
            $"currentStatus={currentStatus} | " +
            $"currentTurn={currentTurn} | " +
            $"hasInitializedMatch={hasInitializedMatch} | " +
            $"frame={Time.frameCount}"
        );
    }
    private FirebaseUser GetCurrentUser()
    {
        return FirebaseAuth.DefaultInstance?.CurrentUser;
    }

    private void OnDisable()
    {
        Debug.Log("[PreGamePanel] DISABLED");
        StopWatchingRoom();
    }

    // on PreGamePanel
    public void EnterMultiplayerFlow()
    {
        if (optionPanel != null) optionPanel.SetActive(false);

        if (IsSignedIn())
        {
            // Already authenticated — skip the login screen entirely.
            gameObject.SetActive(false);
            if (matchStatusPanel != null) matchStatusPanel.gameObject.SetActive(true);
        }
        else
        {
            // Show the login/register screen; OnLoginPressed's success path
            // already hands off to MatchStatusPanel once auth completes.
            if (pregamePanel != null) pregamePanel.SetActive(true);
        }
    }
    private void AuthStateChanged(object sender, EventArgs eventArgs)
    {
        if (auth.CurrentUser != user)
        {
            bool signedIn = user != auth.CurrentUser && auth.CurrentUser != null;

            if (!signedIn && user != null)
            {
                SetStatus("Signed out.");
            }

            user = auth.CurrentUser;

            if (signedIn)
            {
                string shownName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
                SetStatus("Signed in.");
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                // Auto-login (persisted session) reached this point too — hand off
                // to MatchStatusPanel the same way OnLoginPressed's success path does,
                // but only if PreGamePanel is the one currently visible (avoid stealing
                // focus if the user is mid-gameplay or elsewhere when a session refreshes).
                if (matchStatusPanel != null && pregamePanel != null && pregamePanel.activeInHierarchy)
                {
                    matchStatusPanel.gameObject.SetActive(true);
                    gameObject.SetActive(false);
                }
            }
            else
            {
                if (signedInAsText != null)
                    signedInAsText.text = "Not signed in";
            }

            RefreshUI();
        }
    }

    private void OnMatchValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args == null)
        {
            TraceMatch("OnMatchValueChanged ARGS NULL");
            return;
        }

        string raw = null;
        int rawLen = -1;

        if (args.Snapshot != null)
        {
            raw = args.Snapshot.GetRawJsonValue();
            rawLen = string.IsNullOrEmpty(raw) ? 0 : raw.Length;
        }

        Debug.Log(
            $"[MATCHTRACE CALLBACK] OnMatchValueChanged ENTER | " +
            $"dbError={(args.DatabaseError != null ? args.DatabaseError.Message : "null")} | " +
            $"snapshotExists={(args.Snapshot != null && args.Snapshot.Exists)} | " +
            $"rawLen={rawLen} | " +
            $"watchedMatchId={watchedMatchId} | " +
            $"frame={Time.frameCount}"
        );

        if (args.DatabaseError != null)
        {
            Debug.LogError("[PregamePanel] Match listener error: " + args.DatabaseError.Message);
            return;
        }

        if (args.Snapshot == null || !args.Snapshot.Exists)
        {
            TraceMatch("OnMatchValueChanged SNAPSHOT MISSING");
            return;
        }

        if (string.IsNullOrEmpty(raw))
        {
            TraceMatch("OnMatchValueChanged RAW JSON EMPTY");
            return;
        }

        MatchData match = JsonUtility.FromJson<MatchData>(raw);

        Debug.Log("[MATCHTRACE CALLBACK] parsed match id=" + (match == null ? "NULL" : match.matchId));

        if (match == null)
        {
            TraceMatch("OnMatchValueChanged PARSE FAILED");
            return;
        }

        currentMatch = match;

        if (currentMatch.matchId == pendingResolutionMatchId && currentMatch.status == "completed")
        {
            pendingResolutionMatchId = null;
            ShowGameOverForMatch(currentMatch); // overload that takes the MatchData directly — no need to re-fetch
            return; // skip the normal round-progression/waiting flow entirely
        }

        if (currentMatch.currentRoundNumber > lastProcessedRound + 1)
        {
            HandleResolvedRound();
            lastProcessedRound = currentMatch.currentRoundNumber - 1;
        }

        Debug.Log(
                "[MATCH LOADED] " +
                currentMatch.matchId +
                " Round=" +
                currentMatch.currentRoundNumber +
                " RackJson=" +
                (string.IsNullOrEmpty(currentMatch.sharedrackjson) ? "EMPTY" : "PRESENT")
            );

        if (currentMatch != null && watchedRoundNumber != currentMatch.currentRoundNumber)
        {
            WatchSubmissionsForRound(currentMatch.currentRoundNumber);
        }

        TraceMatch("OnMatchValueChanged AFTER currentMatch ASSIGN");

        if (pendingEnterGameplay)
        {
            pendingEnterGameplay = false;
            TraceMatch("OnMatchValueChanged TRIGGER EnterGameplayMode");
            CheckSubmissionThenEnterGameplay();
        }
    }

    private void ShowOnlineRoundResult(RoundResultData result)
    {
        if (Singleton.Instance == null ||
            Singleton.Instance.UIManager == null)
            return;

        string uid = auth.CurrentUser.UserId;
        bool isPlayer1 = currentMatch.player1Uid == uid;

        int localScore =
            isPlayer1
            ? currentMatch.player1Score
            : currentMatch.player2Score;

        int opponentScore =
            isPlayer1
            ? currentMatch.player2Score
            : currentMatch.player1Score;

        Singleton.Instance.UIManager.UpdateTotalScores(
            localScore,
            opponentScore);

        if (result.anyValidMove)
        {
            Singleton.Instance.UIManager.ShowRoundMessage(
                result.winnerDisplayName +
                " wins with " +
                result.winnerWord +
                " (" +
                result.winnerScore +
                " pts)");
        }
        else
        {
            Singleton.Instance.UIManager.ShowRoundMessage(
                "No valid move this round.");
        }
    }

    private void HandleResolvedRound()
    {
        if (string.IsNullOrEmpty(currentMatch.lastRoundResultJson))
            return;

        RoundResultData result = JsonUtility.FromJson<RoundResultData>(currentMatch.lastRoundResultJson);
        if (result == null)
            return;

        bool isViewingThisMatch =
            gameplayPanel != null &&
            gameplayPanel.activeInHierarchy &&
            watchedMatchId == currentMatch.matchId;

        if (isViewingThisMatch && gameLogic != null)
        {
            gameLogic.StartCoroutine(ShowOnlineRoundResultDelayed(result));
        }
        // else: player wasn't watching live — just let the round-2 rack/board load normally, no popup
    }

    private IEnumerator ShowOnlineRoundResultDelayed(
    RoundResultData result)
    {
        string uid = auth.CurrentUser.UserId;

        DatabaseReference submissionRef =
            dbRoot.Child("matches")
                  .Child(currentMatch.matchId)
                  .Child("rounds")
                  .Child(result.roundNumber.ToString())
                  .Child("submissions")
                  .Child(uid);

        var task = submissionRef.GetValueAsync();

        yield return new WaitUntil(() => task.IsCompleted);

        if (!task.IsFaulted &&
            task.Result != null &&
            task.Result.Exists)
        {
            string json = task.Result.GetRawJsonValue();

            RoundSubmissionData localSubmission =
                JsonUtility.FromJson<RoundSubmissionData>(json);

            if (localSubmission != null &&
                localSubmission.isValid &&
                !string.IsNullOrEmpty(localSubmission.simulatedTilesJson))
            {
                SimTileListWrapper wrapper =
                    JsonUtility.FromJson<SimTileListWrapper>(
                        localSubmission.simulatedTilesJson);

                if (wrapper != null &&
                    wrapper.tiles != null &&
                    wrapper.tiles.Count > 0)
                {
                    SimPlacedTileData bestTile = wrapper.tiles[0];

                    foreach (SimPlacedTileData tile in wrapper.tiles)
                    {
                        if (tile.row > bestTile.row)
                        {
                            bestTile = tile;
                        }
                        else if (tile.row == bestTile.row &&
                                 tile.col > bestTile.col)
                        {
                            bestTile = tile;
                        }
                    }

                    LetterPosition anchor =
                        new LetterPosition(
                            bestTile.row,
                            bestTile.col);

                    if (Singleton.Instance != null &&
                        Singleton.Instance.UIManager != null)
                    {
                        Singleton.Instance.UIManager.ShowValidatedWordScore(
                            anchor,
                            localSubmission.score,
                            false);
                    }
                }
            }
        }

        // Give the player time to see THEIR score first
        yield return new WaitForSeconds(1.5f);

        ShowOnlineRoundResult(result);
    }

    private void WatchSubmissionsForRound(int roundNumber)
    {
        if (currentSubmissionsRef != null)
        {
            currentSubmissionsRef.ValueChanged -= OnSubmissionsValueChanged;
            currentSubmissionsRef = null;
        }

        watchedRoundNumber = roundNumber;

        currentSubmissionsRef = dbRoot.Child("matches").Child(currentMatch.matchId)
            .Child("rounds").Child(roundNumber.ToString()).Child("submissions");

        currentSubmissionsRef.ValueChanged += OnSubmissionsValueChanged;
    }

    private void OnSubmissionsValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || currentMatch == null)
            return;

        int submittedCount = (int)args.Snapshot.ChildrenCount;
        int expectedCount = 2; // host + guest — extend when room supports more players

        Debug.Log("[PregamePanel] Round " + watchedRoundNumber + " submissions: " + submittedCount + "/" + expectedCount);

        if (submittedCount >= expectedCount)
        {
            TryResolveRound(currentMatch.matchId, watchedRoundNumber);
        }
    }
    private void TryResolveRound(string matchId, int roundNumber)
    {
        DatabaseReference submissionsRef = dbRoot.Child("matches").Child(matchId)
            .Child("rounds").Child(roundNumber.ToString()).Child("submissions");

        submissionsRef.GetValueAsync().ContinueWithOnMainThread(readTask =>
        {
            if (readTask.IsFaulted || readTask.Result == null || !readTask.Result.Exists)
                return;

            List<RoundSubmissionData> submissions = new List<RoundSubmissionData>();

            foreach (var child in readTask.Result.Children)
            {
                string raw = child.GetRawJsonValue();
                if (string.IsNullOrEmpty(raw))
                    continue;

                RoundSubmissionData sub = JsonUtility.FromJson<RoundSubmissionData>(raw);
                if (sub != null)
                    submissions.Add(sub);
            }

            DatabaseReference matchRef = dbRoot.Child("matches").Child(matchId);

            matchRef.GetValueAsync().ContinueWithOnMainThread(matchReadTask =>
            {
                if (matchReadTask.IsFaulted || matchReadTask.Result == null || !matchReadTask.Result.Exists)
                {
                    Debug.LogError("[PregamePanel] Failed to read match for resolution: " + matchReadTask.Exception);
                    return;
                }

                string matchJson = matchReadTask.Result.GetRawJsonValue();
                MatchData liveMatch = JsonUtility.FromJson<MatchData>(matchJson);

                if (liveMatch == null || liveMatch.currentRoundNumber != roundNumber)
                    return; // already resolved by someone else, or stale read

                if (liveMatch.roundResolutionStatus == "resolving" || liveMatch.roundResolutionStatus == "done")
                    return; // someone already claimed/finished this round

                // --- soft lock: claim it before doing any work ---
                liveMatch.roundResolutionStatus = "resolving";
                liveMatch.roundResolutionByUid = auth.CurrentUser.UserId;

                matchRef.Child("roundResolutionStatus").SetValueAsync("resolving")
                    .ContinueWithOnMainThread(claimTask =>
                    {
                        if (claimTask.IsFaulted)
                        {
                            Debug.LogError("[PregamePanel] Failed to claim round resolution: " + claimTask.Exception);
                            return;
                        }

                        ResolveRoundNow(liveMatch, roundNumber, submissions, matchRef);
                    });
            });
        });
    }
    private void ResolveRoundNow(MatchData liveMatch, int roundNumber, List<RoundSubmissionData> submissions, DatabaseReference matchRef)
    {
        RoundSubmissionData winner = null;
        foreach (var sub in submissions)
        {
            if (!sub.isValid) continue;
            if (winner == null || IsBetterSubmission(sub, winner))
                winner = sub;
        }

        BoardStateData board = JsonUtility.FromJson<BoardStateData>(liveMatch.boardStateJson) ?? new BoardStateData();
        BagStateData bag = JsonUtility.FromJson<BagStateData>(liveMatch.bagStateJson) ?? new BagStateData();
        RackStateData sharedRack = JsonUtility.FromJson<RackStateData>(liveMatch.sharedrackjson) ?? new RackStateData();

        RoundResultData result = new RoundResultData
        {
            roundNumber = roundNumber,
            anyValidMove = winner != null
        };

        if (winner != null)
        {
            SimTileListWrapper wrapper = JsonUtility.FromJson<SimTileListWrapper>(winner.simulatedTilesJson);

            if (wrapper != null && wrapper.tiles != null)
            {
                foreach (var tile in wrapper.tiles)
                {
                    int idx = sharedRack.tiles.FindIndex(t => t.letter == tile.letter);
                    if (idx >= 0)
                        sharedRack.tiles.RemoveAt(idx);

                    int x = tile.col - 1;
                    int y = tile.row - 1;
                    BoardCellData cell = FindCell(board, x, y);
                    if (cell != null)
                    {
                        cell.occupied = true;
                        cell.tile = new TileData
                        {
                            letter = tile.letter,
                            value = tile.points,
                            id = Guid.NewGuid().ToString("N")
                        };
                    }
                }
            }

            result.winnerUid = winner.uid;
            result.winnerWord = winner.word;
            result.winnerScore = winner.score;

            bool winnerIsPlayer1 = winner.uid == liveMatch.player1Uid;
            result.winnerDisplayName = winnerIsPlayer1 ? liveMatch.player1DisplayName : liveMatch.player2DisplayName;

            if (winnerIsPlayer1)
                liveMatch.player1Score += winner.score;
            else
                liveMatch.player2Score += winner.score;
        }

        while (sharedRack.tiles.Count < 7 && bag.tiles != null && bag.tiles.Count > 0)
        {
            sharedRack.tiles.Add(bag.tiles[0]);
            bag.tiles.RemoveAt(0);
        }

        int totalRounds = liveMatch.totalRounds > 0 ? liveMatch.totalRounds : 5;
        int nextRound = roundNumber + 1;
        bool isFinalRoundJustPlayed = nextRound > totalRounds;

        liveMatch.boardStateJson = JsonUtility.ToJson(board);
        liveMatch.bagStateJson = JsonUtility.ToJson(bag);
        liveMatch.sharedrackjson = JsonUtility.ToJson(sharedRack);
        liveMatch.lastRoundResultJson = JsonUtility.ToJson(result);
        liveMatch.currentRoundNumber = nextRound;
        liveMatch.roundResolutionStatus = "done";
        liveMatch.status = isFinalRoundJustPlayed ? "completed" : "active";

        string updatedJson = JsonUtility.ToJson(liveMatch);

        matchRef.SetRawJsonValueAsync(updatedJson).ContinueWithOnMainThread(writeTask =>
        {
            if (writeTask.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to write resolved round: " + writeTask.Exception);
                return;
            }

            Debug.Log("[PregamePanel] Round " + roundNumber + " resolved and written.");
        });
    }
    // PUBLIC — used by MatchStatusPanel, which only has a matchId string
    public void ShowGameOverForMatch(string matchId)
    {
        dbRoot.Child("matches").Child(matchId).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
            {
                SetStatus("Could not load game.");
                return;
            }

            MatchData match = JsonUtility.FromJson<MatchData>(task.Result.GetRawJsonValue());
            if (match == null) return;

            ShowGameOverForMatch(match);
        });
    }

    private void CheckSubmissionThenEnterGameplay()
    {
        Debug.Log("[PregamePanel] CheckSubmissionThenEnterGameplay ENTER | auth.CurrentUser=" +
        (auth != null && auth.CurrentUser != null ? auth.CurrentUser.Email + " (" + auth.CurrentUser.UserId + ")" : "NULL") +
        " | currentMatch=" + (currentMatch != null ? currentMatch.matchId : "NULL"));

        if (currentMatch == null || auth == null || auth.CurrentUser == null)
        {
            Debug.LogWarning("[PregamePanel] CheckSubmissionThenEnterGameplay ABORTED early — null check failed.");
            return;
        }
        

        string uid = auth.CurrentUser.UserId;
        int roundNumber = currentMatch.currentRoundNumber;

        Debug.Log("[PregamePanel] Checking submission at matches/" + currentMatch.matchId + "/rounds/" + roundNumber + "/submissions/" + uid);

        dbRoot.Child("matches").Child(currentMatch.matchId)
            .Child("rounds").Child(roundNumber.ToString())
            .Child("submissions").Child(uid)
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                Debug.Log("[PregamePanel] Submission check task completed. Faulted=" + task.IsFaulted);

                if (task.IsFaulted)
                {
                    Debug.LogError("[PregamePanel] Failed to check submission status: " + task.Exception);
                    return;
                }

                bool alreadySubmitted = task.Result != null && task.Result.Exists;

                if (alreadySubmitted)
                {
                    SetStatus("You've already played this round. Waiting for other players...");

                    if (optionPanel != null) optionPanel.SetActive(false);
                    if (pregamePanel != null) pregamePanel.SetActive(false);
                    if (gameplayPanel != null) gameplayPanel.SetActive(false);
                    if (gameOverPanel != null) gameOverPanel.SetActive(false);

                    if (matchStatusPanel != null)
                    {
                        matchStatusPanel.gameObject.SetActive(true);
                        matchStatusPanel.OnRefreshPressed();
                    }
                }
                else
                {
                    EnterGameplayMode();
                }
            });
    }

    private void ShowGameOverForMatch(MatchData match)
    {
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false); // UIManager's gameOverPanel lives outside this, so it's fine to hide gameplayPanel
        if (matchStatusPanel != null) matchStatusPanel.gameObject.SetActive(false);

        string myUid = auth != null && auth.CurrentUser != null ? auth.CurrentUser.UserId : "";
        bool amPlayer1 = match.player1Uid == myUid;

        int myScore = amPlayer1 ? match.player1Score : match.player2Score;
        int opponentScore = amPlayer1 ? match.player2Score : match.player1Score;
        string opponentName = amPlayer1 ? match.player2DisplayName : match.player1DisplayName;

        string finalMessage;
        if (myScore > opponentScore)
            finalMessage = "You win!";
        else if (myScore < opponentScore)
            finalMessage = "You lose.";
        else
            finalMessage = "It's a tie!";

        string roundSummary =
            "Final score\nYou: " + myScore + "  -  " + opponentName + ": " + opponentScore +
            "\nRounds played: " + match.totalRounds;

        UIManager uiManager = Singleton.Instance != null ? Singleton.Instance.UIManager : null;

        if (uiManager == null)
        {
            // Player may not have entered gameplay this session yet (e.g. tapped a
            // completed row straight from MatchStatusPanel on a fresh app open),
            // so Singleton/UIManager might not have run Awake(). Fall back to a find.
            uiManager = FindAnyObjectByType<UIManager>();
        }

        if (uiManager != null)
        {
            uiManager.ShowGameOverPanel(finalMessage, roundSummary);
        }
        else
        {
            Debug.LogWarning("[PregamePanel] Could not find UIManager to show game over panel.");
            SetStatus(finalMessage + " " + roundSummary.Replace("\n", " "));
        }
    }
    private bool IsBetterSubmission(RoundSubmissionData candidate, RoundSubmissionData currentBest)
    {
        if (candidate.score != currentBest.score)
            return candidate.score > currentBest.score;

        int candLen = string.IsNullOrEmpty(candidate.word) ? 0 : candidate.word.Length;
        int bestLen = string.IsNullOrEmpty(currentBest.word) ? 0 : currentBest.word.Length;

        if (candLen != bestLen)
            return candLen > bestLen;

        return candidate.submittedAtUnix < currentBest.submittedAtUnix; // earlier submission wins ties
    }

    public void WatchMatch(string matchId, bool enterWhenReady = false)
    {
        if (!EnsureFirebaseReady())
        {
            Debug.LogWarning("[PreGamePanel] WatchMatch aborted: Firebase not ready.");
            return;
        }

        TraceMatch("WatchMatch ENTER matchId=" + matchId);

        if (currentMatchRef != null)
        {
            currentMatchRef.ValueChanged -= OnMatchValueChanged;
            currentMatchRef = null;
        }

        watchedMatchId = matchId;
        pendingEnterGameplay = enterWhenReady;

        currentMatchRef = dbRoot.Child("matches").Child(matchId);
        currentMatchRef.ValueChanged += OnMatchValueChanged;

        TraceMatch("WatchMatch AFTER subscribe");
    }
    public void StopWatchingMatch()
    {
        TraceMatch("StopWatchingMatch ENTER");

        if (currentMatchRef != null)
        {
            currentMatchRef.ValueChanged -= OnMatchValueChanged;
            Debug.Log("[PregamePanel] Stopped watching match: " + watchedMatchId);
            currentMatchRef = null;
        }

        TraceMatch("StopWatchingMatch BEFORE CLEAR");

        watchedMatchId = "";
        currentMatch = null;
        hasInitializedMatch = false;

        TraceMatch("StopWatchingMatch AFTER CLEAR");
    }

    public void OnRegisterPressed()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        string displayName = displayNameInput.text.Trim();

        Debug.Log("[PregamePanel] Register button pressed.");

        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an email.");
            Debug.LogWarning("[PregamePanel] Email is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter a password.");
            Debug.LogWarning("[PregamePanel] Password is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            SetStatus("Enter a display name.");
            Debug.LogWarning("[PregamePanel] Display name is empty.");
            return;
        }

        SetStatus("Registering...");
        Debug.Log("[PregamePanel] Trying to register email: " + email);

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                Debug.LogError("[PregamePanel] Register canceled.");
                RunOnMainThread(() => SetStatus("Register canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                string errorMessage = GetFirebaseErrorMessage(task.Exception);
                Debug.LogError("[PregamePanel] Register failed: " + errorMessage);
                RunOnMainThread(() => SetStatus("Register failed: " + errorMessage));
                return;
            }

            Firebase.Auth.AuthResult result = task.Result;
            FirebaseUser createdUser = result.User;
            string uid = createdUser.UserId;

            Debug.Log("[PregamePanel] User created successfully. UID: " + uid);

            Firebase.Auth.UserProfile profile = new Firebase.Auth.UserProfile
            {
                DisplayName = displayName
            };

            createdUser.UpdateUserProfileAsync(profile).ContinueWith(profileTask =>
            {
                if (profileTask.IsCanceled)
                {
                    Debug.LogWarning("[PregamePanel] Profile update canceled.");
                    RunOnMainThread(() => SetStatus("User created, name update canceled."));
                    return;
                }

                if (profileTask.IsFaulted)
                {
                    string profileError = GetFirebaseErrorMessage(profileTask.Exception);
                    Debug.LogError("[PregamePanel] Profile update failed: " + profileError);
                    RunOnMainThread(() => SetStatus("User created, name update failed: " + profileError));
                    return;
                }

                Debug.Log("[PregamePanel] Profile updated successfully.");

                UserData userData = new UserData
                {
                    email = createdUser.Email,
                    displayName = displayName,
                    createdAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    lastSeenAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    //currentRoomId = "",
                    activeRoomIds = new List<string>(),
                    activeMatchIds = new List<string>(),
                    //currentMatchId = "",
                    presenceState = "online"
                };

                string json = JsonUtility.ToJson(userData);

                dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(json).ContinueWith(dbTask =>
                {
                    if (dbTask.IsCanceled)
                    {
                        Debug.LogError("[PregamePanel] Database write canceled.");
                        RunOnMainThread(() => SetStatus("User created, but DB write canceled."));
                        return;
                    }

                    if (dbTask.IsFaulted)
                    {
                        string dbError = GetFirebaseErrorMessage(dbTask.Exception);
                        Debug.LogError("[PregamePanel] Database write failed: " + dbError);
                        RunOnMainThread(() => SetStatus("User created, but DB write failed: " + dbError));
                        return;
                    }

                    Debug.Log("[PregamePanel] User profile saved to database.");

                    RunOnMainThread(() =>
                    {
                        SetStatus("Registered successfully.");
                        RefreshUI();
                    });
                });
            });
        });
    }

    private string GetFirebaseErrorMessage(Exception exception)
    {
        if (exception == null)
            return "Unknown error";

        AggregateException aggregate = exception as AggregateException;
        if (aggregate != null)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                Firebase.FirebaseException firebaseEx = inner as Firebase.FirebaseException;
                if (firebaseEx != null)
                {
                    return firebaseEx.Message + " (Code: " + firebaseEx.ErrorCode + ")";
                }

                if (!string.IsNullOrWhiteSpace(inner.Message))
                {
                    return inner.Message;
                }
            }
        }

        return exception.Message;
    }

    private void SetStatus(string message)
    {
        Debug.Log("[PregamePanel STATUS] " + message);

        if (statusText != null)
            statusText.text = message;
        else
            Debug.LogWarning("[PregamePanel] statusText is not assigned in Inspector.");
    }

    public void OnLoginPressed()
    {
        var auth = FirebaseAuth.DefaultInstance;
        var user = auth?.CurrentUser;

        if (user != null)
        {
            SetStatus("Already logged in.");
            return;
        }

        if (auth == null)
        {
            SetStatus("Firebase Auth not ready.");
            return;
        }

        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an email.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter a password.");
            return;
        }

        SetStatus("Logging in...");

        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Login canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                RunOnMainThread(() => SetStatus("Login failed: " + task.Exception?.GetBaseException().Message));
                return;
            }

            FirebaseUser signedInUser = task.Result.User;

            RunOnMainThread(() =>
            {
                string shownName = string.IsNullOrWhiteSpace(signedInUser.DisplayName)
                    ? signedInUser.Email
                    : signedInUser.DisplayName;

                SetStatus("Login successful.");
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                RefreshUI();

                // Return to MatchStatusPanel now that login succeeded.
                if (matchStatusPanel != null)
                {
                    matchStatusPanel.gameObject.SetActive(true);
                    gameObject.SetActive(false);
                }
            });
        });
    }

    public void OnCreateRoomPressed()
    {
        Debug.Log("[PreGamePanel] OnCreateRoomPressed() running on GameObject: " + gameObject.name + " (EntityId: " + gameObject.GetEntityId() + ")");

        var auth = FirebaseAuth.DefaultInstance;
        var user = auth?.CurrentUser;

        if (user == null)
        {
            SetStatus("You must be logged in first.");
            return;
        }

        // Self-heal dbRoot if this panel's Firebase-init coroutine never completed
        // (e.g. it was interrupted by the panel being disabled before FirebaseInit.IsReady).
        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
                this.auth = FirebaseInit.Auth;
                firebaseInitialized = true;
                Debug.Log("[PreGamePanel] dbRoot was null — recovered from FirebaseInit.");
            }
            else
            {
                SetStatus("Firebase not ready yet. Try again in a moment.");
                Debug.LogWarning("[PreGamePanel] OnCreateRoomPressed aborted: dbRoot null and FirebaseInit not ready.");
                return;
            }
        }

        string roomCode = GenerateRoomCode();
        string hostName = GetBestDisplayName();

        RoomData room = new RoomData
        {
            code = roomCode,
            hostUid = user.UserId,
            hostDisplayName = hostName,
            guestUid = "",
            guestDisplayName = "",
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        string json = JsonUtility.ToJson(room);

        Debug.Log("user = " + user);
        Debug.Log("dbRoot = " + dbRoot);
        Debug.Log("roomCodeInput = " + roomCodeInput);

        SetStatus("Creating room...");

        dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(json).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Create room canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                SetStatus("Create room failed: " + task.Exception?.GetBaseException().Message);
                Debug.LogError("[PregamePanel] Create room failed: " + task.Exception);
                return;
            }

            Debug.Log("[PregamePanel] Room created successfully: " + roomCode);

            roomCodeInput.text = roomCode;
            roomCodeInput.SetTextWithoutNotify(roomCode);
            roomCodeInput.ForceLabelUpdate();

            //SetStatus("Room created: " + roomCode);
            //WatchRoom(roomCode);

            AddRoomToUser(roomCode);

            roomCodeInput.text = roomCode;
            roomCodeInput.SetTextWithoutNotify(roomCode);
            roomCodeInput.ForceLabelUpdate();

            SetStatus("Room created: " + roomCode);
            WatchRoom(roomCode);

            //user.activeRoomIds.Add(roomCode);
            //presenceState = "waiting";

        });
    }
    private void AddMatchToUser(string matchId)
    {
        if (auth == null || auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
                return;

            UserData userData = JsonUtility.FromJson<UserData>(task.Result.GetRawJsonValue());
            if (userData == null)
                return;

            if (userData.activeMatchIds == null)
                userData.activeMatchIds = new List<string>();

            if (!userData.activeMatchIds.Contains(matchId))
                userData.activeMatchIds.Add(matchId);

            dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(JsonUtility.ToJson(userData));
        });
    }
    private void AddRoomToUser(string roomCode)
    {
        if (auth == null || auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
                return;

            UserData userData = JsonUtility.FromJson<UserData>(task.Result.GetRawJsonValue());
            if (userData == null)
                return;

            if (userData.activeRoomIds == null)
                userData.activeRoomIds = new List<string>();

            if (!userData.activeRoomIds.Contains(roomCode))
                userData.activeRoomIds.Add(roomCode);

            dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(JsonUtility.ToJson(userData));
        });
    }

    public void OnJoinRoomPressed()
    {

        string roomCode = roomCodeInput.text.Trim().ToUpper();

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            SetStatus("Enter a room code.");
            return;
        }

        JoinRoomByCode(roomCode);
    }
    /*
    if (!EnsureFirebaseReady())
    {
        SetStatus("Firebase not ready yet. Try again in a moment.");
        return;
    }

    var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

    SetStatus("Joining room...");
    */

    public void JoinRoomByCode(string roomCode)
    {
        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
            return;
        }

        var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

        SetStatus("Joining room...");

        dbRoot.Child("rooms").Child(roomCode).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Join room canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                SetStatus("Join room failed: " + task.Exception?.GetBaseException().Message);
                Debug.LogError("[PregamePanel] Join room read failed: " + task.Exception);
                return;
            }

            DataSnapshot snapshot = task.Result;

            if (!snapshot.Exists)
            {
                SetStatus("Room not found.");
                return;
            }

            string json = snapshot.GetRawJsonValue();
            RoomData room = JsonUtility.FromJson<RoomData>(json);

            if (room == null)
            {
                SetStatus("Room data invalid.");
                return;
            }

            if (!string.IsNullOrEmpty(room.guestUid))
            {
                SetStatus("Room is already full.");
                return;
            }

            room.guestUid = auth.CurrentUser.UserId;
            room.guestDisplayName = GetBestDisplayName();
            room.status = "full";

            string updatedJson = JsonUtility.ToJson(room);

            dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(updatedJson).ContinueWith(updateTask =>
            {
                if (updateTask.IsCanceled)
                {
                    SetStatus("Join update canceled.");
                    return;
                }

                if (updateTask.IsFaulted)
                {
                    SetStatus("Join update failed: " + updateTask.Exception?.GetBaseException().Message);
                    Debug.LogError("[PregamePanel] Join room write failed: " + updateTask.Exception);
                    return;
                }

                Debug.Log("[PregamePanel] Joined room successfully: " + roomCode);
                SetStatus("Joined room successfully: " + roomCode);
                WatchRoom(roomCode);

                

            }, uiScheduler);

        }, uiScheduler);
    }

    public void OnLogoutPressed()
    {
        if (auth == null)
            return;

        StopWatchingRoom();
        StopWatchingMatch();

        auth.SignOut();
        SetStatus("Logged out.");
        RefreshUI();
    }

    private bool IsSignedIn()
    {
        return auth != null && auth.CurrentUser != null;
    }

    private string GetBestDisplayName()
    {
        if (auth == null || auth.CurrentUser == null)
            return "Unknown";

        if (!string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName))
            return auth.CurrentUser.DisplayName;

        if (!string.IsNullOrWhiteSpace(displayNameInput.text))
            return displayNameInput.text.Trim();

        return auth.CurrentUser.Email;
    }

    private void RefreshUI()
    {
        bool signedIn = IsSignedIn();

        if (authSection != null)
            authSection.SetActive(!signedIn);

        if (lobbySection != null)
            lobbySection.SetActive(signedIn);
    }



    private void RunOnMainThread(Action action)
    {
        UnityMainThreadDispatcher.Enqueue(action);
    }

    private string GenerateRoomCode(int length = 6)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[length];

        for (int i = 0; i < length; i++)
        {
            code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
        }

        return new string(code);
    }

    private void HandleRoomState(string roomCode, RoomData room)
    {
        bool roomIsFull =
            !string.IsNullOrEmpty(room.hostUid) &&
            !string.IsNullOrEmpty(room.guestUid);

        if (!roomIsFull)
            return;

        // Room is full but no match yet.
        if (string.IsNullOrEmpty(room.matchId))
        {
            TryCreateInitialMatchFromRoom(roomCode, room);
            return;
        }

        // Match already exists.
        AddMatchToUser(room.matchId);
        WatchMatch(room.matchId, true);
    }

    public void WatchRoom(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode))
            return;

        // Self-heal dbRoot if this panel's Firebase-init coroutine never completed.
        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
                auth = FirebaseInit.Auth;
                firebaseInitialized = true;
                Debug.Log("[PreGamePanel] dbRoot was null in WatchRoom — recovered from FirebaseInit.");
            }
            else
            {
                Debug.LogWarning("[PreGamePanel] WatchRoom aborted: dbRoot null and FirebaseInit not ready.");
                return;
            }
        }

        StopWatchingRoom();

        watchedRoomRef =
            dbRoot.Child("rooms").Child(roomCode);

        roomWatcher = (sender, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError(
                    "[ROOM WATCH] " +
                    args.DatabaseError.Message);

                return;
            }

            if (args.Snapshot == null ||
                !args.Snapshot.Exists)
            {
                return;
            }

            string json =
                args.Snapshot.GetRawJsonValue();

            RoomData room =
                JsonUtility.FromJson<RoomData>(json);

            if (room == null)
                return;

            HandleRoomState(roomCode, room);
        };

        watchedRoomRef.ValueChanged += roomWatcher;

        Debug.Log("[ROOM WATCH] Watching room " + roomCode);
    }

    private void StopWatchingRoom()
    {
        if (watchedRoomRef != null &&
            roomWatcher != null)
        {
            watchedRoomRef.ValueChanged -= roomWatcher;
        }

        watchedRoomRef = null;
        roomWatcher = null;
    }

    private void OnRoomValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError("[PregamePanel] Room listener error: " + args.DatabaseError.Message);
            return;
        }

        if (args.Snapshot == null || !args.Snapshot.Exists)
        {
            Debug.LogWarning("[PregamePanel] Room snapshot missing or room deleted.");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[PregamePanel] Room snapshot JSON was empty.");
            return;
        }

        RoomData room = JsonUtility.FromJson<RoomData>(json);

        if (room == null)
        {
            Debug.LogError("[PregamePanel] Failed to parse RoomData from JSON.");
            return;
        }

        Debug.Log("[PregamePanel] Room changed. Code=" + room.code + ", Status=" + room.status);

        bool roomFull = !string.IsNullOrEmpty(room.guestUid) && room.status == "full";
        bool matchExists = !string.IsNullOrEmpty(room.matchId);
        bool isHost = IsSignedIn() && auth != null && auth.CurrentUser != null && room.hostUid == auth.CurrentUser.UserId;

        Debug.Log("[PregamePanel] roomFull=" + roomFull +
                  ", isHost=" + isHost +
                  ", hostUid=" + room.hostUid +
                  ", currentUid=" + (auth != null && auth.CurrentUser != null ? auth.CurrentUser.UserId : "null"));

        if (roomFull)
        {
            Debug.Log("[PregamePanel] Room is full. A game can begin.");
        }
        else
        {
            Debug.Log("[PregamePanel] Room is waiting for another player.");
        }

        if (startGameButton != null)
        {
            bool canStart = roomFull || matchExists;
            Debug.Log("[PregamePanel] Setting startGameButton.interactable = " + canStart);
            startGameButton.interactable = canStart;
            startGameButton.image.color = canStart ? Color.green : Color.gray;
            Debug.Log("[PregamePanel] AFTER SET: interactable=" + startGameButton.interactable);
        }
        else
        {
            Debug.LogWarning("[PregamePanel] startGameButton is NULL in OnRoomValueChanged!");
        }

        Debug.Log("[PregamePanel] AFTER RunOnMainThread call queued");

        if (!string.IsNullOrEmpty(room.matchId) && room.status == "in_game")
        {
            Debug.Log("[PregamePanel] Match exists. Waiting for player to press Start.");

            if (watchedMatchId != room.matchId || currentMatch == null)
            {
                WatchMatch(room.matchId, false);
            }

            return;
        }
    }

    private void EnterGameplayMode()
    {
        Debug.Log("[ENTER GAMEPLAY] uid=" +auth.CurrentUser.UserId +" pendingEnterGameplay=" + pendingEnterGameplay + " matchId=" + (currentMatch != null ? currentMatch.matchId : "NULL"));
        TraceMatch("EnterGameplayMode ENTER");
        Debug.Log($"[PREGAME] EnterGameplayMode CALLED | frame={Time.frameCount}");

        if (gameLogic == null) { Debug.LogError("[PREGAME] gameLogic is NULL"); TraceMatch("EnterGameplayMode ABORT gameLogic NULL"); return; }
        if (currentMatch == null) { Debug.LogError("[PREGAME] currentMatch is NULL"); TraceMatch("EnterGameplayMode ABORT currentMatch NULL"); return; }
        if (auth == null || auth.CurrentUser == null) { Debug.LogError("[PREGAME] auth/current user is NULL"); TraceMatch("EnterGameplayMode ABORT auth/current user NULL"); return; }

        // Switch panels FIRST — Singleton/UIManager may live under gameplayPanel
        // and won't be ready (Awake hasn't run) until it's actually active.
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (matchStatusPanel != null) matchStatusPanel.gameObject.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        string uid = auth.CurrentUser.UserId;
        bool isPlayer1 = currentMatch.player1Uid == uid;

        Debug.Log(
            $"[PREGAME] EnterGameplayMode PREP | " +
            $"matchId={currentMatch.matchId} | " +
            $"status={currentMatch.status} | " +
            $"turn={currentMatch.currentRoundNumber} | " +
            $"uid={uid} | " +
            $"isPlayer1={isPlayer1} | " +
            $"sharedRackJsonNull={string.IsNullOrEmpty(currentMatch.sharedrackjson)}"
        );
        Debug.Log("[PREGAME] sharedRackJson RAW = " + currentMatch.sharedrackjson);
        List<LetterInfo> localRack = ParseRackJson(currentMatch.sharedrackjson);
        Debug.Log("[PREGAME] localRack parsed count = " + (localRack == null ? -1 : localRack.Count));
        if (localRack == null)
        {
            Debug.LogWarning("[PREGAME] ParseRackJson returned null. Replacing with empty rack.");
            localRack = new List<LetterInfo>();
        }

        int localScore = isPlayer1 ? currentMatch.player1Score : currentMatch.player2Score;
        int opponentScore = isPlayer1 ? currentMatch.player2Score : currentMatch.player1Score;

        Debug.Log("[PREGAME] Local player is " + (isPlayer1 ? "P1" : "P2"));
        Debug.Log("[PREGAME] Local rack count = " + localRack.Count);
        Debug.Log("[PREGAME] Local score = " + localScore + ", Opponent score = " + opponentScore);

        try
        {
            gameLogic.BeginOnlineMatchFromRack(
                7,
                15,
                15,
                localRack,
                localScore,
                opponentScore,
                currentMatch.currentRoundNumber,
                currentMatch.bonusBoardJson
            );

            Debug.Log("[PREGAME] BeginOnlineMatchFromRack completed successfully.");
            TraceMatch("EnterGameplayMode SUCCESS");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[PREGAME] BeginOnlineMatchFromRack threw exception: " + ex);
            TraceMatch("EnterGameplayMode EXCEPTION");
        }
    }

    private List<LetterInfo> CloneLetterList(List<LetterInfo> source)
    {
        List<LetterInfo> clone = new List<LetterInfo>();

        if (source == null)
            return clone;

        foreach (LetterInfo tile in source)
        {
            if (tile == null)
                continue;

            clone.Add(new LetterInfo(tile));
        }

        return clone;
    }
    private void OnDestroy()
    {
        StopWatchingRoom();
        StopWatchingMatch();

        if (auth != null)
        {
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady -= OnOnlineSubmissionReady;
    }

    public void OnStartGamePressed()
    {
        Debug.Log("[PregamePanel] OnStartGamePressed CALLED");

        if (auth == null || auth.CurrentUser == null)
        {
            SetStatus("You must be signed in.");
            return;
        }

        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(roomCode))
        {
            SetStatus("Enter a room code first.");
            return;
        }

        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
            return;
        }

        DatabaseReference roomRef = dbRoot.Child("rooms").Child(roomCode);

        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to load room: " + task.Exception);
                SetStatus("Failed to load room.");
                return;
            }

            if (!task.IsCompleted || task.Result == null || !task.Result.Exists)
            {
                SetStatus("Room not found.");
                return;
            }

            string roomJson = task.Result.GetRawJsonValue();
            RoomData room = JsonUtility.FromJson<RoomData>(roomJson);

            if (room == null)
            {
                SetStatus("Could not parse room data.");
                return;
            }

            if (string.IsNullOrEmpty(room.guestUid))
            {
                SetStatus("Cannot start yet. Waiting for guest.");
                return;
            }

            if (!string.IsNullOrEmpty(room.matchId) && room.status == "in_game")
            {
                Debug.Log("[PregamePanel] Loading existing match: " + room.matchId);

                Debug.Log($"[STARTFLOW] Existing match detected matchId={room.matchId}");
                AddMatchToUser(room.matchId);
                WatchMatch(room.matchId, true);
                Debug.Log("[STARTFLOW] WatchMatch called for existing match");

                return;
            }

            Debug.Log($"[STARTFLOW] About to call TryCreateInitialMatchFromRoom roomCode={roomCode} matchId={(room.matchId ?? "null")}");
            TryCreateInitialMatchFromRoom(roomCode, room);
            Debug.Log("[STARTFLOW] Returned from TryCreateInitialMatchFromRoom call");
        });
    }

    private string NewTileId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private void AddTiles(BagStateData bag, string letter, int value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            bag.tiles.Add(new TileData
            {
                letter = letter,
                value = value,
                id = NewTileId()
            });
        }
    }

    private BagStateData CreateInitialBag()
    {
        BagStateData bag = new BagStateData();

        AddTiles(bag, "A", 1, 9);
        AddTiles(bag, "B", 3, 2);
        AddTiles(bag, "C", 3, 2);
        AddTiles(bag, "D", 2, 4);
        AddTiles(bag, "E", 1, 12);
        AddTiles(bag, "F", 4, 2);
        AddTiles(bag, "G", 2, 3);
        AddTiles(bag, "H", 4, 2);
        AddTiles(bag, "I", 1, 9);
        AddTiles(bag, "J", 8, 1);
        AddTiles(bag, "K", 5, 1);
        AddTiles(bag, "L", 1, 4);
        AddTiles(bag, "M", 3, 2);
        AddTiles(bag, "N", 1, 6);
        AddTiles(bag, "O", 1, 8);
        AddTiles(bag, "P", 3, 2);
        AddTiles(bag, "Q", 10, 1);
        AddTiles(bag, "R", 1, 6);
        AddTiles(bag, "S", 1, 4);
        AddTiles(bag, "T", 1, 6);
        AddTiles(bag, "U", 1, 4);
        AddTiles(bag, "V", 4, 2);
        AddTiles(bag, "W", 4, 2);
        AddTiles(bag, "X", 8, 1);
        AddTiles(bag, "Y", 4, 2);
        AddTiles(bag, "Z", 10, 1);

        ShuffleTiles(bag.tiles);
        return bag;
    }

    private void ShuffleTiles(List<TileData> tiles)
    {
        for (int i = tiles.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            TileData temp = tiles[i];
            tiles[i] = tiles[j];
            tiles[j] = temp;
        }
    }

    private RackStateData DrawTiles(BagStateData bag, int count)
    {
        RackStateData rack = new RackStateData();

        int drawCount = Mathf.Min(count, bag.tiles.Count);

        for (int i = 0; i < drawCount; i++)
        {
            rack.tiles.Add(bag.tiles[0]);
            bag.tiles.RemoveAt(0);
        }

        return rack;
    }

    private BoardStateData CreateInitialBoard(int width = 9, int height = 9)
    {
        BoardStateData board = new BoardStateData
        {
            width = width,
            height = height
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                board.cells.Add(new BoardCellData
                {
                    x = x,
                    y = y,
                    occupied = false,
                    tile = null
                });
            }
        }

        return board;
    }

    private string ToJson<T>(T obj)
    {
        return JsonUtility.ToJson(obj);
    }

    private string GetRackDebugString(RackStateData rack)
    {
        if (rack == null || rack.tiles == null || rack.tiles.Count == 0)
            return "(empty)";

        List<string> parts = new List<string>();

        for (int i = 0; i < rack.tiles.Count; i++)
        {
            TileData tile = rack.tiles[i];
            parts.Add(tile.letter + tile.value);
        }

        return string.Join(", ", parts);
    }

    private void OnOnlineSubmissionReady(RoundMove move)
    {
        if (currentMatch == null || auth == null || auth.CurrentUser == null)
            return;

        SubmitRoundMove(move);
    }

    private BoardCellData FindCell(BoardStateData board, int x, int y)
    {
        foreach (var cell in board.cells)
        {
            if (cell.x == x && cell.y == y)
                return cell;
        }

        return null;
    }

    private void TryCreateInitialMatchFromRoom(string roomCode, RoomData roomSnapshot)
    {
        Debug.Log($"[STARTFLOW] TryCreateInitialMatchFromRoom ENTER roomCode={roomCode} matchId={(roomSnapshot != null ? roomSnapshot.matchId : "null")}");

        if (auth == null || auth.CurrentUser == null)
            return;

        string myUid = auth.CurrentUser.UserId;
        DatabaseReference roomRef = dbRoot.Child("rooms").Child(roomCode);

        SetStatus("Attempting to start game...");

        roomRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null)
                return TransactionResult.Abort();

            var roomDict = mutableData.Value as Dictionary<string, object>;
            if (roomDict == null)
                return TransactionResult.Abort();

            string existingMatchId = roomDict.ContainsKey("matchId") && roomDict["matchId"] != null
                ? roomDict["matchId"].ToString()
                : "";

            string status = roomDict.ContainsKey("status") && roomDict["status"] != null
                ? roomDict["status"].ToString()
                : "";

            string guestUid = roomDict.ContainsKey("guestUid") && roomDict["guestUid"] != null
                ? roomDict["guestUid"].ToString()
                : "";

            if (string.IsNullOrEmpty(guestUid))
                return TransactionResult.Abort();

            if (!string.IsNullOrEmpty(existingMatchId) || status == "in_game")
                return TransactionResult.Abort();

            string newMatchId = dbRoot.Child("matches").Push().Key;

            roomDict["matchId"] = newMatchId;
            roomDict["status"] = "starting";
            roomDict["startedByUid"] = myUid;
            roomDict["startedAtUnix"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            mutableData.Value = roomDict;
            return TransactionResult.Success(mutableData);
        }).ContinueWithOnMainThread(txTask =>
        {
            if (txTask.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Room transaction failed: " + txTask.Exception);
                SetStatus("Failed to claim game start.");
                return;
            }

            roomRef.GetValueAsync().ContinueWithOnMainThread(readBackTask =>
            {
                if (readBackTask.IsFaulted || readBackTask.Result == null || !readBackTask.Result.Exists)
                {
                    SetStatus("Failed to verify room state.");
                    return;
                }

                string updatedRoomJson = readBackTask.Result.GetRawJsonValue();
                RoomData updatedRoom = JsonUtility.FromJson<RoomData>(updatedRoomJson);

                if (updatedRoom == null || string.IsNullOrEmpty(updatedRoom.matchId))
                {
                    SetStatus("Failed to verify match creation.");
                    return;
                }

                if (updatedRoom.status == "in_game")
                {
                    Debug.Log("[PregamePanel] Someone already created the match. matchId=" + updatedRoom.matchId);
                    WatchMatch(updatedRoom.matchId,false);
                    //EnterGameplayMode();
                    return;
                }

                bool iClaimedStart = updatedRoom.status == "starting" &&
                                     updatedRoom.matchId != null &&
                                     readBackTask.Result.Child("startedByUid").Value != null &&
                                     readBackTask.Result.Child("startedByUid").Value.ToString() == myUid;

                if (!iClaimedStart)
                {
                    Debug.Log("[PregamePanel] Another client claimed start. Waiting for final match...");
                    if (!string.IsNullOrEmpty(updatedRoom.matchId))
                    {
                        WatchMatch(updatedRoom.matchId,false);
                        //EnterGameplayMode();
                    }
                    return;
                }

                string matchId = updatedRoom.matchId;

                BagStateData bag = CreateInitialBag();
                RackStateData sharedrackjson = DrawTiles(bag, 7);
                BoardStateData board = CreateInitialBoard();

                Debug.Log("[BONUS] gameLogic reference null? " + (gameLogic == null));
                string bonusBoardJson = "";

                if (gameLogic != null)
                {
                    // This snapshot uses a 9x9 board via CreateInitialBoard(9, 9) [13].
                    gameLogic.SetBoardSize(9, 9);

                    bonusBoardJson = gameLogic.GenerateBonusBoardJsonForOnlineMatch();
                    Debug.Log("[BONUS] bonusBoardJson length=" + (bonusBoardJson == null ? -1 : bonusBoardJson.Length));
                }
                else
                {
                    Debug.LogWarning("[BONUS] gameLogic was NULL in TryCreateInitialMatchFromRoom. Skipping bonus board JSON.");
                }

                MatchData match = new MatchData
                {
                    matchId = matchId,
                    roomCode = roomCode,

                    hostUid = updatedRoom.hostUid,
                    guestUid = updatedRoom.guestUid,

                    player1Uid = updatedRoom.hostUid,
                    player2Uid = updatedRoom.guestUid,
                    player1DisplayName = updatedRoom.hostDisplayName,
                    player2DisplayName = updatedRoom.guestDisplayName,

                    player1Score = 0,
                    player2Score = 0,

                    status = "active",
                    currentRoundNumber = 1,

                    boardStateJson = JsonUtility.ToJson(board),
                    bagStateJson = JsonUtility.ToJson(bag),
                    sharedrackjson = JsonUtility.ToJson(sharedrackjson),
                    
                    bonusBoardJson = bonusBoardJson,

                    lastRoundResultJson = "",
                    roundResolutionStatus = "idle",
                    roundResolutionByUid = "",

                    totalRounds = matchStatusPanel != null ? matchStatusPanel.GetRoundCount() : 5,

                    createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    setupStatus = "done",
                    setupByUid = myUid,
                    setupAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    stateVersion = 1
                };

                string matchJson = JsonUtility.ToJson(match);

                dbRoot.Child("matches").Child(matchId).SetRawJsonValueAsync(matchJson)
                    .ContinueWithOnMainThread(writeMatchTask =>
                    {
                        if (writeMatchTask.IsFaulted)
                        {
                            Debug.LogError("[PregamePanel] Failed writing match: " + writeMatchTask.Exception);
                            SetStatus("Failed to write match.");
                            return;
                        }

                        updatedRoom.status = "in_game";
                        string finalRoomJson = JsonUtility.ToJson(updatedRoom);

                        roomRef.SetRawJsonValueAsync(finalRoomJson).ContinueWithOnMainThread(writeRoomTask =>
                        {
                            if (writeRoomTask.IsFaulted)
                            {
                                Debug.LogError("[PregamePanel] Failed writing room final state: " + writeRoomTask.Exception);
                                SetStatus("Match created, but room update failed.");
                                return;
                            }

                            Debug.Log("[PregamePanel] Match created: " + matchId);
                            Debug.Log("[PregamePanel] Player1 rack: " + GetRackDebugString(sharedrackjson));
                            Debug.Log("[PregamePanel] Player2 rack: " + GetRackDebugString(sharedrackjson));
                            Debug.Log("[PregamePanel] Bag tiles remaining: " + bag.tiles.Count);

                            AddMatchToUser(matchId); // for the local (host) player
                            RemoveRoomFromUser(updatedRoom.hostUid, roomCode);
                            RemoveRoomFromUser(updatedRoom.guestUid, roomCode);

                            SetStatus("Game started.");
                            WatchMatch(matchId,true);
                        });
                    });
            });
        });
    }
    public void SetRoomCodeInput(string code)
    {
        if (roomCodeInput == null)
            return;

        roomCodeInput.text = code;
        roomCodeInput.SetTextWithoutNotify(code);
        roomCodeInput.ForceLabelUpdate();
    }
    private List<LetterInfo> ParseRackJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<LetterInfo>();

        RackStateData rack = JsonUtility.FromJson<RackStateData>(json);
        List<LetterInfo> result = new List<LetterInfo>();

        if (rack != null && rack.tiles != null)
        {
            foreach (var tile in rack.tiles)
            {
                if (tile == null) continue;
                result.Add(new LetterInfo(tile.letter, tile.value));
            }
        }

        return result;
    }
    private void SubmitRoundMove(RoundMove move)
    {
        if (!EnsureFirebaseReady() || currentMatch == null || auth.CurrentUser == null)
        {
            SetStatus("Match not ready.");
            return;
        }

        if (currentMatch.status != "active")
        {
            SetStatus("Match is not active.");
            return;
        }

        string uid = auth.CurrentUser.UserId;
        int roundNumber = currentMatch.currentRoundNumber;

        RoundSubmissionData submission = new RoundSubmissionData
        {
            uid = uid,
            word = move != null ? move.word : "",
            score = move != null ? move.score : 0,
            isValid = move != null && move.isValid,
            simulatedTilesJson = SerializeSimulatedTiles(move),
            submittedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        string json = JsonUtility.ToJson(submission);

        dbRoot.Child("matches").Child(currentMatch.matchId)
            .Child("rounds").Child(roundNumber.ToString())
            .Child("submissions").Child(uid)
            .SetRawJsonValueAsync(json)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("[PregamePanel] Failed to submit round move: " + task.Exception);
                    SetStatus("Failed to submit move.");
                    return;
                }

                Debug.Log("[PregamePanel] Round " + roundNumber + " submission written.");
                SetStatus("Move submitted. Waiting for other players...");

                pendingResolutionMatchId = currentMatch.matchId; // remember: I'm actively waiting on this match

                gameLogic.StartCoroutine(ShowSubmittedWaitingSequence());
            });
    }

    private IEnumerator ShowSubmittedWaitingSequence()
    {
        if (gameLogic != null)
            gameLogic.SetInputLocked(true);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Move submitted!");

        yield return new WaitForSeconds(1.5f);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Waiting for other players...");

        yield return new WaitForSeconds(1.5f);

        if (pendingResolutionMatchId == null)
            yield break; // already redirected to game-over panel — don't switch to MatchStatusPanel

        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (matchStatusPanel != null)
        {
            matchStatusPanel.gameObject.SetActive(true);
            matchStatusPanel.OnRefreshPressed();
        }
    }

    private string SerializeSimulatedTiles(RoundMove move)
    {
        List<SimPlacedTileData> list = new List<SimPlacedTileData>();

        if (move != null && move.isValid && move.simulatedTiles != null)
        {
            foreach (var sim in move.simulatedTiles)
            {
                if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                    continue;

                list.Add(new SimPlacedTileData
                {
                    letter = sim.letterInfo.letter,
                    points = sim.letterInfo.points,
                    row = sim.letterPosition.RowX,
                    col = sim.letterPosition.ColY
                });
            }
        }

        return JsonUtility.ToJson(new SimTileListWrapper { tiles = list });
    }

    public void TryResumeActiveMatch()
    {
        if (!EnsureFirebaseReady())
            return;

        if (auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("matches")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.Result == null)
                {
                    Debug.LogError("[RESUME] Failed to load matches.");
                    ShowPregamePanel();
                    return;
                }

                foreach (var child in task.Result.Children)
                {
                    string raw = child.GetRawJsonValue();

                    if (string.IsNullOrEmpty(raw))
                        continue;

                    MatchData match =
                        JsonUtility.FromJson<MatchData>(raw);

                    if (match == null)
                        continue;

                    if (match.status != "active")
                        continue;

                    bool belongsToUser =
                        match.player1Uid == uid ||
                        match.player2Uid == uid;

                    if (!belongsToUser)
                        continue;

                    Debug.Log(
                        "[RESUME] Found active match: " +
                        match.matchId);

                    currentMatch = match;

                    WatchMatch(match.matchId,true);

                    //pendingEnterGameplay = true;

                    //EnterGameplayMode();

                    return;
                }

                Debug.Log("[RESUME] No active match found.");

                ShowPregamePanel();
            });
    }
    public void ShowPregamePanel()
    {
        if (optionPanel != null)
            optionPanel.SetActive(false);

        if (pregamePanel != null)
            pregamePanel.SetActive(true);

        if (gameplayPanel != null)
            gameplayPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        Debug.Log("[RESUME] Showing pregame panel");
    }

    public void OnSwitchTestUserPressed()
    {
        if (auth == null)
        {
            Debug.LogWarning("[PregamePanel] Cannot switch test user, auth is null.");
            return;
        }

        string currentEmail = auth.CurrentUser != null ? auth.CurrentUser.Email : null;

        string targetEmail = (currentEmail == TestUserA_Email)
            ? TestUserB_Email
            : TestUserA_Email;

        Debug.Log("[PregamePanel] Switching test user: " + (currentEmail ?? "none") + " -> " + targetEmail);

        if (auth.CurrentUser != null)
            auth.SignOut();

        SetStatus("Switching to " + targetEmail + "...");

        auth.SignInWithEmailAndPasswordAsync(targetEmail, TestUserPassword).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Switch canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                string err = GetFirebaseErrorMessage(task.Exception);
                Debug.LogError("[PregamePanel] Switch test user failed: " + err);
                RunOnMainThread(() => SetStatus("Switch failed: " + err));
                return;
            }

            FirebaseUser signedInUser = task.Result.User;

            RunOnMainThread(() =>
            {
                Debug.Log("[PregamePanel] RunOnMainThread action START for switch to " + targetEmail);

                string shownName = string.IsNullOrWhiteSpace(signedInUser.DisplayName)
                    ? signedInUser.Email
                    : signedInUser.DisplayName;

                SetStatus("Switched to: " + shownName);
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                RefreshUI();

                if (matchStatusPanel != null)
                {
                    Debug.Log("[PregamePanel] Calling RefreshMatchStateForUser with uid=" + signedInUser.UserId);
                    matchStatusPanel.gameObject.SetActive(true);
                    matchStatusPanel.UpdateLoginNameDisplay();
                    matchStatusPanel.RefreshMatchStateForUser(signedInUser.UserId);
                }
                else
                {
                    Debug.LogWarning("[PregamePanel] matchStatusPanel is NULL in switch-user callback!");

                }
                Debug.Log("[PregamePanel] RunOnMainThread action END");
            });
        });
    }

    private bool EnsureFirebaseReady()
    {
        if (dbRoot != null && auth != null)
            return true;

        if (FirebaseInit.IsReady && FirebaseInit.Database != null)
        {
            dbRoot = FirebaseInit.Database.RootReference;
            auth = FirebaseInit.Auth;
            firebaseInitialized = true;
            Debug.Log("[PreGamePanel] Firebase self-healed via EnsureFirebaseReady.");
            return true;
        }

        Debug.LogWarning("[PreGamePanel] EnsureFirebaseReady failed: Firebase not ready yet.");
        return false;
    }
    private void RemoveRoomFromUser(string uid, string roomCode)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(roomCode))
            return;

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
                return;

            UserData userData = JsonUtility.FromJson<UserData>(task.Result.GetRawJsonValue());
            if (userData == null || userData.activeRoomIds == null)
                return;

            if (userData.activeRoomIds.Remove(roomCode))
            {
                dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(JsonUtility.ToJson(userData));
            }
        });
    }
}
