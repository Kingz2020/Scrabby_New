using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Database;
using Firebase.Extensions;

public class MatchController : MonoBehaviour
{
    [SerializeField] private GameLogic gameLogic;

    private DatabaseReference dbRoot;
    private DatabaseReference matchRef;
    private DatabaseReference submissionsRef;

    private string matchId;
    private string localUid;
    private MatchData currentMatch;

    public void StartWatchingMatch(string matchId, string localUid)
    {
        this.matchId = matchId;
        this.localUid = localUid;
        dbRoot = FirebaseInit.Database.RootReference;

        matchRef = dbRoot.Child("matches").Child(matchId);
        matchRef.ValueChanged += OnMatchValueChanged;
    }

    private void OnDestroy()
    {
        if (matchRef != null)
            matchRef.ValueChanged -= OnMatchValueChanged;

        if (submissionsRef != null)
            submissionsRef.ValueChanged -= OnSubmissionsChanged;
    }

    private void OnMatchValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists)
            return;

        string json = args.Snapshot.GetRawJsonValue();
        MatchData match = JsonUtility.FromJson<MatchData>(json);
        if (match == null)
            return;

        bool isFirstLoad = currentMatch == null;
        bool roundAdvanced = !isFirstLoad && match.turnNumber != currentMatch.turnNumber;

        currentMatch = match;

        if (isFirstLoad)
        {
            gameLogic.StartOnlineMatch(match, localUid);
        }
        else if (roundAdvanced)
        {
            gameLogic.ApplyMatchUpdate(match);
        }

        WatchSubmissionsForRound(match.turnNumber);
    }

    private void WatchSubmissionsForRound(int roundNumber)
    {
        if (submissionsRef != null)
            submissionsRef.ValueChanged -= OnSubmissionsChanged;

        submissionsRef = dbRoot.Child("matches")
            .Child(matchId)
            .Child("rounds")
            .Child(roundNumber.ToString())
            .Child("submissions");

        submissionsRef.ValueChanged += OnSubmissionsChanged;
    }

    public void SubmitCurrentMove()
    {
        if (currentMatch == null)
            return;

        RoundMove move = gameLogic.EvaluateLocalSubmissionForOnline();

        SubmissionData submission = new SubmissionData
        {
            uid = localUid,
            word = move.word,
            score = move.score,
            isValid = move.isValid,
            submittedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        string json = JsonUtility.ToJson(submission);

        dbRoot.Child("matches")
            .Child(matchId)
            .Child("rounds")
            .Child(currentMatch.turnNumber.ToString())
            .Child("submissions")
            .Child(localUid)
            .SetRawJsonValueAsync(json);
    }

    private void OnSubmissionsChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.Snapshot == null || currentMatch == null)
            return;

        int submittedCount = (int)args.Snapshot.ChildrenCount;
        int expectedCount = GetExpectedPlayerCount(currentMatch);

        if (expectedCount > 0 && submittedCount >= expectedCount)
        {
            TryResolveRound(currentMatch.turnNumber);
        }
    }

    private int GetExpectedPlayerCount(MatchData match)
    {
        int count = 0;

        if (!string.IsNullOrEmpty(match.player1Uid))
            count++;

        if (!string.IsNullOrEmpty(match.player2Uid))
            count++;

        return count;
    }

    private void TryResolveRound(int roundNumber)
    {
        dbRoot.Child("matches")
            .Child(matchId)
            .Child("rounds")
            .Child(roundNumber.ToString())
            .Child("submissions")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists)
                    return;

                List<SubmissionData> allSubmissions = new List<SubmissionData>();
                foreach (var child in task.Result.Children)
                {
                    string rawJson = child.GetRawJsonValue();
                    if (string.IsNullOrEmpty(rawJson))
                        continue;

                    SubmissionData sub = JsonUtility.FromJson<SubmissionData>(rawJson);
                    if (sub != null)
                        allSubmissions.Add(sub);
                }

                SubmissionData winner = PickBestSubmission(allSubmissions);
                if (winner == null)
                    return;

                matchRef.RunTransaction(mutableData =>
                {
                    var matchDict = mutableData.Value as Dictionary<string, object>;
                    if (matchDict == null)
                        return TransactionResult.Success(mutableData);

                    if (!matchDict.ContainsKey("turnNumber"))
                        return TransactionResult.Success(mutableData);

                    int liveRoundNumber = Convert.ToInt32(matchDict["turnNumber"]);
                    if (liveRoundNumber != roundNumber)
                        return TransactionResult.Success(mutableData);

                    int player1Score = matchDict.ContainsKey("player1Score")
                        ? Convert.ToInt32(matchDict["player1Score"])
                        : 0;

                    int player2Score = matchDict.ContainsKey("player2Score")
                        ? Convert.ToInt32(matchDict["player2Score"])
                        : 0;

                    string player1Uid = matchDict.ContainsKey("player1Uid")
                        ? matchDict["player1Uid"]?.ToString()
                        : string.Empty;

                    string player2Uid = matchDict.ContainsKey("player2Uid")
                        ? matchDict["player2Uid"]?.ToString()
                        : string.Empty;

                    if (winner.uid == player1Uid)
                    {
                        matchDict["player1Score"] = player1Score + winner.score;
                    }
                    else if (winner.uid == player2Uid)
                    {
                        matchDict["player2Score"] = player2Score + winner.score;
                    }

                    matchDict["turnNumber"] = liveRoundNumber + 1;

                    if (!string.IsNullOrEmpty(player1Uid) && !string.IsNullOrEmpty(player2Uid))
                    {
                        matchDict["currentTurnUid"] =
                            (winner.uid == player1Uid) ? player2Uid : player1Uid;
                    }

                    mutableData.Value = matchDict;
                    return TransactionResult.Success(mutableData);
                })
                .ContinueWithOnMainThread(txTask =>
                {
                    if (txTask.IsFaulted || txTask.IsCanceled)
                    {
                        Debug.LogWarning("[MatchController] Round resolution transaction failed.");
                        return;
                    }

                    Debug.Log("[MatchController] Round " + roundNumber + " resolved.");
                });
            });
    }

    private SubmissionData PickBestSubmission(List<SubmissionData> submissions)
    {
        SubmissionData best = null;

        foreach (var sub in submissions)
        {
            if (sub == null || !sub.isValid)
                continue;

            if (best == null || sub.score > best.score)
                best = sub;
        }

        return best;
    }
}