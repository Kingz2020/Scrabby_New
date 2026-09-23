using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

// The bot's half of a quick game.
//
// Nothing here is special-cased downstream: the bot's move is written into
// the same submissions node a person's would be, with the same fields, and
// the round resolves the way every other round resolves.
//
// The work is split between this phone and the server, because each can do
// only half of it:
//
//   - Only this phone can find the bot's word. The search needs the match's
//     board and letters, which are loaded here while the player is in the
//     game - so the word is found the moment the player plays theirs, the
//     one moment the board is certainly this match's.
//
//   - Only the server can play it later. A bot answers after minutes or
//     hours, and by then the phone is in a pocket with the app asleep. So the
//     word waits, hidden, in botQueue/{matchId}, and a scheduled function on
//     the server (functions/index.js, botReferee) posts it when it is due -
//     which is also what sends the "your move" notification, exactly as for
//     a person.
//
// The server also seats the bot when nobody has joined, so a quick game
// fills even if the app was closed in the first few seconds. This phone does
// the same while it is open, which is quicker; whichever gets there first
// takes the seat, and the other finds it taken.
//
// botQueue/{matchId}:
//     ownerUid     the player who opened the quick game (only they can read it)
//     botUid       who will sit down, chosen when the game is offered
//     botName
//     seatAtUnix   when to sit down if nobody real has
//     moves/{round}: { dueAtUnix, submission }
public partial class OnlineMatchController
{
    private const string BotQueue = "botQueue";

    // ------------------------------------------------------------ the seat --

    private readonly HashSet<string> seatsBeingWatched = new HashSet<string>();

    // Who will sit down in each quick game offered from this phone, so a word
    // played before the seat is taken can already be answered.
    private readonly Dictionary<string, string> plannedBots = new Dictionary<string, string>();

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

        if (!EnsureFirebaseReady() || GetCurrentUser() == null)
        {
            Debug.LogWarning("[BOT] Firebase is not ready; cannot plan a bot for " + matchId + ".");
            return;
        }

        PlanTheBot(matchId, seconds);

        // Pressing Quick game twice should not start two countdowns on the
        // same seat.
        if (!seatsBeingWatched.Add(matchId))
            return;

