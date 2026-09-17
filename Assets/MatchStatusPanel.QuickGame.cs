using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A game you can start playing straight away, with whoever comes along.
//
// Pressing Quick game never means waiting around for someone to be online at
// the same moment. Either there is an open game - somebody started one and its
// second seat is empty - and you take that seat, or there is not and you start
// one yourself. Either way you are playing round 1 immediately. Online games
// already let each player take their turn in their own time, so an open game
// is just a match whose second player has not arrived yet: the round waits for
// two submissions whether the second one is an hour away or a minute.
//
// Only one open game is offered at a time, in a single slot. Taking a game out
// of it happens in a transaction, so two players pressing at once cannot both
// be handed the same seat.
public partial class MatchStatusPanel
{
    [SerializeField] private Button quickGameButton;

    // Nobody is there to choose, so it plays the same length as a solo game.
    private const int QuickGameRounds = 4;

    // An open game older than this is no longer offered to anyone new. Joining
    // a game whose creator has stopped playing leaves the joiner waiting on a
    // round that is never going to be answered.
    private const long QuickGameOfferMs = 12L * 60 * 60 * 1000;

    // Each step can find the slot changed under it and have to look again.
    // This keeps a run of bad luck from turning into a loop.
    private const int QuickGameMaxAttempts = 5;

    // TESTING ONLY. Once a quick game starts, turns the online match tracing
    // back on - entering the game, submitting a round, waiting, resolving -
    // which is normally muted because it floods the console. Set to false once
    // quick game is known to work.
    private const bool QuickGameTraceOnlineMatch = false;

    private bool quickBusy;

    private enum SlotResult { Empty, Own, Claimed, Stale, Queued }

    private DatabaseReference QuickSlot
    {
        get { return dbRoot.Child("quickQueue").Child("waiting"); }
    }

    private void WireQuickGame()
    {
        if (quickGameButton == null)
        {
            Debug.LogWarning("[QUICK] No quickGameButton assigned on MatchStatusPanel.");
            return;
        }

        quickGameButton.onClick.RemoveAllListeners();
        quickGameButton.onClick.AddListener(OnQuickGamePressed);
        SetQuickGameBusy(false);
    }

    public void OnQuickGamePressed()
    {
        if (quickBusy)
        {
            ScrabbyLog.Trace("[QUICK] Pressed while already starting one - ignored.");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            Debug.LogWarning("[QUICK] Pressed but nobody is signed in.");
            ShowStatus("Sign in to play online.");
            return;
        }

        if (dbRoot == null)
        {
            Debug.LogWarning("[QUICK] Pressed but Firebase is not ready.");
            ShowStatus("Firebase is not ready yet.");
            return;
        }

        if (preGamePanel == null)
        {
            Debug.LogError("[QUICK] preGamePanel is not assigned; cannot build a match.");
            return;
        }

        SetQuickGameBusy(true);
        ShowStatus("Starting a quick game...");

        ScrabbyLog.Trace("[QUICK] ===== Pressed | uid=" + auth.CurrentUser.UserId +
                  " name=" + MyQuickGameName() + " =====");

        if (QuickGameTraceOnlineMatch)
        {
            OnlineMatchController.Verbose = true;
            ScrabbyLog.Trace("[QUICK] Online match tracing switched on for this session " +
                      "([MATCHTRACE] and [OnlineMatchController] lines).");
        }

        FindOpenGame(1);
    }

