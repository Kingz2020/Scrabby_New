using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A game with whoever else is looking for one, no invitation needed.
//
// There is one waiting slot. The first player to ask makes a room and puts
// themselves in the slot; the next takes them out of it and joins their room.
// From there it is the invitation path exactly - the room fills, whoever is
// watching it creates the match - so there is no second way for a match to
// start, and nothing here to keep in step with the first.
//
// The slot is taken in a transaction, and that is the whole reason this works.
// Two players asking in the same second would otherwise both read the same
// waiting player and both try to join them, and one of them would end up in a
// room nobody plays in. A transaction makes reading the slot and emptying it a
// single step, so only one of them can win.
public partial class MatchStatusPanel
{
    [SerializeField] private Button quickGameButton;

    // Nobody is there to choose, so it plays the same length as a solo game.
    private const int QuickGameRounds = 4;
    private const int QuickGameTurnMinutes = 5;

    // A waiting player older than this is treated as gone. Their app clearing
    // the slot on disconnect is what normally handles it; this is for when
    // that did not happen.
    private const long QuickGameStaleMs = 120000;

    private bool quickSearching;
    private string quickRoomCode;

    private DatabaseReference quickGuestRef;
    private EventHandler<ValueChangedEventArgs> quickGuestWatcher;

    private DatabaseReference QuickSlot
    {
        get { return dbRoot.Child("quickQueue").Child("waiting"); }
    }

    private void WireQuickGame()
    {
        if (quickGameButton == null)
            return;

        quickGameButton.onClick.RemoveAllListeners();
        quickGameButton.onClick.AddListener(OnQuickGamePressed);

        SetQuickGameLabel(false);
    }

    public void OnQuickGamePressed()
    {
        if (quickSearching)
        {
            CancelQuickGame(true);
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            ShowStatus("Sign in to play online.");
            return;
        }

        if (dbRoot == null)
        {
            ShowStatus("Firebase is not ready yet.");
            return;
        }

        quickSearching = true;
        SetQuickGameLabel(true);
        ShowStatus("Looking for an opponent...");

        CreateQuickRoomThenQueue();
    }

    // The room exists before its code goes in the slot. The other way round, a
    // player could be claimed in the gap and the claimer would try to join a
    // room that is not there yet.
    private void CreateQuickRoomThenQueue()
    {
        string code = GenerateRoomCode();
        string myUid = auth.CurrentUser.UserId;

        RoomData room = new RoomData
        {
            code = code,
            hostUid = myUid,
            hostDisplayName = MyQuickGameName(),
            guestUid = "",
            guestDisplayName = "",
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            playerCount = 2,
            totalRounds = QuickGameRounds,
            turnTimeMinutes = QuickGameTurnMinutes
        };

        dbRoot.Child("rooms").Child(code)
            .SetRawJsonValueAsync(JsonUtility.ToJson(room))
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogError("[QUICK] Room write failed: " + task.Exception);
                    StopQuickGame("Could not start a quick game.");
                    return;
                }

