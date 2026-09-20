using System;
using System.Collections;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

// The bot's half of a quick game.
//
// Nothing here is special-cased downstream: the bot's move is written into
// the same submissions node a person's would be, with the same fields, and
// the round resolves the way every other round resolves. The only difference
// is who wrote it and that it is worked out on this device.
//
// That last part is what makes this cheap and what limits it: the bot only
// moves while the player has the game open. In a quick game they are sitting
// there waiting, so it never shows. If they close the app mid-match, the
// reply is there when they come back - which is exactly how a human opponent
// who answered while they were away would look.
public partial class OnlineMatchController
{
    // Waiting out the offer, and sitting down if nobody comes.
    //
    // This lives here rather than on the match list panel, which is where it
    // started and where it never worked: entering the game hides that panel,
    // and a hidden GameObject stops its coroutines dead. The countdown was
    // being killed about a second after it started, every time. This object
    // is the manager and stays awake for the life of the app.
    private readonly System.Collections.Generic.HashSet<string> seatsBeingWatched =
        new System.Collections.Generic.HashSet<string>();

    public void LetABotTakeTheSeatIfNobodyComes(string matchId)
    {
        LetABotTakeTheSeatIfNobodyComes(matchId, BotOpponent.SecondsBeforeTakingTheSeat);
    }

    // A game just offered gets the full wait, so two people looking at once
    // still find each other. A game the player is coming back to has already
    // done its waiting - possibly for hours - so the seat is taken almost at
    // once.
    public void LetABotTakeTheSeatIfNobodyComes(string matchId, float seconds)
    {
        if (string.IsNullOrEmpty(matchId))
            return;

        // The database reference is not there in Awake when Firebase is still
        // starting up - the controller heals itself on the first call that
        // needs it, and every other method in this class asks first. These
        // did not, so they returned without a word and the bot simply never
        // existed.
        if (!EnsureFirebaseReady())
        {
            Debug.LogWarning("[BOT] Firebase is not ready; cannot watch the seat in " +
                             matchId + ".");
            return;
        }

        // Pressing Quick game twice should not start two countdowns on the
        // same seat.
        if (!seatsBeingWatched.Add(matchId))
        {
            Debug.Log("[BOT] Already watching the seat in " + matchId + ".");
            return;
        }

        Debug.Log("[BOT] Watching the seat in " + matchId + " for " + seconds + "s.");

        StartCoroutine(BotTakesTheSeat(matchId, seconds));
    }

    private IEnumerator BotTakesTheSeat(string matchId, float seconds)
    {
        float waited = 0f;

        while (waited < seconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!EnsureFirebaseReady() || GetCurrentUser() == null)
        {
            Debug.LogWarning("[BOT] Firebase went away while waiting for the seat in " +
                             matchId + ".");
            yield break;
        }

        string myUid = GetCurrentUser().UserId;
        bool seatTaken = false;

        BotOpponent.Player who = BotOpponent.PickSomebody();

        var claim = dbRoot.Child("matches").Child(matchId).Child("player2Uid")
                          .RunTransaction(data =>
        {
            string current = data.Value as string;

            // Somebody real got there first.
            if (!string.IsNullOrEmpty(current))
                return TransactionResult.Abort();

            data.Value = who.uid;
            seatTaken = true;

            return TransactionResult.Success(data);
        });

        yield return new WaitUntil(() => claim.IsCompleted);

        seatsBeingWatched.Remove(matchId);

        if (claim.IsFaulted || claim.IsCanceled)
        {
            Debug.LogWarning("[BOT] Could not claim the seat in " + matchId + ": " +
                             (claim.Exception != null ? claim.Exception.Message : "cancelled"));
            yield break;
        }

        if (!seatTaken)
        {
            Debug.Log("[BOT] Seat in " + matchId + " was already taken by somebody else.");
            yield break;
        }

        dbRoot.Child("matches").Child(matchId).Child("player2DisplayName")
              .SetValueAsync(who.name);

        // The game is full, so the offer comes down - but only if the offer
        // still standing is the one made for this match.
        dbRoot.Child("quickQueue").Child("waiting").GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists)
                return;

            object owner = task.Result.Child("uid").Value;
            object offered = task.Result.Child("matchId").Value;

