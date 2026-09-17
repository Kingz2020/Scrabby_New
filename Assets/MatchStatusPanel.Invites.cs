using System;
using System.Collections;
using Firebase.Database;
using Firebase.Extensions;
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

            StartCoroutine(WaitForInviteAnswer(requestRef, onDone));
        });
    }

    private IEnumerator WaitForInviteAnswer(
        DatabaseReference requestRef, Action<bool, string, string> onDone)
    {
        string status = null;
        string invitedName = "";

        EventHandler<ValueChangedEventArgs> watcher = (sender, args) =>
        {
            if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists)
                return;

            object value = args.Snapshot.Child("status").Value;
            object name = args.Snapshot.Child("toName").Value;

            if (value != null)
                status = value.ToString();

            if (name != null)
                invitedName = name.ToString();
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
}