                quickRoomCode = code;
                ClaimOrQueue();
            });
    }

    private void ClaimOrQueue()
    {
        if (!quickSearching)
        {
            // Cancelled while the room was being written.
            DeleteQuickRoom();
            return;
        }

        string myUid = auth.CurrentUser.UserId;
        string myName = MyQuickGameName();
        string myRoom = quickRoomCode;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Set inside the transaction, which may run more than once: first
        // against the local cache, again against the server if they differ.
        // So every run starts from a clean slate, and only the last one counts.
        string claimedRoom = null;
        bool queued = false;
        bool clearedStale = false;

        QuickSlot.RunTransaction(data =>
        {
            claimedRoom = null;
            queued = false;
            clearedStale = false;

            var current = data.Value as Dictionary<string, object>;

            if (current != null)
            {
                string uid = Text(current, "uid");
                string room = Text(current, "roomCode");
                long createdAt = Number(current, "createdAt");

                if (uid == myUid)
                {
                    queued = true;
                    return TransactionResult.Success(data);
                }

                if (!string.IsNullOrEmpty(room) && now - createdAt < QuickGameStaleMs)
                {
                    claimedRoom = room;
                    data.Value = null;
                    return TransactionResult.Success(data);
                }

                // Somebody left behind. The rules only let a player put
                // themselves into an empty slot, so it is emptied here and
                // the whole thing tried again.
                data.Value = null;
                clearedStale = true;
                return TransactionResult.Success(data);
            }

            // Nobody waiting. Returning success rather than aborting when the
            // slot looks empty matters: the first run is often against a cache
            // that has not heard from the server, and an abort would give up
            // without ever seeing a player who is actually there.
            data.Value = new Dictionary<string, object>
            {
                { "uid", myUid },
                { "name", myName },
                { "roomCode", myRoom },
                { "createdAt", now }
            };

            queued = true;
            return TransactionResult.Success(data);
        })
        .ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogError("[QUICK] Queue transaction failed: " + task.Exception);

                // Most likely the database rules do not allow quickQueue yet.
                StopQuickGame("Quick game is not available right now.");
                return;
            }

            if (!quickSearching)
            {
                // Cancelled while the transaction ran.
                if (queued)
                    LeaveQuickSlot();

                DeleteQuickRoom();
                return;
            }

            if (clearedStale)
            {
                ClaimOrQueue();
                return;
            }

            if (!string.IsNullOrEmpty(claimedRoom))
            {
                // Someone was waiting, so the room made for waiting in is not
                // needed. Joining theirs goes down the invitation path.
                DeleteQuickRoom();

                quickSearching = false;
                SetQuickGameLabel(false);
                ShowStatus("Opponent found - starting...");

                preGamePanel.JoinRoomByCode(claimedRoom);
                return;
            }

            if (queued)
            {
                // If this app goes away while waiting, Firebase empties the
                // slot, so nobody joins a room nobody will play in.
                QuickSlot.OnDisconnect().RemoveValue();

                // Not added to this player's own rooms: with nobody invited it
                // would list as a bare "(waiting)" row whose button opens a
                // lobby for an empty room. The Cancel button already says a
                // search is on, and the match lists itself once it starts.
                WatchQuickRoomForGuest(quickRoomCode);

                // The same watch the invitation path uses. When the room fills
                // it creates the match and takes both players into it.
                preGamePanel.WatchRoom(quickRoomCode);

                ShowStatus("Waiting for an opponent...");
            }
        });
    }

    private void WatchQuickRoomForGuest(string code)
    {
        StopWatchingQuickRoom();

        quickGuestRef = dbRoot.Child("rooms").Child(code).Child("guestUid");

        quickGuestWatcher = (sender, args) =>
        {
            if (args.DatabaseError != null || args.Snapshot == null)
                return;

            string guest = args.Snapshot.Value as string;

            if (string.IsNullOrEmpty(guest))
                return;

            // Matched. The disconnect clean-up has to be switched off now:
            // left on, closing the app later would empty the slot even if
            // somebody new were waiting in it, and throw them out.
            QuickSlot.OnDisconnect().Cancel();

            StopWatchingQuickRoom();

            quickSearching = false;
            quickRoomCode = null;
            SetQuickGameLabel(false);
            ShowStatus("Opponent found - starting...");
        };

        quickGuestRef.ValueChanged += quickGuestWatcher;
    }

    private void StopWatchingQuickRoom()
    {
        if (quickGuestRef != null && quickGuestWatcher != null)
            quickGuestRef.ValueChanged -= quickGuestWatcher;

        quickGuestRef = null;
        quickGuestWatcher = null;
    }

    private void CancelQuickGame(bool announce)
    {
        if (!quickSearching && string.IsNullOrEmpty(quickRoomCode))
            return;

        quickSearching = false;
        SetQuickGameLabel(false);
        StopWatchingQuickRoom();

        if (dbRoot != null)
        {
            QuickSlot.OnDisconnect().Cancel();
            LeaveQuickSlot();
            DeleteQuickRoom();
        }

        if (announce)
            ShowStatus("Quick game cancelled.");
    }

    // Empties the slot only if it still holds this player - by now someone may
    // have taken them out of it, or someone else may be waiting there.
    private void LeaveQuickSlot()
    {
        if (auth == null || auth.CurrentUser == null)
            return;

        string myUid = auth.CurrentUser.UserId;

        QuickSlot.RunTransaction(data =>
        {
            var current = data.Value as Dictionary<string, object>;

            if (current == null)
                return TransactionResult.Success(data);

            if (Text(current, "uid") != myUid)
                return TransactionResult.Abort();

            data.Value = null;
            return TransactionResult.Success(data);
        });
    }

    // Removes the waiting room - unless someone has already joined it, in
    // which case the match is starting and is left to start.
    private void DeleteQuickRoom()
    {
        string code = quickRoomCode;
        quickRoomCode = null;

        if (string.IsNullOrEmpty(code) || dbRoot == null)
            return;

        dbRoot.Child("rooms").Child(code).RunTransaction(data =>
        {
            var current = data.Value as Dictionary<string, object>;

            if (current == null)
                return TransactionResult.Success(data);

            if (!string.IsNullOrEmpty(Text(current, "guestUid")))
                return TransactionResult.Abort();

            data.Value = null;
            return TransactionResult.Success(data);
        })
        .ContinueWithOnMainThread(task =>
        {
            if (!task.IsFaulted && !task.IsCanceled)
                RemoveRoomsFromCurrentUser(new List<string> { code });
        });
    }

    private void StopQuickGame(string message)
    {
        CancelQuickGame(false);
        ShowStatus(message);
    }

    private void SetQuickGameLabel(bool searching)
    {
        if (quickGameButton == null)
            return;

        TMP_Text label = quickGameButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.text = searching ? "Cancel" : "Quick game";
    }

    private string MyQuickGameName()
    {
        string name = auth.CurrentUser.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? auth.CurrentUser.Email : name;
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