            if (owner != null && owner.ToString() == myUid &&
                offered != null && offered.ToString() == matchId)
            {
                dbRoot.Child("quickQueue").Child("waiting").RemoveValueAsync();
            }
        });

        Debug.Log("[BOT] " + who.name + " (" + who.level + ") took the empty seat in " +
                  matchId + ".");

        // A move is owed now - round one was played while the offer was open.
        //
        // But the bot thinks with the local game's board and rack, and those
        // are only that match's while the player is actually in it. Standing
        // on the match list, the game holds nothing, or worse, holds another
        // match. So the seat being filled takes the player into the game,
        // which is what they asked for by pressing Quick game, and entering
        // is what asks the bot for its move.
        if (currentMatch != null && currentMatch.matchId == matchId)
        {
            CheckWhetherTheBotOwesAMove();
        }
        else if (currentMatch == null)
        {
            Debug.Log("[BOT] Taking the player into " + matchId + " so the round can go on.");
            ResumeMatch(matchId);
        }
        else
        {
            // Mid-game somewhere else: nobody is dragged out of it. The move
            // is owed and will be played the moment this match is opened.
            Debug.Log("[BOT] Player is in another match; " + matchId +
                      " will be answered when they open it.");
        }
    }

    private string BotUidOf(MatchData match)
    {
        string uid = OpponentUidOf(match);

        return BotOpponent.Is(uid) ? uid : null;
    }

    public bool OpponentIsABot()
    {
        return currentMatch != null && BotOpponent.Is(OpponentUidOf(currentMatch));
    }

    private string OpponentUidOf(MatchData match)
    {
        if (match == null)
            return null;

        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : null;

        return match.player1Uid == myUid ? match.player2Uid : match.player1Uid;
    }

    // Rounds the bot is already working on, so a second trigger for the same
    // round does not start a second search and write two moves.
    private readonly System.Collections.Generic.HashSet<string> botIsThinkingAbout =
        new System.Collections.Generic.HashSet<string>();

    // Called once the player's own submission is in.
    public void AskTheBotToAnswer(int roundNumber)
    {
        if (!OpponentIsABot() || currentMatch == null)
            return;

        if (this == null || !gameObject.activeInHierarchy)
            return;

        Answer(currentMatch.matchId, roundNumber);
    }

    // Whether the bot owes a move, asked of a submissions snapshot we already
    // have. Round one of a quick game is the case that matters: the player
    // plays it while the seat is still empty and the offer still open, so by
    // the time a bot sits down, their word is already in and nothing is going
    // to ask the bot for one.
    public void BotAnswersIfOwed(DataSnapshot submissions, int roundNumber)
    {
        if (!OpponentIsABot() || currentMatch == null || submissions == null)
            return;

        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : null;

        if (string.IsNullOrEmpty(myUid))
            return;

        bool mineIsIn = submissions.HasChild(myUid);
        string botUid = BotUidOf(currentMatch);
        bool theirsIsIn = !string.IsNullOrEmpty(botUid) && submissions.HasChild(botUid);

        // Never before the player: the bot answers a word, it does not open
        // with one.
        if (!mineIsIn || theirsIsIn)
        {
            Debug.Log("[BOT] Round " + roundNumber + ": nothing owed (mine in = " +
                      mineIsIn + ", theirs in = " + theirsIsIn + ").");
            return;
        }

        Debug.Log("[BOT] Round " + roundNumber + ": owes a move; thinking.");

        Answer(currentMatch.matchId, roundNumber);
    }

    // The same question when there is no snapshot to hand - on taking the
    // seat, or on opening a match again later.
    public void CheckWhetherTheBotOwesAMove()
    {
        if (currentMatch == null)
        {
            Debug.Log("[BOT] No match open, so nothing to answer for yet.");
            return;
        }

        if (!EnsureFirebaseReady() || !OpponentIsABot())
            return;

        string matchId = currentMatch.matchId;
        int round = Mathf.Max(1, currentMatch.currentRoundNumber);

        dbRoot.Child("matches").Child(matchId)
              .Child("rounds").Child(round.ToString())
              .Child("submissions")
              .GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null)
                return;

            BotAnswersIfOwed(task.Result, round);
        });
    }

    private void Answer(string matchId, int roundNumber)
    {
        string key = matchId + "#" + roundNumber;

        if (!botIsThinkingAbout.Add(key))
            return;

        if (this == null || !gameObject.activeInHierarchy)
            return;

        StartCoroutine(BotAnswers(matchId, roundNumber));
    }

    private IEnumerator BotAnswers(string matchId, int roundNumber)
    {
        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;
        string botUid = BotUidOf(currentMatch);

        if (logic == null || !EnsureFirebaseReady() || string.IsNullOrEmpty(botUid) ||
            currentMatch == null || currentMatch.matchId != matchId)
        {
            botIsThinkingAbout.Remove(matchId + "#" + roundNumber);
            yield break;
        }

        // Already decided and waiting for its moment.
        if (BotMoves.HasOneFor(matchId, roundNumber))
        {
            botIsThinkingAbout.Remove(matchId + "#" + roundNumber);
            yield break;
        }

        // When the player moved. The wait is measured from that, not from now,
        // so a fast phone does not shorten it.
        long theirStamp = 0;
        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : "";

        var mine = dbRoot.Child("matches").Child(matchId)
                         .Child("rounds").Child(roundNumber.ToString())
                         .Child("submissions").Child(myUid)
                         .GetValueAsync();

        yield return new WaitUntil(() => mine.IsCompleted);

        if (!mine.IsFaulted && !mine.IsCanceled && mine.Result != null && mine.Result.Exists)
            long.TryParse(mine.Result.Child("submittedAtUnix").Value + "", out theirStamp);

        // Decided now, while the board and the rack are still this match's -
        // which they only are while the player is in it. Played later.
        //
        // Every bot opens on the triple word if it can reach one. The search
        // picks from a band around its own strength, which on an empty board
        // walks straight past the best square on it, and a first move that
        // ignores a triple word is the clearest sign that nobody is there.
        RoundMove move = null;

        yield return logic.FindBotMove(BotOpponent.LevelOf(botUid), roundNumber == 1,
                                       found => move = found);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long dueAt = (theirStamp > 0 ? theirStamp : now) + BotOpponent.ThinkingTimeMs(botUid);

        RoundSubmissionData submission = new RoundSubmissionData
        {
            uid = botUid,
            word = move != null ? move.word : "",
            score = move != null ? move.score : 0,
            isValid = move != null && move.isValid,
            simulatedTilesJson = SerializeSimulatedTiles(move),

            // Nothing reads this, and a bot that took an hour has no sensible
            // clock left. A plausible number rather than a revealing one.
            secondsRemaining = UnityEngine.Random.Range(6, 48),

            // Always later than the player's, so a level score goes to them.
            submittedAtUnix = dueAt
        };

        BotMoves.Remember(new BotMoves.Pending
        {
            matchId = matchId,
            roundNumber = roundNumber,
            botUid = botUid,
            dueAtUnix = dueAt,
            submissionJson = JsonUtility.ToJson(submission)
        });

        botIsThinkingAbout.Remove(matchId + "#" + roundNumber);

        // It may already be due - the player can come back to a match hours
        // after playing it, in which case the answer was owed long ago.
        PlayAnythingDue();
    }

    // ------------------------------------------------------- posting them --

    private Coroutine theTicker;

    // Started once, by the controller, and left running: it is the only thing
    // that puts a decided move onto the board, and it has to work from any
    // screen, because the player will not be sitting in the match an hour
    // later waiting for it.
    public void KeepAnEyeOnPendingMoves()
    {
        if (theTicker == null)
            theTicker = StartCoroutine(Ticker());
    }

    private IEnumerator Ticker()
    {
        // A moment after launch, so Firebase has a chance to sign in.
        yield return new WaitForSeconds(4f);

        while (true)
        {
            PlayAnythingDue();

            yield return new WaitForSeconds(20f);
        }
    }

    // For the editor menu: play whatever is owed, right now.
    public void PlayDueBotMovesNow()
    {
        PlayAnythingDue();
    }

    private void PlayAnythingDue()
    {
        if (!EnsureFirebaseReady())
            return;

        foreach (BotMoves.Pending move in BotMoves.Due())
        {
            if (move == null || string.IsNullOrEmpty(move.submissionJson))
                continue;

            // Taken off the list first: a write that fails is better than one
            // written twice, and the bot owing a move is noticed again the
            // next time the match is opened.
            BotMoves.Forget(move.matchId, move.roundNumber);

            dbRoot.Child("matches").Child(move.matchId)
                  .Child("rounds").Child(move.roundNumber.ToString())
                  .Child("submissions").Child(move.botUid)
                  .SetRawJsonValueAsync(move.submissionJson)
                  .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError("[BOT] Could not write " + move.botUid +
                                   "'s move: " + task.Exception);
                    return;
                }

                Debug.Log("[BOT] " + move.botUid + " played round " +
                          move.roundNumber + " of " + move.matchId + ".");
            });
        }
    }
}
