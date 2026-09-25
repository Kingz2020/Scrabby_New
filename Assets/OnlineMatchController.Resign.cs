using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

// Giving up, and clearing up.
//
// Two things a player could not do until now: end a game they no longer want
// to play, and take a finished one off their list. Both are the player saying
// they are done with a match, so both live here, and both ask first.
//
// A resignation is written on the match itself rather than kept on the phone:
// the other player has to be told, and the server's bot referee has to stop
// playing against nobody. Removing a finished game is the opposite - it is
// written only on this player's own list, so the other player keeps their
// copy of a game that was really played.
public partial class OnlineMatchController
{
    private const string ResignedField = "resignedByUid";

    // Whether there is a game on screen that this player could resign from.
    //
    // Not while a result is being looked at, and not on a game that is over:
    // the button would be offering to give up something already decided.
    //
    // It asks the match and the panels, and nothing else. An earlier version
    // also checked the back-to-match button, meaning to keep the key off the
    // screen during a replay - but that reference is the one on the result
    // card, which the scene starts with switched on, so the check was true
    // before a game had even begun and the key never appeared at all.
    public bool CanResignNow()
    {
        return WhyNotResign() == null;
    }

    // The same question, with the answer said out loud. Kept beside it rather
    // than folded into it: a button that does not appear tells you nothing
    // about why, and that cost a build to find out once already.
    public string WhyNotResign()
    {
        if (currentMatch == null || string.IsNullOrEmpty(currentMatch.matchId))
            return "no match";

        if (viewingOnlineMatchResult)
            return "looking at a result";

        if (gameOverPanel != null && gameOverPanel.activeInHierarchy)
            return "the result card is up";

        if (currentMatch.status == "completed")
            return "the match is over";

        if (currentMatch.totalRounds > 0 &&
            currentMatch.currentRoundNumber > currentMatch.totalRounds)
        {
            return "the rounds are used up";
        }

        Firebase.Auth.FirebaseUser me = GetCurrentUser();

        if (me == null)
            return "nobody is signed in";

        if (currentMatch.player1Uid != me.UserId &&
            currentMatch.player2Uid != me.UserId)
        {
            return "not this player's match";
        }

        return null;
    }

    // What the other player is called, for the question and for the result.
    private string OpponentLabel(MatchData match)
    {
        if (match == null)
            return "Your opponent";

        Firebase.Auth.FirebaseUser me = GetCurrentUser();
        bool amPlayer1 = me != null && match.player1Uid == me.UserId;

        string name = BotOpponent.Label(
            amPlayer1 ? match.player2Uid : match.player1Uid,
            amPlayer1 ? match.player2DisplayName : match.player1DisplayName);

        return string.IsNullOrWhiteSpace(name) ? "Your opponent" : name.Trim();
    }

    public void AskToResign()
    {
        if (!CanResignNow())
            return;

        string opponent = OpponentLabel(currentMatch);

        ConfirmCard.Ask(
            "Resign this game?",
            opponent + " wins it, and it moves to your finished games.\n" +
            "This cannot be undone.",
            "Resign",
            ResignNow);
    }

    private void ResignNow()
    {
        // Asked again on the way through: the question was on screen for a
        // few seconds, and a round can have ended in them.
        if (!CanResignNow() || !EnsureFirebaseReady())
            return;

        string matchId = currentMatch.matchId;
        string uid = GetCurrentUser().UserId;
        string opponent = OpponentLabel(currentMatch);

        Dictionary<string, object> changes = new Dictionary<string, object>
        {
            { "status", "completed" },
            { ResignedField, uid },
            { "resignedAtUnix", ServerValue.Timestamp }
        };

        dbRoot.Child("matches").Child(matchId)
              .UpdateChildrenAsync(changes)
              .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[RESIGN] Could not resign " + matchId + ": " +
                                 task.Exception);

                if (matchStatusPanel != null)
                    matchStatusPanel.ShowStatus("Could not resign - try again.");

                return;
            }

            Debug.Log("[RESIGN] " + uid + " resigned " + matchId + ".");

            if (matchStatusPanel != null)
            {
                matchStatusPanel.ShowStatus("You resigned. " + opponent + " wins.");
                matchStatusPanel.ForceRefresh();
            }
        });

        // The board goes now rather than when the write comes back: the game
        // is given up the moment it is asked for, and the watcher would
        // otherwise answer its own write by putting the result up over a
        // player who has already left for the list.
        LeaveTheBoardForTheList();
    }

    private void LeaveTheBoardForTheList()
    {
        StopWatchingCurrentMatch();
        LeftMatchViews();

        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.AbandonGameInProgress();

        if (gameplayPanel != null)
            gameplayPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (backToMatchButton != null)
            backToMatchButton.SetActive(false);

        if (matchStatusPanel != null)
            matchStatusPanel.gameObject.SetActive(true);
    }

    // ---------------------------------------------------- finished games --

    // Whether the result on screen is one this player can take off their list.
    public bool CanRemoveCurrentMatch()
    {
        if (!viewingOnlineMatchResult || currentMatch == null)
            return false;

        return !string.IsNullOrEmpty(currentMatch.matchId) && GetCurrentUser() != null;
    }

    public void AskToRemoveCurrentMatch()
    {
        if (!CanRemoveCurrentMatch())
            return;

        string opponent = OpponentLabel(currentMatch);

        ConfirmCard.Ask(
            "Remove this game?",
            "It comes off your list for good. " + opponent +
            " keeps their copy, and your record stays as it is.",
            "Remove",
            RemoveCurrentMatchNow);
    }

    private void RemoveCurrentMatchNow()
    {
        if (!CanRemoveCurrentMatch() || !EnsureFirebaseReady())
            return;

        string matchId = currentMatch.matchId;
        string uid = GetCurrentUser().UserId;

        // The list is the player's own, so only their copy of it is touched -
        // and a transaction rather than a read and a write, because a game
        // ending elsewhere writes to the same list.
        dbRoot.Child("users").Child(uid).Child("activeMatchIds")
              .RunTransaction(data =>
        {
            List<object> kept = new List<object>();

            List<object> asList = data.Value as List<object>;

            if (asList != null)
            {
                foreach (object entry in asList)
                {
                    if (entry != null && entry.ToString() != matchId)
                        kept.Add(entry);
                }
            }
            else
            {
                // A list with a gap in it comes back keyed by index.
                Dictionary<string, object> asMap = data.Value as Dictionary<string, object>;

                if (asMap != null)
                {
                    foreach (KeyValuePair<string, object> pair in asMap)
                    {
                        if (pair.Value != null && pair.Value.ToString() != matchId)
                            kept.Add(pair.Value);
                    }
                }
            }

            // An empty list is no list: Realtime Database keeps nothing.
            data.Value = kept.Count > 0 ? kept : null;

            return TransactionResult.Success(data);
        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[REMOVE] Could not remove " + matchId + ": " +
                                 task.Exception);

                if (matchStatusPanel != null)
                    matchStatusPanel.ShowStatus("Could not remove that game - try again.");

                return;
            }

            Debug.Log("[REMOVE] " + matchId + " is off this player's list.");

            if (matchStatusPanel != null)
            {
                matchStatusPanel.ShowStatus("Game removed.");
                matchStatusPanel.ForceRefresh();
            }
        });

        viewingOnlineMatchResult = false;
        LeaveTheBoardForTheList();
    }
}
