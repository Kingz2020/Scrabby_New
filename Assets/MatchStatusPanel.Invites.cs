using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;

// Asking for an invitation to be sent, rather than sending it.
//
// The game used to search every player's profile to turn an email address into
// a player, then write the invitation into that player's own profile. Both
// needed a database where anyone signed in could read and write anyone's data
// - which also let anyone read every player's email and rewrite other
// people's games.
//
// So the game now leaves a request, carrying only its own name, and the server
// (functions/index.js, onInviteRequest) looks the address up and delivers it.
// The answer comes back on the request: sent, and to whom, or nobody has that
// address.
public partial class MatchStatusPanel
{
    // Long enough for a cold server to wake up, short enough that nobody is
    // left looking at "Sending...".
    private const float InviteRequestTimeout = 25f;

    // toEmail for an invitation typed in, toUid for a rematch with someone
    // already known. onDone(ok, message, invitedName).
    public void SendInviteRequest(
        string roomCode, string toEmail, string toUid,
        Action<bool, string, string> onDone)
    {
        if (dbRoot == null || auth == null || auth.CurrentUser == null)
        {
            onDone(false, "Not connected yet.", "");
            return;
        }

        string myName = string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName)
            ? auth.CurrentUser.Email
            : auth.CurrentUser.DisplayName;

        InviteRequestData request = new InviteRequestData
        {
            fromUid = auth.CurrentUser.UserId,
            fromDisplayName = myName,
            roomCode = roomCode,
            toEmail = toEmail ?? "",
            toUid = toUid ?? "",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        DatabaseReference requestRef = dbRoot.Child("inviteRequests").Push();

        requestRef.SetRawJsonValueAsync(JsonUtility.ToJson(request))
                  .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[INVITE] Could not ask for an invitation: " + task.Exception);
                onDone(false, "Could not send the invitation.", "");
                return;
            }

            StartCoroutine(WaitForInviteAnswer(requestRef, toEmail, onDone));
        });
    }

    private IEnumerator WaitForInviteAnswer(
        DatabaseReference requestRef, string typedEmail,
        Action<bool, string, string> onDone)
    {
        string status = null;
        string invitedName = "";
        string invitedUid = "";

        EventHandler<ValueChangedEventArgs> watcher = (sender, args) =>
        {
            if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists)
                return;

            object value = args.Snapshot.Child("status").Value;
            object name = args.Snapshot.Child("toName").Value;
            object uid = args.Snapshot.Child("toUid").Value;

            if (value != null)
                status = value.ToString();

            if (name != null)
                invitedName = name.ToString();

            if (uid != null)
                invitedUid = uid.ToString();
        };

        DatabaseReference resultRef = requestRef.Child("result");
        resultRef.ValueChanged += watcher;

        float waited = 0f;

        while (status == null && waited < InviteRequestTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        resultRef.ValueChanged -= watcher;

        // The request has been answered; it is not a record worth keeping.
        requestRef.RemoveValueAsync();

        switch (status)
        {
            case "sent":
                // Worth remembering: the server has just confirmed this
                // address belongs to a player, so it can be offered next time
                // rather than typed again.
                RememberOpponent(invitedUid, invitedName, typedEmail);

                onDone(true, "Invitation sent to " +
                             (string.IsNullOrEmpty(invitedName) ? "them" : invitedName) + ".",
                       invitedName);
                break;

            case "no-user":
                onDone(false, "No Scrabby player has that email address.", "");
                break;

            case "self":
                onDone(false, "You cannot invite yourself.", "");
                break;

            case null:
                onDone(false, "The invitation is taking too long - try again.", "");
                break;

            default:
                onDone(false, "The invitation could not be sent.", "");
                break;
        }
    }

    [Serializable]
    private class InviteRequestData
    {
        public string fromUid;
        public string fromDisplayName;
        public string roomCode;
        public string toEmail;
        public string toUid;
        public long createdAtUnix;
    }

    // ------------------------------------------------ people you have played
    //
    // Typing a friend's address every time is a chore, and a typo means "no
    // Scrabby player has that email address" rather than a game. Anyone the
    // server has found once is kept on the player's own profile and offered
    // in a list.

    [SerializeField] private TMP_Dropdown recentOpponentsDropdown;

    private readonly List<string> recentOpponentEmails = new List<string>();

    [Serializable]
    private class KnownOpponent
    {
        public string email;
        public string name;
        public long lastPlayedUnix;
    }

    private void WireRecentOpponents()
    {
        if (recentOpponentsDropdown == null)
            return;

        recentOpponentsDropdown.onValueChanged.RemoveAllListeners();
        recentOpponentsDropdown.onValueChanged.AddListener(OnRecentOpponentChosen);

        LoadRecentOpponents();
    }

    private void OnRecentOpponentChosen(int index)
    {
        // The first line is the prompt, not a player.
        if (index <= 0 || index > recentOpponentEmails.Count || inviteInput == null)
            return;

        inviteInput.SetTextWithoutNotify(recentOpponentEmails[index - 1]);
        inviteInput.ForceLabelUpdate();
    }

    public void LoadRecentOpponents()
    {
        if (recentOpponentsDropdown == null || dbRoot == null ||
            auth == null || auth.CurrentUser == null)
        {
            return;
        }

        dbRoot.Child("users").Child(auth.CurrentUser.UserId).Child("knownOpponents")
              .GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || recentOpponentsDropdown == null)
                return;

            List<KnownOpponent> known = new List<KnownOpponent>();

            if (task.Result != null && task.Result.Exists)
            {
                foreach (DataSnapshot child in task.Result.Children)
                {
                    KnownOpponent entry =
                        JsonUtility.FromJson<KnownOpponent>(child.GetRawJsonValue());

                    if (entry != null && !string.IsNullOrEmpty(entry.email))
                        known.Add(entry);
                }
            }

            // Most recently played first: the next game is usually with
            // whoever you played last.
            known.Sort((a, b) => b.lastPlayedUnix.CompareTo(a.lastPlayedUnix));

            recentOpponentEmails.Clear();

            List<string> lines = new List<string>
            {
                known.Count == 0 ? "No one yet - type an address" : "Choose a player"
            };

            foreach (KnownOpponent entry in known)
            {
                recentOpponentEmails.Add(entry.email);

                lines.Add(string.IsNullOrEmpty(entry.name) || entry.name == entry.email
                    ? entry.email
                    : entry.name + "  (" + entry.email + ")");
            }

            recentOpponentsDropdown.ClearOptions();
            recentOpponentsDropdown.AddOptions(lines);
            recentOpponentsDropdown.SetValueWithoutNotify(0);
            recentOpponentsDropdown.RefreshShownValue();

            ScrabbyLog.Trace("[INVITE] " + known.Count + " known opponent(s) listed.");
        });
    }

    private void RememberOpponent(string uid, string name, string email)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(email) ||
            dbRoot == null || auth == null || auth.CurrentUser == null)
        {
            return;
        }

        KnownOpponent entry = new KnownOpponent
        {
            email = email,
            name = name ?? "",
            lastPlayedUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        dbRoot.Child("users").Child(auth.CurrentUser.UserId)
              .Child("knownOpponents").Child(uid)
              .SetRawJsonValueAsync(JsonUtility.ToJson(entry))
              .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogWarning("[INVITE] Could not remember " + email + ": " + task.Exception);
                return;
            }

            LoadRecentOpponents();
        });
    }
}