        StartCoroutine(BotTakesTheSeat(matchId, seconds));
    }

    // Written to the database, so the server knows who to seat and when, even
    // if this phone is never opened again.
    private void PlanTheBot(string matchId, float seconds)
    {
        string myUid = GetCurrentUser().UserId;
        long seatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(seconds * 1000f);
        DatabaseReference plan = dbRoot.Child(BotQueue).Child(matchId);

        plan.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[BOT] Could not read the plan for " + matchId + ": " + task.Exception);
                return;
            }

            DataSnapshot existing = task.Result;

            if (existing != null && existing.Exists && existing.Child("botUid").Value != null)
            {
                plannedBots[matchId] = existing.Child("botUid").Value.ToString();

                // Coming back to a game that has waited: sit down sooner.
                long already;
                long.TryParse(existing.Child("seatAtUnix").Value + "", out already);

                if (already == 0 || seatAt < already)
                    plan.Child("seatAtUnix").SetValueAsync(seatAt);

                return;
            }

            BotOpponent.Player who = BotOpponent.PickSomebody();
            plannedBots[matchId] = who.uid;

            Dictionary<string, object> fields = new Dictionary<string, object>
            {
                { "ownerUid", myUid },
                { "botUid", who.uid },
                { "botName", who.name },
                { "seatAtUnix", seatAt }
            };

            plan.UpdateChildrenAsync(fields).ContinueWithOnMainThread(write =>
            {
                if (write.IsFaulted || write.IsCanceled)
                    Debug.LogWarning("[BOT] Could not write the plan for " + matchId + ": " + write.Exception);
                else
                    Debug.Log("[BOT] " + who.name + " will sit down in " + matchId +
                              " if nobody comes.");
            });
        });
    }

    // The quick way in, while this phone is open. The server does the same a
    // minute or so later if this never gets to run.
    private IEnumerator BotTakesTheSeat(string matchId, float seconds)
    {
        float waited = 0f;

        while (waited < seconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        seatsBeingWatched.Remove(matchId);

        if (!EnsureFirebaseReady() || GetCurrentUser() == null)
            yield break;

        string botUid;

        if (!plannedBots.TryGetValue(matchId, out botUid) || string.IsNullOrEmpty(botUid))
        {
            // The plan never got written; the server will not seat anybody
            // either, so this is the only chance.
            botUid = BotOpponent.PickSomebody().uid;
            plannedBots[matchId] = botUid;
        }

        string myUid = GetCurrentUser().UserId;
        bool seatTaken = false;
        string takenBy = botUid;

        var claim = dbRoot.Child("matches").Child(matchId).Child("player2Uid")
                          .RunTransaction(data =>
        {
            string current = data.Value as string;

            // Somebody real - or the server's bot - got there first.
            if (!string.IsNullOrEmpty(current))
                return TransactionResult.Abort();

            data.Value = takenBy;
            seatTaken = true;

            return TransactionResult.Success(data);
        });

        yield return new WaitUntil(() => claim.IsCompleted);

        if (claim.IsFaulted || claim.IsCanceled || !seatTaken)
            yield break;

        dbRoot.Child("matches").Child(matchId).Child("player2DisplayName")
              .SetValueAsync(BotOpponent.NameOf(botUid));

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

        Debug.Log("[BOT] " + BotOpponent.NameOf(botUid) + " took the empty seat in " + matchId + ".");

        // A word already played in round one is answered now, if the board
        // is still that match's; otherwise it was answered when it was played.
        if (currentMatch != null && currentMatch.matchId == matchId)
            CheckWhetherTheBotOwesAMove();
    }

    // ------------------------------------------------------------ who it is --

    private string BotUidOf(MatchData match)
    {
        if (match == null)
            return null;

        string uid = OpponentUidOf(match);

        if (BotOpponent.Is(uid))
            return uid;

        // The seat is still empty, but a bot has been chosen for it: a word
        // played now is answered by whoever will sit down.
        string planned;

        if (string.IsNullOrEmpty(uid) && plannedBots.TryGetValue(match.matchId, out planned))
            return planned;

        return null;
    }

    public bool OpponentIsABot()
    {
        return currentMatch != null && BotOpponent.Is(OpponentUidOf(currentMatch));
    }

    private bool OpponentIsOrWillBeABot()
    {
        return !string.IsNullOrEmpty(BotUidOf(currentMatch));
    }

    private string OpponentUidOf(MatchData match)
    {
        if (match == null)
            return null;

        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : null;

        return match.player1Uid == myUid ? match.player2Uid : match.player1Uid;
    }

    // ---------------------------------------------------------- the board --

    // Which match the game's board and letters are loaded with. The word
    // search reads the board as it is, so it is only ever run when that is
    // the match being answered - never from the match list, where the board
    // holds whatever was last played, or nothing.
    private string boardHoldsMatchId;

    private void BoardNowHolds(string matchId)
    {
        boardHoldsMatchId = matchId;

        // An own quick game with its seat still empty: find out who is going
        // to sit in it, so the word about to be played can be answered. The
        // phone may have been restarted since the game was offered, and what
        // it knew then went with it.
        if (currentMatch == null || currentMatch.matchId != matchId ||
            !string.IsNullOrEmpty(currentMatch.player2Uid) ||
            plannedBots.ContainsKey(matchId) ||
            GetCurrentUser() == null || currentMatch.player1Uid != GetCurrentUser().UserId ||
            !EnsureFirebaseReady())
        {
            return;
        }

        dbRoot.Child(BotQueue).Child(matchId).Child("botUid").GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            if (!task.IsFaulted && !task.IsCanceled && task.Result != null &&
                task.Result.Exists && task.Result.Value != null)
            {
                plannedBots[matchId] = task.Result.Value.ToString();
            }
        });
    }

    private void BoardHoldsNothing()
    {
        boardHoldsMatchId = null;
    }

    // -------------------------------------------------------- the answer --

    // Rounds the bot is already working on, so a second trigger for the same
    // round does not start a second search.
    private readonly HashSet<string> botIsThinkingAbout = new HashSet<string>();

    // Called once the player's own submission is in.
    public void AskTheBotToAnswer(int roundNumber)
    {
        if (currentMatch == null || !OpponentIsOrWillBeABot())
            return;

        if (this == null || !gameObject.activeInHierarchy)
            return;

        Answer(currentMatch.matchId, roundNumber);
    }

    // Whether the bot owes a move, asked of a submissions snapshot we already
    // have.
    public void BotAnswersIfOwed(DataSnapshot submissions, int roundNumber)
    {
        if (currentMatch == null || submissions == null || !OpponentIsOrWillBeABot())
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
            return;

        Answer(currentMatch.matchId, roundNumber);
    }

    // The same question when there is no snapshot to hand.
    public void CheckWhetherTheBotOwesAMove()
    {
        if (currentMatch == null || !EnsureFirebaseReady() || !OpponentIsOrWillBeABot())
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
        // Only with this match on the board. If it is not, the search would
        // answer a board from some other game; the server gives the round up
        // as a pass after a long wait instead (botReferee's safety net).
        if (boardHoldsMatchId != matchId)
        {
            Debug.Log("[BOT] Round " + roundNumber + " of " + matchId +
                      " is owed, but the board is not that match's; not searching.");
            return;
        }

        string key = matchId + "#" + roundNumber;

        if (!botIsThinkingAbout.Add(key))
            return;

        if (this == null || !gameObject.activeInHierarchy)
        {
            botIsThinkingAbout.Remove(key);
            return;
        }

        StartCoroutine(BotAnswers(matchId, roundNumber));
    }

    private IEnumerator BotAnswers(string matchId, int roundNumber)
    {
        string key = matchId + "#" + roundNumber;
        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;
        string botUid = BotUidOf(currentMatch);
        string myUid = GetCurrentUser() != null ? GetCurrentUser().UserId : "";

        if (logic == null || !EnsureFirebaseReady() || string.IsNullOrEmpty(botUid) ||
            currentMatch == null || currentMatch.matchId != matchId || string.IsNullOrEmpty(myUid))
        {
            botIsThinkingAbout.Remove(key);
            yield break;
        }

        // Already worked out - by this phone earlier, or waiting on the server.
        var queued = dbRoot.Child(BotQueue).Child(matchId)
                           .Child("moves").Child(roundNumber.ToString())
                           .GetValueAsync();

        yield return new WaitUntil(() => queued.IsCompleted);

        if (!queued.IsFaulted && !queued.IsCanceled && queued.Result != null && queued.Result.Exists)
        {
            botIsThinkingAbout.Remove(key);
            yield break;
        }

        // When the player moved. The wait is measured from that, not from now,
        // so a fast phone does not shorten it.
        long theirStamp = 0;

        var mine = dbRoot.Child("matches").Child(matchId)
                         .Child("rounds").Child(roundNumber.ToString())
                         .Child("submissions").Child(myUid)
                         .GetValueAsync();

        yield return new WaitUntil(() => mine.IsCompleted);

        if (!mine.IsFaulted && !mine.IsCanceled && mine.Result != null && mine.Result.Exists)
            long.TryParse(mine.Result.Child("submittedAtUnix").Value + "", out theirStamp);

        // Every bot opens on the triple word if it can reach one. The search
        // picks from a band around its own strength, which on an empty board
        // walks straight past the best square on it, and a first move that
        // ignores a triple word is the clearest sign that nobody is there.
        RoundMove move = null;

        yield return logic.FindBotMove(BotOpponent.LevelOf(botUid), roundNumber == 1,
                                       found => move = found);

        // The player may have left the match while it was searching, and the
        // board with it; a word found on a board that changed underneath is
        // not safe to play.
        if (boardHoldsMatchId != matchId)
        {
            Debug.Log("[BOT] The board changed while searching " + matchId + "; dropped.");
            botIsThinkingAbout.Remove(key);
            yield break;
        }

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

        // The whole plan is written with the move, so a match whose seat was
        // filled before plans existed gets one here and the server can play
        // it like any other.
        string raw = "{\"dueAtUnix\":" + dueAt +
                     ",\"submission\":" + JsonUtility.ToJson(submission) + "}";

        DatabaseReference plan = dbRoot.Child(BotQueue).Child(matchId);

        var header = plan.UpdateChildrenAsync(new Dictionary<string, object>
        {
            { "ownerUid", myUid },
            { "botUid", botUid },
            { "botName", BotOpponent.NameOf(botUid) }
        });

        yield return new WaitUntil(() => header.IsCompleted);

        var written = plan.Child("moves").Child(roundNumber.ToString()).SetRawJsonValueAsync(raw);

        yield return new WaitUntil(() => written.IsCompleted);

        botIsThinkingAbout.Remove(key);

        if (header.IsFaulted || written.IsFaulted || written.IsCanceled)
        {
            Debug.LogError("[BOT] Could not queue " + botUid + "'s move for round " +
                           roundNumber + " of " + matchId + ": " +
                           (written.Exception ?? header.Exception));
            yield break;
        }

        Debug.Log("[BOT] " + BotOpponent.NameOf(botUid) + " will play '" + submission.word +
                  "' (" + submission.score + ") in round " + roundNumber + " of " + matchId +
                  ", " + ((dueAt - now) / 60000) + " min from now.");
    }

    // ------------------------------------------------------ the old queue --

    // Moves decided by earlier versions were kept on the phone rather than in
    // the database. Those still get played, from here, until there are none
    // left; nothing adds to that list any more.
    private Coroutine theTicker;

    public void KeepAnEyeOnPendingMoves()
    {
        if (theTicker == null)
            theTicker = StartCoroutine(Ticker());
    }

    private IEnumerator Ticker()
    {
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

            BotMoves.Forget(move.matchId, move.roundNumber);

            dbRoot.Child("matches").Child(move.matchId)
                  .Child("rounds").Child(move.roundNumber.ToString())
                  .Child("submissions").Child(move.botUid)
                  .SetRawJsonValueAsync(move.submissionJson);
        }
    }
}