    // ---- step 1: is somebody's open game waiting? -----------------------------
    private void FindOpenGame(int attempt)
    {
        if (attempt > QuickGameMaxAttempts)
        {
            QuickGameFailed("Gave up after " + QuickGameMaxAttempts + " attempts in FindOpenGame.");
            return;
        }

        string myUid = auth.CurrentUser.UserId;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Written inside the transaction, which may run more than once: first
        // against the local cache, again against the server if they disagree.
        // Every run starts clean so only the last one counts.
        SlotResult result = SlotResult.Empty;
        string foundMatch = null;

        ScrabbyLog.Trace("[QUICK] Step 1 (attempt " + attempt + "): looking in quickQueue/waiting");

        QuickSlot.RunTransaction(data =>
        {
            result = SlotResult.Empty;
            foundMatch = null;

            var current = data.Value as Dictionary<string, object>;
            ScrabbyLog.Trace("[QUICK]   step 1 run sees: " + DescribeSlot(data.Value, current, now));

            // Success, not abort, on an empty-looking slot: the first run is
            // often against a cache that has not heard from the server, and an
            // abort would give up without ever seeing a game that is there.
            if (current == null)
                return TransactionResult.Success(data);

            string uid = Text(current, "uid");
            string matchId = Text(current, "matchId");
            long age = now - Number(current, "createdAt");

            if (uid == myUid && !string.IsNullOrEmpty(matchId))
            {
                result = SlotResult.Own;
                foundMatch = matchId;
                return TransactionResult.Success(data);
            }

            // Too old, or left behind by the earlier version that queued rooms
            // rather than matches. Cleared, then looked at again.
            if (string.IsNullOrEmpty(matchId) || age >= QuickGameOfferMs)
            {
                data.Value = null;
                result = SlotResult.Stale;
                return TransactionResult.Success(data);
            }

            data.Value = null;
            result = SlotResult.Claimed;
            foundMatch = matchId;
            return TransactionResult.Success(data);
        })
        .ContinueWithOnMainThread(task =>
        {
            ScrabbyLog.Trace("[QUICK] Step 1 done | faulted=" + task.IsFaulted +
                      " canceled=" + task.IsCanceled + " result=" + result +
                      " match=" + foundMatch);

            if (task.IsFaulted || task.IsCanceled)
            {
                QuickGameFailed("Step 1 transaction failed: " + task.Exception, true);
                return;
            }

            switch (result)
            {
                case SlotResult.Own:
                    ScrabbyLog.Trace("[QUICK] My own open game " + foundMatch +
                              " is still waiting for an opponent - carrying on with it.");
                    EnterQuickGame(foundMatch);
                    break;

                case SlotResult.Stale:
                    ScrabbyLog.Trace("[QUICK] Cleared an expired or old-format entry; looking again.");
                    FindOpenGame(attempt + 1);
                    break;

                case SlotResult.Claimed:
                    JoinOpenGame(foundMatch, attempt);
                    break;

                default:
                    ScrabbyLog.Trace("[QUICK] Nobody's open game is waiting - starting a new one.");
                    CreateOpenGame(attempt);
                    break;
            }
        });
    }

    // ---- step 2a: take the empty seat in somebody's game ----------------------
    private void JoinOpenGame(string matchId, int attempt)
    {
        string myUid = auth.CurrentUser.UserId;
        DatabaseReference match = dbRoot.Child("matches").Child(matchId);

        ScrabbyLog.Trace("[QUICK] Step 2a: joining matches/" + matchId);

        // Checked first so a game that has since been removed is not recreated
        // as a stub by writing a player into it.
        match.Child("player1Uid").GetValueAsync().ContinueWithOnMainThread(readTask =>
        {
            string player1 = readTask.IsFaulted || readTask.Result == null
                ? null
                : readTask.Result.Value as string;

            ScrabbyLog.Trace("[QUICK]   match player1Uid=" + (player1 ?? "(missing)") +
                      " readFaulted=" + readTask.IsFaulted);

            if (string.IsNullOrEmpty(player1))
            {
                Debug.LogWarning("[QUICK]   that open game no longer exists; looking again.");
                FindOpenGame(attempt + 1);
                return;
            }

            if (player1 == myUid)
            {
                EnterQuickGame(matchId);
                return;
            }

            bool joined = false;

            match.Child("player2Uid").RunTransaction(data =>
            {
                string seat = data.Value as string;
                joined = false;

                if (string.IsNullOrEmpty(seat))
                {
                    data.Value = myUid;
                    joined = true;
                    return TransactionResult.Success(data);
                }

                if (seat == myUid)
                {
                    joined = true;
                    return TransactionResult.Success(data);
                }

                return TransactionResult.Abort();
            })
            .ContinueWithOnMainThread(seatTask =>
            {
                ScrabbyLog.Trace("[QUICK]   seat transaction | faulted=" + seatTask.IsFaulted +
                          " joined=" + joined);

                if (seatTask.IsFaulted || seatTask.IsCanceled)
                {
                    QuickGameFailed("Seat transaction failed: " + seatTask.Exception, true);
                    return;
                }

                if (!joined)
                {
                    Debug.LogWarning("[QUICK]   seat 2 was already taken; looking again.");
                    FindOpenGame(attempt + 1);
                    return;
                }

                var names = new Dictionary<string, object>
                {
                    { "player2DisplayName", MyQuickGameName() },
                    { "guestUid", myUid }
                };

                match.UpdateChildrenAsync(names).ContinueWithOnMainThread(nameTask =>
                {
                    ScrabbyLog.Trace("[QUICK]   seat 2 taken as " + MyQuickGameName() +
                              " | name write faulted=" + nameTask.IsFaulted);

                    preGamePanel.AddMatchToUser(myUid, matchId, () =>
                    {
                        ScrabbyLog.Trace("[QUICK]   added " + matchId + " to my matches.");
                        EnterQuickGame(matchId);
                    });
                });
            });
        });
    }

