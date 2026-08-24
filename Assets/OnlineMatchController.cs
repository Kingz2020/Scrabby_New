using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// Central controller for online match lifecycle:
/// - Watch a match node
/// - Handle match updates and round progression
/// - Submit moves and run waiting flow
/// - Resolve rounds and update board/bag/rack
/// - Show game-over
/// 
/// Intended to live on a persistent GameObject (e.g. under DontDestroyOnLoad),
/// independent of UI panels.
/// </summary>
public class OnlineMatchController : MonoBehaviour
{
    //public static OnlineMatchController Instance { get; private set; }
    [Header("Core References")]
    [SerializeField] private GameLogic gameLogic;
    [SerializeField] private UIManager uiManager;

    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject pregamePanel;
    [SerializeField] private MatchStatusPanel matchStatusPanel;

    [Header("Firebase")]
    private FirebaseDatabase database;
    private FirebaseAuth auth;

    // Internal state
    private DatabaseReference dbRoot;
    private DatabaseReference currentMatchRef;
    private DatabaseReference currentSubmissionsRef;

    [Header("UI Panels")]
    [SerializeField] private PreGamePanel preGamePanel;

    private MatchData currentMatch;
    private string watchedMatchId;
    private bool pendingEnterGameplay;
    private string pendingResolutionMatchId;
    private int watchedRoundNumber = -1;
    private int lastProcessedRound = 0;

    private int matchTraceSeq = 0;

    public MatchData CurrentMatch => currentMatch;

    //private string currentMatchId;
    public event System.Action<MatchData> OnMatchUpdated;

    private bool viewingOnlineMatchResult;

    #region Init

    private void Awake()
    {
        // Try to seed from FirebaseInit, but don't crash if it's not ready yet.
        if (FirebaseInit.IsReady)
        {
            database = FirebaseInit.Database;
            auth = FirebaseInit.Auth;
            if (database != null)
                dbRoot = database.RootReference;
        }
        else
        {
            Debug.LogWarning("[OnlineMatchController] FirebaseInit not ready in Awake; will self-heal later.");
        }

        if (uiManager == null && Singleton.Instance != null)
            uiManager = Singleton.Instance.UIManager;

        if (gameLogic == null && Singleton.Instance != null)
            gameLogic = Singleton.Instance.GameLogic;

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady += SubmitRoundMove;
    }

    private FirebaseUser GetCurrentUser()
    {
        return auth != null ? auth.CurrentUser : null;
    }

    private void OnDestroy()
    {
        if (gameLogic != null)
            gameLogic.onlineSubmissionReady -= SubmitRoundMove;

        StopWatchingCurrentMatch();
    }

    private bool EnsureFirebaseReady()
    {
        // If we already have everything, good.
        if (dbRoot != null && auth != null)
            return true;

        // If FirebaseInit is not done yet, we really aren't ready.
        if (!FirebaseInit.IsReady)
        {
            Debug.LogWarning("[OnlineMatchController] EnsureFirebaseReady: FirebaseInit.IsReady is false.");
            return false;
        }

        // Grab instances from FirebaseInit
        if (database == null)
            database = FirebaseInit.Database;

        if (auth == null)
            auth = FirebaseInit.Auth;

        if (database != null && dbRoot == null)
            dbRoot = database.RootReference;

        bool ready = (dbRoot != null && auth != null);
        Debug.Log("[OnlineMatchController] EnsureFirebaseReady: ready=" + ready);
        return ready;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Start watching a match. If enterWhenReady is true,
    /// we will attempt to enter gameplay when a snapshot arrives.
    /// </summary>
    public void WatchMatch(string matchId, bool enterWhenReady)
    {
        if (!EnsureFirebaseReady())
        {
            Debug.LogWarning("[OnlineMatchController] WatchMatch aborted: Firebase not ready.");
            return;
        }

        TraceMatch("WatchMatch ENTER");

        if (currentMatchRef != null)
        {
            currentMatchRef.ValueChanged -= OnMatchValueChanged;
            currentMatchRef = null;
        }

        watchedMatchId = matchId;

        if (enterWhenReady)
            pendingEnterGameplay = true;

        currentMatchRef = dbRoot.Child("matches").Child(matchId);
        currentMatchRef.ValueChanged += OnMatchValueChanged;

        TraceMatch("WatchMatch AFTER subscribe");
    }

    

    /// <summary>
    /// Called by UI when the local player submits a move.
    /// </summary>
    public void SubmitRoundMove(RoundMove move)
    {
        if (!EnsureFirebaseReady() || currentMatch == null || GetCurrentUser() == null)
        {
            Debug.LogWarning("[OnlineMatchController] SubmitRoundMove: match not ready.");
            return;
        }

        if (currentMatch.status != "active")
        {
            Debug.LogWarning("[OnlineMatchController] SubmitRoundMove: match is not active.");
            return;
        }

        string uid = GetCurrentUser().UserId;
        int roundNumber = currentMatch.currentRoundNumber;

                Debug.Log(
            "[SUBMIT DIAGNOSTIC]" +
            " uid=" + uid +
            " matchId=" + currentMatch.matchId +
            " round=" + roundNumber +
            " move.word=" + (move != null ? move.word : "NULL") +
            " move.score=" + (move != null ? move.score.ToString() : "NULL") +
            " simulatedTiles=" + SerializeSimulatedTiles(move)
        );

        float secondsRemaining = 0f;

        if (Singleton.Instance?.GameLogic != null)
        {
            secondsRemaining = Mathf.Max(0f, Singleton.Instance.GameLogic.GetRemainingRoundTime());
        }

        RoundSubmissionData submission = new RoundSubmissionData
        {
            uid = uid,
            word = move != null ? move.word : "",
            score = move != null ? move.score : 0,
            isValid = move != null && move.isValid,
            simulatedTilesJson = SerializeSimulatedTiles(move),

            secondsRemaining = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining)),

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
                      Debug.LogError("[OnlineMatchController] Failed to submit round move: " + task.Exception);
                      if (uiManager != null)
                          uiManager.ShowRoundMessage("Failed to submit move.");
                      return;
                  }

                  Debug.Log("[OnlineMatchController] Round " + roundNumber + " submission written.");
                  if (uiManager != null)
                      uiManager.ShowRoundMessage("Move submitted. Waiting for other players...");

                  // Remember: actively waiting on this match
                  pendingResolutionMatchId = currentMatch.matchId;