    // ---- step 2b: start a game of my own, with its second seat open -----------
    private void CreateOpenGame(int attempt)
    {
        string myUid = auth.CurrentUser.UserId;
        string matchId = dbRoot.Child("matches").Push().Key;

        MatchData match = preGamePanel.BuildNewMatch(
            matchId, "",
            myUid, MyQuickGameName(),
            "", "",
            QuickGameRounds);

        ScrabbyLog.Trace("[QUICK] Step 2b: writing new open game matches/" + matchId);

        dbRoot.Child("matches").Child(matchId)
            .SetRawJsonValueAsync(JsonUtility.ToJson(match))
            .ContinueWithOnMainThread(writeTask =>
            {
                ScrabbyLog.Trace("[QUICK]   match write faulted=" + writeTask.IsFaulted);

                if (writeTask.IsFaulted || writeTask.IsCanceled)
                {
                    QuickGameFailed("Match write failed: " + writeTask.Exception, true);
                    return;
                }

                OfferOpenGame(matchId, attempt, 1);
            });
    }

    // ---- step 3: put it in the slot for the next player -----------------------
    private void OfferOpenGame(string matchId, int attempt, int offerAttempt)
    {
        if (offerAttempt > QuickGameMaxAttempts)
        {
            DeleteUnplayedGame(matchId);
            QuickGameFailed("Gave up after " + QuickGameMaxAttempts + " attempts in OfferOpenGame.");
            return;
        }

        string myUid = auth.CurrentUser.UserId;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        SlotResult result = SlotResult.Empty;
        string otherMatch = null;

        ScrabbyLog.Trace("[QUICK] Step 3 (offer attempt " + offerAttempt + "): offering " + matchId);

        QuickSlot.RunTransaction(data =>
        {
            result = SlotResult.Empty;
            otherMatch = null;

            var current = data.Value as Dictionary<string, object>;
            ScrabbyLog.Trace("[QUICK]   step 3 run sees: " + DescribeSlot(data.Value, current, now));

            if (current == null)
            {
                data.Value = new Dictionary<string, object>
                {
                    { "uid", myUid },
                    { "name", MyQuickGameName() },
                    { "matchId", matchId },
                    { "createdAt", now }
                };

                result = SlotResult.Queued;
                return TransactionResult.Success(data);
            }

            string uid = Text(current, "uid");
            string existing = Text(current, "matchId");
            long age = now - Number(current, "createdAt");

            if (uid == myUid && !string.IsNullOrEmpty(existing))
            {
                result = SlotResult.Own;
                otherMatch = existing;
                return TransactionResult.Success(data);
            }

            // The rules only let a player put themselves into an empty slot, so
            // anything stale is cleared first and the offer made again.
            if (string.IsNullOrEmpty(existing) || age >= QuickGameOfferMs)
            {
                data.Value = null;
                result = SlotResult.Stale;
                return TransactionResult.Success(data);
            }

            // Somebody offered a game in the moment between looking and
            // offering. Theirs is taken instead, and the new one discarded.
            data.Value = null;
            result = SlotResult.Claimed;
            otherMatch = existing;
            return TransactionResult.Success(data);
        })
        .ContinueWithOnMainThread(task =>
        {
            ScrabbyLog.Trace("[QUICK] Step 3 done | faulted=" + task.IsFaulted +
                      " canceled=" + task.IsCanceled + " result=" + result +
                      " other=" + otherMatch);

            if (task.IsFaulted || task.IsCanceled)
            {
                DeleteUnplayedGame(matchId);
                QuickGameFailed("Step 3 transaction failed: " + task.Exception, true);
                return;
            }

            switch (result)
            {
                case SlotResult.Queued:
                    ScrabbyLog.Trace("[QUICK] Offered " + matchId + " - playing round 1 while waiting.");
                    preGamePanel.AddMatchToUser(myUid, matchId, () =>
                    {
                        ScrabbyLog.Trace("[QUICK]   added " + matchId + " to my matches.");
                        EnterQuickGame(matchId);
                    });
                    break;

                case SlotResult.Stale:
                    OfferOpenGame(matchId, attempt, offerAttempt + 1);
                    break;

                case SlotResult.Own:
                    DeleteUnplayedGame(matchId);
                    EnterQuickGame(otherMatch);
                    break;

                case SlotResult.Claimed:
                    DeleteUnplayedGame(matchId);
                    JoinOpenGame(otherMatch, attempt);
                    break;
            }
        });
    }

    // ---- in you go ----------------------------------------------------------------
    // The same way in as pressing Resume on a match in the list.
    private void EnterQuickGame(string matchId)
    {
        ScrabbyLog.Trace("[QUICK] Entering match " + matchId + " via ResumeMatch.");

        SetQuickGameBusy(false);
        ShowStatus("Starting...");

        Singleton.Instance.OnlineMatchController.ResumeMatch(matchId);
    }

    // A game made a moment ago that turned out not to be needed. Nobody has
    // played in it and it is on nobody's list, so it can simply go.
    private void DeleteUnplayedGame(string matchId)
    {
        if (string.IsNullOrEmpty(matchId))
            return;

        ScrabbyLog.Trace("[QUICK] Discarding unused match " + matchId);
        dbRoot.Child("matches").Child(matchId).RemoveValueAsync();
    }

    private void QuickGameFailed(string reason, bool rulesMayBeTheCause = false)
    {
        Debug.LogError("[QUICK] FAILED: " + reason);

        SetQuickGameBusy(false);
        ShowStatus(rulesMayBeTheCause
            ? "Quick game is not available right now."
            : "Could not start a quick game. Try again.");
    }

    private void SetQuickGameBusy(bool busy)
    {
        quickBusy = busy;

        if (quickGameButton == null)
            return;

        quickGameButton.interactable = !busy;

        TMP_Text label = quickGameButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.text = busy ? "Starting..." : "Quick game";
    }

    private string MyQuickGameName()
    {
        string name = auth.CurrentUser.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? auth.CurrentUser.Email : name;
    }

    private static string DescribeSlot(object raw, Dictionary<string, object> map, long now)
    {
        if (raw == null)
            return "EMPTY";

        if (map == null)
            return "UNREADABLE (" + raw.GetType().Name + ")";

        return "uid=" + Text(map, "uid") + " name=" + Text(map, "name") +
               " matchId=" + Text(map, "matchId") +
               " age=" + ((now - Number(map, "createdAt")) / 1000) + "s";
    }

    private static string Text(Dictionary<string, object> map, string key)
    {
        object value;
        return map.TryGetValue(key, out value) && value != null ? value.ToString() : "";
    }

    private static long Number(Dictionary<string, object> map, string key)
    {
        object value;

        if (!map.TryGetValue(key, out value) || value == null)
            return 0;

        long parsed;
        return long.TryParse(value.ToString(), out parsed) ? parsed : 0;
    }
}