                  // Run the waiting sequence on this controller
                  if (this != null && gameObject.activeInHierarchy)
                      StartCoroutine(ShowSubmittedWaitingSequence());
              });
    }

    /// <summary>
    /// Simple wrapper used by UI to resume a watched match.
    /// </summary>
    public void ResumeMatch(string matchId)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            Debug.LogWarning("[OnlineMatchController] ResumeMatch called with null/empty matchId");
            return;
        }

        Debug.Log("[OnlineMatchController] ResumeMatch called with matchId=" + matchId);

        WatchMatch(matchId, enterWhenReady: true);
    }
    /// <summary>
    /// Show game-over panel for a given matchId.
    /// </summary>

    public void ShowGameOverForMatchId(string matchId)
    {
        if (matchStatusPanel != null)
            matchStatusPanel.gameObject.SetActive(false);

        if (string.IsNullOrEmpty(matchId))
        {
            Debug.LogWarning("[OnlineMatchController] Cannot show game-over: matchId is empty.");
            return;
        }

        if (!EnsureFirebaseReady())
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot show game-over: Firebase is not ready."
            );
            return;
        }

        DatabaseReference matchRef = dbRoot
            .Child("matches")
            .Child(matchId);

        matchRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
            {
                Debug.LogWarning(
                    "[OnlineMatchController] Could not load completed match: " + matchId
                );
                return;
            }

            MatchData match = JsonUtility.FromJson<MatchData>(
                task.Result.GetRawJsonValue()
            );

            if (match == null)
            {
                Debug.LogWarning(
                    "[OnlineMatchController] Could not parse completed MatchData: " + matchId
                );
                return;
            }
            viewingOnlineMatchResult = true;
            ShowGameOverForMatch(match);
        });
    }

    public void PlayAgainFromResults()
    {
        Debug.Log("[ONLINE] PlayAgainFromResults CALLED");

        // We are leaving the old completed match result.
        viewingOnlineMatchResult = false;

        // Hide the result panel.
        if (Singleton.Instance != null &&
            Singleton.Instance.UIManager != null &&
            Singleton.Instance.UIManager.gameOverPanel != null)
        {
            Singleton.Instance.UIManager.gameOverPanel.SetActive(false);
        }

        // Show the Match Status / online room screen again.
        if (matchStatusPanel != null)
            matchStatusPanel.gameObject.SetActive(true);

        // TEMPORARY:
        // This only returns the player to the online match list.
        // Replace this part with your existing "create room" method once we
        // identify its exact name and required parameters.
        //matchStatusPanel.RefreshMatchList();
    }

    public void ClearViewingOnlineMatchResult()
    {
        viewingOnlineMatchResult = false;
    }

    public bool IsViewingOnlineMatchResult()
    {
        return viewingOnlineMatchResult;
    }

    public void ShowGameOverForMatch(MatchData match)
    {
        if (uiManager == null)
            uiManager = Singleton.Instance != null ? Singleton.Instance.UIManager : null;

        if (uiManager == null)
        {
            Debug.LogWarning("[OnlineMatchController] Could not find UIManager to show game-over panel.");
            return;
        }

        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : null;
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
            $"Final score: {myScore} - {opponentName} {opponentScore} " +
            $"(played {match.totalRounds} rounds)";

        if (match.roundScores != null && match.roundScores.Count > 0)
        {
            roundSummary += "\n\nRound scores";

            foreach (RoundScoreLine round in match.roundScores)
            {
                int myRoundScore = amPlayer1 ? round.player1Score : round.player2Score;
                int opponentRoundScore = amPlayer1 ? round.player2Score : round.player1Score;

                roundSummary +=
                    $"\nRound {round.roundNumber}: " +
                    $"You {myRoundScore} - {opponentName} {opponentRoundScore}";
            }
        }

        uiManager.ShowGameOverPanel(finalMessage, roundSummary);

        /* StartCoroutine(
             LoadOnlineRoundHistoryForGameOver(
                 match,
                 finalMessage,
                 opponentName,
                 amPlayer1
             )*/
        //StartCoroutine(LoadOnlineRoundReplayRows(match,amPlayer1,opponentName));

        ShowOnlineRoundReplayRows(match,amPlayer1,opponentName);
        //);
    }

    /*private IEnumerator LoadOnlineRoundReplayRows(
    MatchData match,
    bool amPlayer1,
    string opponentName)
    {
        if (match == null ||
            string.IsNullOrEmpty(match.matchId))
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot load round replay: missing match."
            );
            yield break;
        }

        if (!EnsureFirebaseReady())
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot load round replay: Firebase is not ready."
            );
            yield break;
        }

        DatabaseReference roundsRef = dbRoot
            .Child("matches")
            .Child(match.matchId)
            .Child("rounds");

        var task = roundsRef.GetValueAsync();

        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted ||
            task.IsCanceled ||
            task.Result == null ||
            !task.Result.Exists)
        {
            Debug.LogWarning(
                "[OnlineMatchController] No round replay data found for match " +
                match.matchId
            );
            yield break;
        }

        List<OnlineRoundHistoryEntry> history =
            new List<OnlineRoundHistoryEntry>();

        foreach (DataSnapshot roundSnapshot in task.Result.Children)
        {
            OnlineRoundHistoryEntry entry =
                ParseOnlineRoundHistoryEntry(roundSnapshot);

            if (entry != null)
                history.Add(entry);
        }

        history.Sort((a, b) =>
            a.roundNumber.CompareTo(b.roundNumber)
        );

        if (uiManager == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot display round replay: UIManager is null."
            );
            yield break;
        }

        uiManager.ShowOnlineRoundReplayRows(
            history,
            amPlayer1,
            opponentName,
            ReplayRoundFromGameOver
        );
    }
    */
    private IEnumerator LoadOnlineRoundHistoryForGameOver(
    MatchData match,
    string finalMessage,
    string opponentName,
    bool amPlayer1)
    {
        if (match == null || string.IsNullOrEmpty(match.matchId))
            yield break;

        if (!EnsureFirebaseReady())
            yield break;

        var historyRef = dbRoot
            .Child("matches")
            .Child(match.matchId)
            .Child("roundHistory");

        var task = historyRef.GetValueAsync();

        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted ||
            task.IsCanceled ||
            task.Result == null ||
            !task.Result.Exists)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Could not load game-over roundHistory."
            );
            yield break;
        }

        List<OnlineRoundHistoryEntry> history =
            new List<OnlineRoundHistoryEntry>();

        foreach (DataSnapshot child in task.Result.Children)
        {
            string rawJson = child.GetRawJsonValue();

            if (string.IsNullOrEmpty(rawJson))
                continue;

            OnlineRoundHistoryEntry entry =
                JsonUtility.FromJson<OnlineRoundHistoryEntry>(rawJson);

            if (entry != null)
                history.Add(entry);
        }

        history.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            return a.roundNumber.CompareTo(b.roundNumber);
        });

        if (history.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] game-over roundHistory was empty."
            );
            yield break;
        }

        int myFinalScore = amPlayer1
            ? match.player1Score
            : match.player2Score;

        int opponentFinalScore = amPlayer1
            ? match.player2Score
            : match.player1Score;

        string summary =
            $"Final score: {myFinalScore} - {opponentName} {opponentFinalScore} " +
            $"(played {history.Count} rounds)";

        summary += "\n\nRounds";

        foreach (OnlineRoundHistoryEntry round in history)
        {
            if (round == null)
                continue;

            string myWord = amPlayer1
                ? round.player1Word
                : round.player2Word;

            string opponentWord = amPlayer1
                ? round.player2Word
                : round.player1Word;

            int myScore = amPlayer1
                ? round.player1Score
                : round.player2Score;

            int opponentScore = amPlayer1
                ? round.player2Score
                : round.player1Score;

            bool iWonRound =
                !string.IsNullOrEmpty(round.winnerUid) &&
                round.winnerUid == GetCurrentUser()?.UserId;

            string winnerLabel;

            if (!round.anyValidMove)
            {
                winnerLabel = "No valid move";
            }
            else if (iWonRound)
            {
                winnerLabel = "You won";
            }
            else
            {
                winnerLabel = opponentName + " won";
            }

            summary +=
                $"\nRound {round.roundNumber}: " +
                $"{myWord} ({myScore}) vs " +
                $"{opponentWord} ({opponentScore}) — " +
                winnerLabel;
        }

        if (uiManager != null)
        {
            uiManager.UpdateGameOverSummary(
                finalMessage,
                summary
            );
        }

        Debug.Log(
            "[OnlineMatchController] Game-over round history loaded: " +
            history.Count + " rounds."
        );
    }


    

    #endregion

    #region Match listener



    #region Submissions / round resolution

    private void WatchSubmissionsForRound(int roundNumber)
    {
        if (currentMatch == null)
        {
            Debug.LogWarning("[OnlineMatchController] WatchSubmissionsForRound called with null currentMatch.");
            return;
        }

        // Detach old listener
        if (currentSubmissionsRef != null)
        {
            currentSubmissionsRef.ValueChanged -= OnSubmissionsValueChanged;
            currentSubmissionsRef = null;
        }

        watchedRoundNumber = roundNumber;

        currentSubmissionsRef = dbRoot.Child("matches")
                                      .Child(currentMatch.matchId)
                                      .Child("rounds").Child(roundNumber.ToString())
                                      .Child("submissions");

        currentSubmissionsRef.ValueChanged += OnSubmissionsValueChanged;

        Debug.Log("[OnlineMatchController] Now watching submissions for match " +
                  currentMatch.matchId + " round " + roundNumber);
    }

    private void OnSubmissionsValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null)
        {
            //currentMatch = null;
            return;
        }

        int submittedCount = (int)args.Snapshot.ChildrenCount;
        int expectedCount = 2; // host + guest; adjust when supporting more players

        Debug.Log("[OnlineMatchController] Round " + watchedRoundNumber +
                  " submissions: " + submittedCount + " / " + expectedCount);

        if (submittedCount >= expectedCount && currentMatch != null)
        {
            TryResolveRound(currentMatch.matchId, watchedRoundNumber);
        }
    }

    public void TryResolveRound(string matchId, int roundNumber)
    {
        Debug.Log("[OnlineMatchController] TryResolveRound START | matchId=" +
                  matchId + " round=" + roundNumber);

        DatabaseReference submissionsRef = dbRoot.Child("matches")
                                                 .Child(matchId)
                                                 .Child("rounds").Child(roundNumber.ToString())
                                                 .Child("submissions");

        submissionsRef.GetValueAsync().ContinueWithOnMainThread(readTask =>
        {
            if (readTask.IsFaulted || readTask.Result == null || !readTask.Result.Exists)
            {
                Debug.LogWarning("[OnlineMatchController] TryResolveRound ABORT: submissions read failed or none exist " +
                                 "| matchId=" + matchId + " round=" + roundNumber +
                                 " | faulted=" + readTask.IsFaulted);
                return;
            }

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

            Debug.Log("[OnlineMatchController] TryResolveRound after submissions read | matchId=" +
                      matchId + " round=" + roundNumber + " submissionsCount=" + submissions.Count);

            if (submissions.Count == 0)
            {
                Debug.LogWarning("[OnlineMatchController] TryResolveRound ABORT: submissions.Count == 0");
                return;
            }

            DatabaseReference matchRef = dbRoot.Child("matches").Child(matchId);

            matchRef.GetValueAsync().ContinueWithOnMainThread(matchReadTask =>
            {
                if (matchReadTask.IsFaulted ||
                    matchReadTask.Result == null ||
                    !matchReadTask.Result.Exists)
                {
                    Debug.LogError("[OnlineMatchController] Failed to read match for resolution: " +
                                   matchReadTask.Exception + " | matchId=" + matchId);
                    return;
                }

                string matchJson = matchReadTask.Result.GetRawJsonValue();
                MatchData liveMatch = JsonUtility.FromJson<MatchData>(matchJson);

                if (liveMatch == null)
                {
                    Debug.LogWarning("[OnlineMatchController] TryResolveRound ABORT: liveMatch null after parse.");
                    return;
                }

                Debug.Log("[OnlineMatchController] TryResolveRound MATCH SNAPSHOT | matchId=" +
                          liveMatch.matchId +
                          " currentRoundNumber=" + liveMatch.currentRoundNumber +
                          " roundResolutionStatus=" + liveMatch.roundResolutionStatus +
                          " status=" + liveMatch.status);

                if (liveMatch.currentRoundNumber != roundNumber)
                {
                    Debug.LogWarning("[OnlineMatchController] TryResolveRound ABORT: liveMatch.currentRoundNumber (" +
                                     liveMatch.currentRoundNumber + ") != requested round (" + roundNumber + ")");
                    return; // already resolved or stale
                }

                if (liveMatch.roundResolutionStatus == "resolving" ||
                    liveMatch.roundResolutionStatus == "done")
                {
                    Debug.LogWarning("[OnlineMatchController] TryResolveRound ABORT: roundResolutionStatus=" +
                                     liveMatch.roundResolutionStatus + " (someone else is/has resolved)");
                    return; // someone else is/has resolved
                }

                // Claim resolution using a transaction
                Debug.Log("[OnlineMatchController] TryResolveRound attempting transaction claim for matchId=" +
                          matchId + " round=" + roundNumber);

                matchRef.Child("roundResolutionStatus").RunTransaction(mutableData =>
                {
                    string current = mutableData.Value as string;

                    if (current == "resolving" || current == "done")
                    {
                        Debug.LogWarning("[OnlineMatchController] TryResolveRound TRANSACTION ABORT inside function: current status=" +
                                         current + " for matchId=" + matchId + " round=" + roundNumber);
                        return TransactionResult.Abort(); // someone already claimed it
                    }

                    mutableData.Value = "resolving";
                    return TransactionResult.Success(mutableData);
                })
                .ContinueWithOnMainThread(claimTask =>
                {
                    Debug.Log("[OnlineMatchController] TRANSACTION ENTER" +
                              " | matchId=" + matchId +
                              " round=" + roundNumber +
                              " taskStatus=" + claimTask.Status +
                              " taskType=" + claimTask.GetType().FullName);

                    if (claimTask.IsFaulted)
                    {
                        Debug.LogError("[OnlineMatchController] TRANSACTION FAULTED: " +
                                       claimTask.Exception);
                        return;
                    }

                    if (claimTask.IsCanceled)
                    {
                        Debug.LogWarning("[OnlineMatchController] TRANSACTION CANCELED" +
                                         " | matchId=" + matchId +
                                         " round=" + roundNumber);
                        return;
                    }

                    Task<DataSnapshot> transactionTask = claimTask as Task<DataSnapshot>;

                    if (transactionTask == null)
                    {
                        Debug.LogError("[OnlineMatchController] TRANSACTION TYPE ERROR: expected Task<DataSnapshot>, received " +
                                       claimTask.GetType().FullName);
                        return;
                    }

                    DataSnapshot transactionSnapshot = transactionTask.Result;

                    Debug.Log("[OnlineMatchController] TRANSACTION RESULT RECEIVED" +
                              " | snapshotNull=" + (transactionSnapshot == null) +
                              " | exists=" + (transactionSnapshot != null && transactionSnapshot.Exists) +
                              " | value=" +
                              (transactionSnapshot != null && transactionSnapshot.Value != null
                                  ? transactionSnapshot.Value.ToString()
                                  : "NULL"));

                    if (transactionSnapshot == null ||
                        !transactionSnapshot.Exists)
                    {
                        Debug.LogError("[OnlineMatchController] TRANSACTION RESULT INVALID — aborting resolution.");
                        return;
                    }

                    Debug.Log("[OnlineMatchController] TRANSACTION CLAIM COMPLETED — beginning re-read.");

                    matchRef.GetValueAsync().ContinueWithOnMainThread(reReadTask =>
                    {
                        if (reReadTask.IsFaulted ||
                            reReadTask.Result == null ||
                            !reReadTask.Result.Exists)
                        {
                            Debug.LogWarning("[OnlineMatchController] TryResolveRound re-read FAILED — aborting resolve" +
                                             " | matchId=" + matchId +
                                             " round=" + roundNumber);
                            return;
                        }

                        MatchData freshMatch =
                            JsonUtility.FromJson<MatchData>(
                                reReadTask.Result.GetRawJsonValue());

                        if (freshMatch == null)
                        {
                            Debug.LogWarning("[OnlineMatchController] TryResolveRound STOP: freshMatch null after re-read.");
                            return;
                        }

                        Debug.Log("[OnlineMatchController] TryResolveRound re-read MATCH" +
                                  " | matchId=" + freshMatch.matchId +
                                  " | currentRoundNumber=" + freshMatch.currentRoundNumber +
                                  " | roundResolutionStatus=" + freshMatch.roundResolutionStatus);

                        if (freshMatch.currentRoundNumber != roundNumber)
                        {
                            Debug.LogWarning("[OnlineMatchController] TryResolveRound STOP: round already advanced" +
                                             " | expected=" + roundNumber +
                                             " | actual=" + freshMatch.currentRoundNumber);
                            return;
                        }

                        Debug.Log("[OnlineMatchController] TryResolveRound calling ResolveRoundNow" +
                                  " | matchId=" + matchId +
                                  " | round=" + roundNumber +
                                  " | submissionsCount=" + submissions.Count);

                        ResolveRoundNow(liveMatch, roundNumber, submissions, matchRef);
                    });
                });

            });


        });
    }

    private void ResolveRoundNow(
     MatchData liveMatch,
     int roundNumber,
     List<RoundSubmissionData> submissions,
     DatabaseReference matchRef)
    {
        Debug.Log("[OnlineMatchController] ResolveRoundNow START | matchId=" +
                  liveMatch.matchId + " round=" + roundNumber +
                  " submissionsCount=" + (submissions != null ? submissions.Count : -1));

        if (submissions == null || submissions.Count == 0)
        {
            Debug.LogWarning("[OnlineMatchController] ResolveRoundNow aborted: no submissions.");
            // IMPORTANT: don't leave status stuck on 'resolving'
            matchRef.Child("roundResolutionStatus").SetValueAsync("idle");
            return;
        }

        RoundSubmissionData winner = null;

        foreach (var sub in submissions)
        {
            Debug.Log("[OnlineMatchController] ResolveRoundNow submission uid=" + sub.uid +
                      " score=" + sub.score + " isValid=" + sub.isValid);

            if (!sub.isValid)
                continue;

            if (winner == null || IsBetterSubmission(sub, winner))
                winner = sub;
        }

        RoundSubmissionData player1Submission = submissions.Find(
        sub => sub != null && sub.uid == liveMatch.player1Uid
        );

        RoundSubmissionData player2Submission = submissions.Find(
            sub => sub != null && sub.uid == liveMatch.player2Uid
        );

        Debug.Log("[OnlineMatchController] ResolveRoundNow winner uid=" +
                  (winner != null ? winner.uid : "NULL"));

        BoardStateData board = JsonUtility.FromJson<BoardStateData>(liveMatch.boardStateJson) ?? new BoardStateData();
        BagStateData bag = JsonUtility.FromJson<BagStateData>(liveMatch.bagStateJson) ?? new BagStateData();
        RackStateData sharedRack = JsonUtility.FromJson<RackStateData>(liveMatch.sharedrackjson) ?? new RackStateData();

        string preRoundBoardStateJson = liveMatch.boardStateJson;
        string roundBonusBoardJson = liveMatch.bonusBoardJson;

        Debug.Log("[OnlineMatchController] ResolveRoundNow parsed state | " +
                  "boardCells=" + (board.cells != null ? board.cells.Count : -1) +
                  " bagTiles=" + (bag.tiles != null ? bag.tiles.Count : -1) +
                  " sharedRackTiles=" + (sharedRack.tiles != null ? sharedRack.tiles.Count : -1));

        RoundResultData result = new RoundResultData
        {
            roundNumber = roundNumber,
            anyValidMove = winner != null
        };

        if (winner != null)
        {
            Debug.Log("[OnlineMatchController] ResolveRoundNow winner has simulatedTilesJson length=" +
                      (winner.simulatedTilesJson != null ? winner.simulatedTilesJson.Length : 0));

            SimTileListWrapper wrapper = JsonUtility.FromJson<SimTileListWrapper>(winner.simulatedTilesJson);

            result.winningTilesJson = winner.simulatedTilesJson;

            liveMatch.lastRoundResultJson = JsonUtility.ToJson(result);

            if (wrapper != null && wrapper.tiles != null)
            {
                Debug.Log("[OnlineMatchController] ResolveRoundNow placing " +
                          wrapper.tiles.Count + " tiles on board.");

                foreach (var tile in wrapper.tiles)
                {
                    int idx = sharedRack.tiles.FindIndex(t => t.letter == tile.letter);
                    if (idx >= 0)
                        sharedRack.tiles.RemoveAt(idx);
                    else
                        Debug.LogWarning("[OnlineMatchController] ResolveRoundNow tile " + tile.letter +
                                         " not found in sharedRack; skipping remove.");

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
                    else
                    {
                        Debug.LogWarning("[OnlineMatchController] ResolveRoundNow could not find board cell at x=" +
                                         x + " y=" + y + " for tile " + tile.letter);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[OnlineMatchController] ResolveRoundNow wrapper/tiles NULL for winner; no placement.");
            }

            result.winnerUid = winner.uid;
            result.winnerWord = winner.word;
            result.winnerScore = winner.score;

            bool winnerIsPlayer1 = winner.uid == liveMatch.player1Uid;
            result.winnerDisplayName = winnerIsPlayer1
                ? liveMatch.player1DisplayName
                : liveMatch.player2DisplayName;

            Debug.Log("[OnlineMatchController] ResolveRoundNow winnerDisplayName=" +
                      result.winnerDisplayName + " winnerScore=" + result.winnerScore);

            if (winnerIsPlayer1)
                liveMatch.player1Score += winner.score;
            else
                liveMatch.player2Score += winner.score;

            Debug.Log("[OnlineMatchController] ResolveRoundNow updated scores | p1=" +
                      liveMatch.player1Score + " p2=" + liveMatch.player2Score);
        }
        else
        {
            Debug.Log("[OnlineMatchController] ResolveRoundNow no valid winner; skipping board/rack updates.");
        }
        //here
        if (liveMatch.roundScores == null)
            liveMatch.roundScores = new List<RoundScoreLine>();

        bool roundAlreadyRecorded = liveMatch.roundScores.Exists(
            r => r.roundNumber == roundNumber
        );

        if (!roundAlreadyRecorded)
        {
            bool winnerIsPlayer1 = winner != null && winner.uid == liveMatch.player1Uid;

            liveMatch.roundScores.Add(new RoundScoreLine
            {
                roundNumber = roundNumber,
                player1Score = winnerIsPlayer1 ? winner.score : 0,
                player2Score = winner != null && !winnerIsPlayer1 ? winner.score : 0
            });

            Debug.Log(
                "[OnlineMatchController] Recorded round score | round=" + roundNumber +
                " p1=" + (winnerIsPlayer1 ? winner.score : 0) +
                " p2=" + (winner != null && !winnerIsPlayer1 ? winner.score : 0)
            );
        }

        if (liveMatch.roundHistory == null)
            liveMatch.roundHistory = new List<OnlineRoundHistoryEntry>();

        bool roundHistoryAlreadyRecorded = liveMatch.roundHistory.Exists(
            entry => entry != null && entry.roundNumber == roundNumber
        );

        if (!roundHistoryAlreadyRecorded)
        {
            liveMatch.roundHistory.Add(new OnlineRoundHistoryEntry
            {
                roundNumber = roundNumber,

                preRoundBoardStateJson = preRoundBoardStateJson,
                roundBonusBoardJson = roundBonusBoardJson,

                player1Word = player1Submission != null
                    ? player1Submission.word
                    : "",

                player1Score = player1Submission != null
                    ? player1Submission.score
                    : 0,

                player1Valid = player1Submission != null &&
                               player1Submission.isValid,

                player1SimulatedTilesJson = player1Submission != null
                    ? player1Submission.simulatedTilesJson
                    : "",

                player2Word = player2Submission != null
                    ? player2Submission.word
                    : "",

                player2Score = player2Submission != null
                    ? player2Submission.score
                    : 0,

                player2Valid = player2Submission != null &&
                               player2Submission.isValid,

                player2SimulatedTilesJson = player2Submission != null
                    ? player2Submission.simulatedTilesJson
                    : "",

                winnerUid = result.winnerUid,
                winnerWord = result.winnerWord,
                winnerScore = result.winnerScore,
                anyValidMove = result.anyValidMove,
                winnerIsPlayer1 = winner != null &&
                   winner.uid == liveMatch.player1Uid

            });

            Debug.Log(
                "[OnlineMatchController] Saved round replay history | round=" +
                roundNumber +
                " p1='" + (player1Submission != null ? player1Submission.word : "") +
                "' p2='" + (player2Submission != null ? player2Submission.word : "") +
                "' winner='" + result.winnerWord + "'"
            );
        }

        if (sharedRack.tiles == null)
        {
            Debug.LogWarning("[OnlineMatchController] ResolveRoundNow sharedRack.tiles is NULL; creating list.");
            sharedRack.tiles = new List<TileData>();
        }

        if (bag.tiles == null)
        {
            Debug.LogWarning("[OnlineMatchController] ResolveRoundNow bag.tiles is NULL; creating list.");
            bag.tiles = new List<TileData>();
        }

        int beforeRefill = sharedRack.tiles.Count;

        while (sharedRack.tiles.Count < 7 && bag.tiles != null && bag.tiles.Count > 0)
        {
            sharedRack.tiles.Add(bag.tiles[0]);
            bag.tiles.RemoveAt(0);
        }

        Debug.Log("[OnlineMatchController] ResolveRoundNow refilled rack from " +
                  beforeRefill + " to " + sharedRack.tiles.Count +
                  " (bag now " + (bag.tiles != null ? bag.tiles.Count : -1) + " tiles)");

        int totalRounds = liveMatch.totalRounds > 0 ? liveMatch.totalRounds : 5;
        int nextRound = roundNumber + 1;
        bool isFinalRoundJustPlayed = nextRound > totalRounds;

        Debug.Log("[OnlineMatchController] ResolveRoundNow round progression | " +
                  "current=" + roundNumber + " next=" + nextRound +
                  " totalRounds=" + totalRounds +
                  " isFinal=" + isFinalRoundJustPlayed);

        // Persist logical state
        liveMatch.boardStateJson = JsonUtility.ToJson(board);
        liveMatch.bagStateJson = JsonUtility.ToJson(bag);
        liveMatch.sharedrackjson = JsonUtility.ToJson(sharedRack);
        liveMatch.lastRoundResultJson = JsonUtility.ToJson(result);
        liveMatch.currentRoundNumber = nextRound;
        liveMatch.roundResolutionStatus = isFinalRoundJustPlayed ? "done" : "idle";
        liveMatch.status = isFinalRoundJustPlayed ? "completed" : "active";

        // Regenerate bonus board for NEXT round, if match continues
        if (!isFinalRoundJustPlayed && Singleton.Instance != null && Singleton.Instance.GameLogic != null)
        {
            var gameLogic = Singleton.Instance.GameLogic;

            try
            {
                gameLogic.LoadBoardStateIntoValidatedTiles(board);
                string newBonusJson = gameLogic.GenerateBonusBoardJsonForOnlineMatch();
                liveMatch.bonusBoardJson = newBonusJson;

                Debug.Log("[ONLINE] Regenerated bonusBoardJson for next round, length=" +
                          (string.IsNullOrEmpty(newBonusJson) ? 0 : newBonusJson.Length));
            }
            catch (Exception ex)
            {
                Debug.LogError("[OnlineMatchController] ResolveRoundNow bonus regeneration threw: " + ex);
            }
        }
        else
        {
            Debug.Log("[OnlineMatchController] ResolveRoundNow skip bonus regen; match final or Singleton/GameLogic missing.");
        }

        Debug.Log("[OnlineMatchController] ResolveRoundNow ABOUT TO GUARD READ & WRITE | nextRound=" +
          nextRound + " status=" + liveMatch.status +
          " roundResolutionStatus=" + liveMatch.roundResolutionStatus);

        // Guard: re-check right before writing — abort if someone already advanced this round
        matchRef.GetValueAsync().ContinueWithOnMainThread(guardTask =>
        {
            if (guardTask.IsFaulted || guardTask.Result == null || !guardTask.Result.Exists)
            {
                Debug.LogWarning("[OnlineMatchController] Resolve guard read failed — aborting write.");
                return;
            }

            MatchData freshCheck =
                JsonUtility.FromJson<MatchData>(guardTask.Result.GetRawJsonValue());

            Debug.Log("[OnlineMatchController] Resolve guard freshCheck.currentRoundNumber=" +
                      (freshCheck != null
                          ? freshCheck.currentRoundNumber.ToString()
                          : "NULL"));

            if (freshCheck == null || freshCheck.currentRoundNumber != roundNumber)
            {
                Debug.LogWarning("[OnlineMatchController] Round " + roundNumber +
                    " resolution aborted at final write — match already advanced to " +
                    (freshCheck != null
                        ? freshCheck.currentRoundNumber.ToString()
                        : "NULL") + ".");
                return;
            }

            Debug.Log(
                "[OnlineMatchController] ResolveRoundNow guard passed; " +
                "updating resolved match fields.");

            var updates = new Dictionary<string, object>
            {
                { "boardStateJson", liveMatch.boardStateJson },
                { "bagStateJson", liveMatch.bagStateJson },
                { "sharedrackjson", liveMatch.sharedrackjson },
                { "bonusBoardJson", liveMatch.bonusBoardJson },
                { "lastRoundResultJson", liveMatch.lastRoundResultJson },
                { "currentRoundNumber", liveMatch.currentRoundNumber },
                { "roundResolutionStatus", liveMatch.roundResolutionStatus },
                { "status", liveMatch.status },
                { "player1Score", liveMatch.player1Score },
                { "player2Score", liveMatch.player2Score },
                { "roundScores", liveMatch.roundScores },
                { "roundHistory", liveMatch.roundHistory }
            };

           


            matchRef.UpdateChildrenAsync(updates)
                .ContinueWithOnMainThread(writeTask =>
                {
                    if (writeTask.IsFaulted)
                    {
                        Debug.LogError(
                            "[OnlineMatchController] Failed to update resolved round: " +
                            writeTask.Exception);
                        return;
                    }

                    Debug.Log(
                        "[OnlineMatchController] Round " + roundNumber +
                        " resolved; match fields updated and round history preserved.");

                    SaveRoundScores(matchRef, liveMatch.roundScores);
                    SaveRoundHistory(matchRef, liveMatch.roundHistory);
                });
        });
    }

    #endregion

    #region Helpers and UI flows

    private void SaveRoundScores(
    DatabaseReference matchRef,
    List<RoundScoreLine> roundScores)
    {
        if (matchRef == null || roundScores == null || roundScores.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] SaveRoundScores skipped: no data."
            );
            return;
        }

        Dictionary<string, object> updates =
            new Dictionary<string, object>();

        foreach (RoundScoreLine round in roundScores)
        {
            string prefix = "roundScores/" + round.roundNumber;

            updates[prefix + "/roundNumber"] = round.roundNumber;
            updates[prefix + "/player1Score"] = round.player1Score;
            updates[prefix + "/player2Score"] = round.player2Score;
        }

        matchRef.UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "[OnlineMatchController] Failed to save roundScores: " +
                        task.Exception
                    );
                    return;
                }

                Debug.Log(
                    "[OnlineMatchController] Saved roundScores: " +
                    roundScores.Count
                );
            });
    }

    /*private void SaveRoundHistory(
    DatabaseReference matchRef,
    List<OnlineRoundHistoryEntry> roundHistory)
    {
        if (matchRef == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] SaveRoundHistory skipped: matchRef is null."
            );
            return;
        }

        if (roundHistory == null ||
            roundHistory.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] SaveRoundHistory skipped: no history."
            );
            return;
        }

        matchRef.Child("roundHistory")
            .SetRawJsonValueAsync(JsonUtility.ToJson(
                new OnlineRoundHistoryWrapper
                {
                    entries = roundHistory
                }
            ))
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "[OnlineMatchController] Failed to save round history: " +
                        task.Exception
                    );
                    return;
                }

                Debug.Log(
                    "[OnlineMatchController] Saved " +
                    roundHistory.Count +
                    " round-history entries."
                );
            });
    }
    */
    /*private void SaveRoundHistory(
    DatabaseReference matchRef,
    List<OnlineRoundHistoryEntry> roundHistory)
    {
        if (matchRef == null ||
            roundHistory == null ||
            roundHistory.Count == 0)
        {
            return;
        }

        Dictionary<string, object> updates =
            new Dictionary<string, object>();

        foreach (OnlineRoundHistoryEntry entry in roundHistory)
        {
            if (entry == null)
                continue;

            string path =
                "roundHistory/" +
                entry.roundNumber;

            updates[path] = JsonUtility.ToJson(entry);
        }

        if (updates.Count == 0)
            return;

        matchRef.UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "[OnlineMatchController] Failed to save round history: " +
                        task.Exception
                    );
                    return;
                }

                Debug.Log(
                    "[OnlineMatchController] Saved " +
                    updates.Count +
                    " replay-history entries."
                );
            });
    }
    */
    private void SaveRoundHistory(
    DatabaseReference matchRef,
    List<OnlineRoundHistoryEntry> roundHistory)
    {
        if (matchRef == null || roundHistory == null || roundHistory.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] SaveRoundHistory skipped: no data."
            );
            return;
        }

        Dictionary<string, object> updates =
            new Dictionary<string, object>();

        foreach (OnlineRoundHistoryEntry entry in roundHistory)
        {
            if (entry == null || entry.roundNumber <= 0)
                continue;

            string prefix = "roundHistory/" + entry.roundNumber + "/";

            updates[prefix + "roundNumber"] = entry.roundNumber;

            updates[prefix + "preRoundBoardStateJson"] =
                entry.preRoundBoardStateJson ?? "";

            updates[prefix + "roundBonusBoardJson"] =
                entry.roundBonusBoardJson ?? "";

            updates[prefix + "player1Word"] =
                entry.player1Word ?? "";

            updates[prefix + "player1Score"] =
                entry.player1Score;

            updates[prefix + "player1Valid"] =
                entry.player1Valid;

            updates[prefix + "player1SimulatedTilesJson"] =
                entry.player1SimulatedTilesJson ?? "";

            updates[prefix + "player2Word"] =
                entry.player2Word ?? "";

            updates[prefix + "player2Score"] =
                entry.player2Score;

            updates[prefix + "player2Valid"] =
                entry.player2Valid;

            updates[prefix + "player2SimulatedTilesJson"] =
                entry.player2SimulatedTilesJson ?? "";

            updates[prefix + "winnerUid"] =
                entry.winnerUid ?? "";

            updates[prefix + "winnerWord"] =
                entry.winnerWord ?? "";

            updates[prefix + "winnerScore"] =
                entry.winnerScore;

            updates[prefix + "anyValidMove"] =
                entry.anyValidMove;
            updates[prefix + "winnerIsPlayer1"] =
    entry.winnerIsPlayer1;
        }

        if (updates.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] SaveRoundHistory skipped: no valid entries."
            );
            return;
        }

        matchRef.UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "[OnlineMatchController] Failed to save roundHistory: " +
                        task.Exception
                    );
                    return;
                }

                Debug.Log(
                    "[OnlineMatchController] Saved roundHistory entries: " +
                    roundHistory.Count
                );
            });
    }

    private IEnumerator ShowSubmittedWaitingSequence()
    {
        if (gameLogic != null)
            gameLogic.SetInputLocked(true);

        if (uiManager != null)
            uiManager.ShowRoundMessage("Move submitted!");

        yield return new WaitForSeconds(1.5f);

        if (uiManager != null)
            uiManager.ShowRoundMessage("Waiting for other players...");

        yield return new WaitForSeconds(1.5f);

        if (pendingResolutionMatchId == null)
            yield break; // already redirected to game-over panel

        // Switch back to MatchStatusPanel
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (matchStatusPanel != null)
        {
            matchStatusPanel.gameObject.SetActive(true);
            matchStatusPanel.OnRefreshPressed();
        }
    }

    private void CheckSubmissionThenEnterGameplay()
    {
        TraceMatch(
            "CheckSubmissionThenEnterGameplay ENTER"
            + " | pendingEnterGameplay=" + pendingEnterGameplay
            + " | currentMatchId=" + (currentMatch != null ? currentMatch.matchId : "NULL")
            + " | currentRound=" + (currentMatch != null
                ? currentMatch.currentRoundNumber.ToString()
                : "NULL")
        );

        if (currentMatch == null || auth == null || auth.CurrentUser == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot enter gameplay: match or signed-in user is unavailable."
            );
            return;
        }

        string uid = auth.CurrentUser.UserId;

        pendingEnterGameplay = false;

        if (gameplayPanel != null)
            gameplayPanel.SetActive(true);

        if (pregamePanel != null)
            pregamePanel.SetActive(false);

        if (matchStatusPanel != null)
            matchStatusPanel.gameObject.SetActive(false);

        TraceMatch(
            "CheckSubmissionThenEnterGameplay DIRECT START GAMEPLAY"
            + " | uid=" + uid
        );

        StartGameplayForCurrentMatch(uid);
    }

    public void StartGameplayForCurrentMatch(string uid)
    {
        Debug.Log("[OnlineMatchController] StartGameplayForCurrentMatch START | uid=" + uid);

        if (gameLogic == null || currentMatch == null || string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[OnlineMatchController] StartGameplayForCurrentMatch aborted: missing references.");
            return;
        }

        bool isPlayer1 = currentMatch.player1Uid == uid;

        List<LetterInfo> localRack = ParseRackJson(currentMatch.sharedrackjson);
        if (localRack == null)
            localRack = new List<LetterInfo>();

        int localScore = isPlayer1 ? currentMatch.player1Score : currentMatch.player2Score;
        int opponentScore = isPlayer1 ? currentMatch.player2Score : currentMatch.player1Score;

        try
        {
            gameLogic.BeginOnlineMatchFromRack(
                7, 15, 15,
                localRack, localScore, opponentScore,
                currentMatch.currentRoundNumber,
                currentMatch.bonusBoardJson,
                currentMatch.boardStateJson,
                currentMatch.lastRoundResultJson,
                currentMatch.totalRounds
            );
        }
        catch (Exception ex)
        {
            Debug.LogError("[OnlineMatchController] BeginOnlineMatchFromRack threw exception: " + ex);
        }
    }

    

    private void HandleResolvedRound(int resolvedRoundNumber)
    {
        if (currentMatch == null || string.IsNullOrEmpty(currentMatch.lastRoundResultJson))
            return;

        RoundResultData result = JsonUtility.FromJson<RoundResultData>(currentMatch.lastRoundResultJson);
        if (result == null)
            return;

        // If the player is in gameplay and watching this match, show popup
        bool isViewingThisMatch =
            Singleton.Instance != null &&
            Singleton.Instance.UIManager != null &&
            Singleton.Instance.UIManager.gameObject.activeInHierarchy &&
            watchedMatchId == currentMatch.matchId;

        if (isViewingThisMatch && gameLogic != null)
        {
            gameLogic.StartCoroutine(ShowOnlineRoundResultDelayed(result));
        }
        // else: let the next rack/board load normally.
    }

    private IEnumerator ShowOnlineRoundResultDelayed(RoundResultData result)
    {
        string uid = GetCurrentUser()?.UserId ?? "";

        // Fetch local submission to find anchor for popup
        DatabaseReference submissionRef =
            dbRoot.Child("matches").Child(currentMatch.matchId)
                  .Child("rounds").Child(result.roundNumber.ToString())
                  .Child("submissions").Child(uid);

        var task = submissionRef.GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (!task.IsFaulted && task.Result != null && task.Result.Exists)
        {
            string json = task.Result.GetRawJsonValue();
            RoundSubmissionData localSubmission = JsonUtility.FromJson<RoundSubmissionData>(json);

            if (localSubmission != null &&
                localSubmission.isValid &&
                !string.IsNullOrEmpty(localSubmission.simulatedTilesJson))
            {
                SimTileListWrapper wrapper = JsonUtility.FromJson<SimTileListWrapper>(localSubmission.simulatedTilesJson);
                if (wrapper != null && wrapper.tiles != null && wrapper.tiles.Count > 0)
                {
                    // Find best tile for popup anchor (same as your original code)
                    SimPlacedTileData bestTile = wrapper.tiles[0];

                    foreach (SimPlacedTileData tile in wrapper.tiles)
                    {
                        if (tile.row < bestTile.row ||
                            (tile.row == bestTile.row && tile.col < bestTile.col))
                        {
                            bestTile = tile;
                        }
                    }

                    LetterPosition anchor = new LetterPosition(bestTile.row, bestTile.col);

                    if (uiManager != null)
                    {
                        uiManager.ShowValidatedWordScore(anchor, localSubmission.score, false);
                    }

                    // Give time to see their score first
                    yield return new WaitForSeconds(1.5f);
                }
            }
        }

        // Then show the round result message
        if (uiManager != null)
        {
            if (result.anyValidMove)
            {
                uiManager.ShowRoundMessage($"{result.winnerDisplayName} wins with {result.winnerWord} ({result.winnerScore} pts)");
            }
            else
            {
                uiManager.ShowRoundMessage("No valid move this round.");
            }
        }
    }

    #endregion

    //#region Small helpers taken from your existing code

    private void TraceMatch(string label)
    {
        matchTraceSeq++;
        string currentMatchId = currentMatch != null ? currentMatch.matchId : "NULL";
        string currentStatus = currentMatch != null ? currentMatch.status : "NULL";
        string currentTurn = currentMatch != null ? currentMatch.currentRoundNumber.ToString() : "NULL";

        Debug.Log("[MATCHTRACE #" + matchTraceSeq + "] " + label +
                  " | watchedMatchId=" + watchedMatchId +
                  " | currentMatchId=" + currentMatchId +
                  " | currentStatus=" + currentStatus +
                  " | currentTurn=" + currentTurn);
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
                if (tile == null)
                    continue;

                result.Add(new LetterInfo(tile.letter, tile.value));
            }
        }

        return result;
    }

    private BoardCellData FindCell(BoardStateData board, int x, int y)
    {
        if (board == null || board.cells == null)
            return null;

        foreach (var cell in board.cells)
        {
            if (cell.x == x && cell.y == y)
                return cell;
        }

        return null;
    }

    private bool IsBetterSubmission(RoundSubmissionData candidate, RoundSubmissionData currentBest)
    {
        if (candidate.score != currentBest.score)
            return candidate.score > currentBest.score;

        int candLen = string.IsNullOrEmpty(candidate.word) ? 0 : candidate.word.Length;
        int bestLen = string.IsNullOrEmpty(currentBest.word) ? 0 : currentBest.word.Length;

        if (candLen != bestLen)
            return candLen > bestLen;

        // earlier submission wins ties
        return candidate.submittedAtUnix < currentBest.submittedAtUnix;
    }


    /*public void StartWatchingMatch(string matchId)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            Debug.LogWarning("[OnlineMatchController] StartWatchingMatch called with empty matchId");
            return;
        }

        // If already watching this match, do nothing
        if (!string.IsNullOrEmpty(currentMatchId) && currentMatchId == matchId && currentMatchRef != null)
        {
            Debug.Log("[OnlineMatchController] Already watching match " + matchId);
            return;
        }

        StopWatchingCurrentMatch();

        currentMatchId = matchId;
        currentMatchRef = dbRoot.Child("matches").Child(matchId);

        Debug.Log("[OnlineMatchController] Now watching match " + matchId);
        currentMatchRef.ValueChanged += OnMatchValueChanged;
    }
    */
    public void StopWatchingCurrentMatch()
    {
        if (currentMatchRef != null)
        {
            currentMatchRef.ValueChanged -= OnMatchValueChanged;
            currentMatchRef = null;
        }

        if (currentSubmissionsRef != null)
        {
            currentSubmissionsRef.ValueChanged -= OnSubmissionsValueChanged;
            currentSubmissionsRef = null;
        }

        currentMatch = null;
        watchedMatchId = null;   // was: currentMatchId
        watchedRoundNumber = -1;
        lastProcessedRound = 0;
        pendingEnterGameplay = false;
    }

    /*private void OnMatchValueChanged(object sender, ValueChangedEventArgs args)
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

        Debug.Log("[MATCHTRACE CALLBACK] OnMatchValueChanged ENTER | dbError=" +
                  (args.DatabaseError != null ? args.DatabaseError.Message : "null") +
                  " | snapshotExists=" + (args.Snapshot != null && args.Snapshot.Exists) +
                  " | rawLen=" + rawLen +
                  " | watchedMatchId=" + watchedMatchId);

        if (args.DatabaseError != null)
        {
            Debug.LogError("[OnlineMatchController] Match listener error: " + args.DatabaseError.Message);
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
        Debug.Log("[MATCHTRACE CALLBACK] parsed match id = " + (match == null ? "NULL" : match.matchId));

        if (match == null)
        {
            TraceMatch("OnMatchValueChanged PARSE FAILED");
            return;
        }

        currentMatch = match;
        TraceMatch("OnMatchValueChanged AFTER currentMatch ASSIGN");

        // Notify any UI listeners
        OnMatchUpdated?.Invoke(currentMatch);

        // Handle resolved rounds we haven't processed yet
        if (currentMatch.currentRoundNumber > lastProcessedRound + 1)
        {
            int resolvedRound = currentMatch.currentRoundNumber - 1;
            HandleResolvedRound(resolvedRound);
            lastProcessedRound = resolvedRound;
        }
        */
    // (Re)watch private OnlineRoundHistoryEntry ParseOnlineRoundHistoryEntry(
    /*        DataSnapshot roundSnapshot)
    {
                if (roundSnapshot == null ||
                    !roundSnapshot.Exists)
                {
                    return null;
                }

                OnlineRoundHistoryEntry entry =
                    new OnlineRoundHistoryEntry();

                int roundNumber;

                if (!int.TryParse(
                    roundSnapshot.Key,
                    out roundNumber))
                {
                    return null;
                }

                entry.roundNumber = roundNumber;

                DataSnapshot resultSnapshot =
                    roundSnapshot.Child("result");

                if (resultSnapshot.Exists)
                {
                    string rawResult =
                        resultSnapshot.GetRawJsonValue();

                    if (!string.IsNullOrEmpty(rawResult))
                    {
                        RoundResultData result =
                            JsonUtility.FromJson<RoundResultData>(rawResult);

                        if (result != null)
                        {
                            entry.player1Word = result.winnerIsPlayer1
                                ? result.winnerWord
                                : "";

                            entry.player2Word = result.winnerIsPlayer1
                                ? ""
                                : result.winnerWord;

                            entry.player1Score = result.winnerIsPlayer1
                                ? result.winnerScore
                                : 0;

                            entry.player2Score = result.winnerIsPlayer1
                                ? 0
                                : result.winnerScore;

                            entry.winnerIsPlayer1 =
                                result.winnerIsPlayer1;

                            entry.anyValidMove =
                                result.anyValidMove;
                        }
                    }
                }

                return entry;
            }
    */
    /*for the current round (if you still use this pattern)
    if (currentMatch != null && watchedRoundNumber != currentMatch.currentRoundNumber)
    {
        WatchSubmissionsForRound(currentMatch.currentRoundNumber);
    }

    // If a panel requested entry, now is the time
    if (pendingEnterGameplay)
    {
        TraceMatch("OnMatchValueChanged TRIGGER CheckSubmissionThenEnterGameplay"
            + " | pendingEnterGameplay=" + pendingEnterGameplay);

        CheckSubmissionThenEnterGameplay();

        TraceMatch("OnMatchValueChanged RETURNED from CheckSubmissionThenEnterGameplay");
    }
}
    */

    private void OnMatchValueChanged(
object sender,
ValueChangedEventArgs args)
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
            rawLen = string.IsNullOrEmpty(raw)
                ? 0
                : raw.Length;
        }

        Debug.Log(
            "[MATCHTRACE CALLBACK] OnMatchValueChanged ENTER" +
            " | dbError=" +
            (args.DatabaseError != null
                ? args.DatabaseError.Message
                : "null") +
            " | snapshotExists=" +
            (args.Snapshot != null &&
             args.Snapshot.Exists) +
            " | rawLen=" + rawLen +
            " | watchedMatchId=" + watchedMatchId
        );

        if (args.DatabaseError != null)
        {
            Debug.LogError(
                "[OnlineMatchController] Match listener error: " +
                args.DatabaseError.Message
            );
            return;
        }

        if (args.Snapshot == null ||
            !args.Snapshot.Exists)
        {
            TraceMatch("OnMatchValueChanged SNAPSHOT MISSING");
            return;
        }

        if (string.IsNullOrEmpty(raw))
        {
            TraceMatch("OnMatchValueChanged RAW JSON EMPTY");
            return;
        }

        MatchData match =
            JsonUtility.FromJson<MatchData>(raw);

        Debug.Log(
            "[MATCHTRACE CALLBACK] parsed match id = " +
            (match == null ? "NULL" : match.matchId)
        );

        if (match == null)
        {
            TraceMatch("OnMatchValueChanged PARSE FAILED");
            return;
        }

        currentMatch = match;

        TraceMatch(
            "OnMatchValueChanged AFTER currentMatch ASSIGN"
        );

        // Notify UI that the watched match snapshot changed.
        OnMatchUpdated?.Invoke(currentMatch);

        // Process a round when the remote match has advanced to the next round.
        if (currentMatch.currentRoundNumber >
            lastProcessedRound + 1)
        {
            int resolvedRound =
                currentMatch.currentRoundNumber - 1;

            HandleResolvedRound(resolvedRound);

            lastProcessedRound = resolvedRound;
        }

        // Listen for submissions for the currently active round.
        if (watchedRoundNumber !=
            currentMatch.currentRoundNumber)
        {
            WatchSubmissionsForRound(
                currentMatch.currentRoundNumber
            );
        }

        // A UI action such as Resume asked us to enter gameplay once
        // the match snapshot became available.
        if (pendingEnterGameplay)
        {
            TraceMatch(
                "OnMatchValueChanged TRIGGER " +
                "CheckSubmissionThenEnterGameplay" +
                " | pendingEnterGameplay=" +
                pendingEnterGameplay
            );

            CheckSubmissionThenEnterGameplay();

            TraceMatch(
                "OnMatchValueChanged RETURNED from " +
                "CheckSubmissionThenEnterGameplay"
            );
        }
    }
    private void ShowOnlineRoundReplayRows(
        MatchData match,
        bool amPlayer1,
        string opponentName)
    {
        if (match == null)
            return;

        if (match.roundHistory == null ||
            match.roundHistory.Count == 0)
        {
            Debug.LogWarning(
                "[OnlineMatchController] No round history found for replay."
            );

            if (uiManager != null)
            {
                uiManager.ClearOnlineRoundReplayRows();
            }

            return;
        }

        List<OnlineRoundHistoryEntry> history =
            new List<OnlineRoundHistoryEntry>(
                match.roundHistory
            );

        history.Sort((a, b) =>
            a.roundNumber.CompareTo(b.roundNumber)
        );

        uiManager.ShowOnlineRoundReplayRows(
            history,
            amPlayer1,
            opponentName,
            ReplayRoundFromGameOver
        );
    }

    private void ReplayRoundFromGameOver(
    OnlineRoundHistoryEntry entry)
    {
        if (entry == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Replay requested with null history entry."
            );
            return;
        }

        if (gameLogic == null &&
            Singleton.Instance != null)
        {
            gameLogic = Singleton.Instance.GameLogic;
        }

        if (gameLogic == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot replay: GameLogic is missing."
            );
            return;
        }

        if (currentMatch == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot replay: current match is missing."
            );
            return;
        }

        string localUid = GetCurrentUser() != null
            ? GetCurrentUser().UserId
            : "";

        bool amPlayer1 =
            currentMatch.player1Uid == localUid;

        int localScore = 0;
        int opponentScore = 0;

        if (currentMatch.roundScores != null)
        {
            foreach (RoundScoreLine score in currentMatch.roundScores)
            {
                if (score == null ||
                    score.roundNumber >= entry.roundNumber)
                {
                    continue;
                }

                localScore += amPlayer1
                    ? score.player1Score
                    : score.player2Score;

                opponentScore += amPlayer1
                    ? score.player2Score
                    : score.player1Score;
            }
        }

        if (gameplayPanel != null)
            gameplayPanel.SetActive(true);

        if (uiManager != null &&
            uiManager.gameOverPanel != null)
        {
            uiManager.gameOverPanel.SetActive(false);
        }

        gameLogic.LoadOnlineRoundReplay(
            15,
            15,
            entry.roundNumber,
            currentMatch.totalRounds,
            localScore,
            opponentScore,
            entry.preRoundBoardStateJson,
            entry.roundBonusBoardJson
        );

        uiManager.ShowRoundMessage(
            "Replay — Round " +
            entry.roundNumber +
            ": " +
            (entry.anyValidMove
                ? entry.winnerWord + " (" +
                  entry.winnerScore + " points)"
                : "No valid move")
        );
    }
    /*private void ShowOnlineRoundReplayRows(
    MatchData match,
    bool amPlayer1,
    string opponentName)
    {
        if (match == null)
        {
            Debug.LogWarning(
                "[OnlineMatchController] Cannot show replay: match is null."
            );
            return;
        }

        List<OnlineRoundHistoryEntry> history =
            new List<OnlineRoundHistoryEntry>();

        if (match.roundScores != null)
        {
            foreach (RoundScoreLine scoreLine in match.roundScores)
            {
                if (scoreLine == null)
                    continue;

                OnlineRoundHistoryEntry entry =
                    new OnlineRoundHistoryEntry();

                entry.roundNumber =
                    scoreLine.roundNumber;

                entry.player1Score =
                    scoreLine.player1Score;

                entry.player2Score =
                    scoreLine.player2Score;

                entry.player1Word =
                    scoreLine.player1Score > 0
                        ? "(word unavailable)"
                        : "";

                entry.player2Word =
                    scoreLine.player2Score > 0
                        ? "(word unavailable)"
                        : "";

                entry.anyValidMove =
                    scoreLine.player1Score > 0 ||
                    scoreLine.player2Score > 0;

                entry.winnerIsPlayer1 =
                    scoreLine.player1Score >
                    scoreLine.player2Score;

                history.Add(entry);
            }
        }

        history.Sort((a, b) =>
            a.roundNumber.CompareTo(b.roundNumber)
        );

        if (uiManager != null)
        {
            uiManager.ShowOnlineRoundReplayRows(
                history,
                amPlayer1,
                opponentName,
                null
            );
        }
    }
    */

    #endregion
}
