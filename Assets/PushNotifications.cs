using Firebase.Extensions;
using Firebase.Messaging;
using UnityEngine;

// The phone's half of push notifications.
//
// Firebase gives every install an address (a token) that notifications can be
// sent to. This asks the player for permission to show them (Android 13 and up
// asks; older phones allow it anyway), and keeps the signed-in player's token
// at pushTokens/{uid} so the server functions (functions/index.js) know where
// to send "your move".
//
// The token is kept outside users/{uid} on purpose: the profile is written
// whole in places, and a token stored inside it would be wiped each time.
public static class PushNotifications
{
    private static bool started;
    private static string token;

    public static void Begin()
    {
        if (started)
            return;

        started = true;

        FirebaseMessaging.TokenReceived += (sender, e) => Save(e.Token);

        // A notification that arrives while the game is open is not shown by
        // Android; the player is already looking at the game.
        FirebaseMessaging.MessageReceived += (sender, e) =>
            ScrabbyLog.Trace("[PUSH] Received while open: " +
                      (e.Message.Notification != null ? e.Message.Notification.Body : "(data only)"));

        FirebaseMessaging.RequestPermissionAsync().ContinueWithOnMainThread(task =>
            ScrabbyLog.Trace("[PUSH] Permission request " + (task.IsFaulted ? "failed: " + task.Exception : "done.")));

        // A notification says "your move" or "the round is decided". Once the
        // game is open, it has been read, whether it was tapped or the player
        // came in by the icon - and Android only clears the one that was
        // tapped. So they go when the game comes to the front.
        ClearShownNotifications();
        Application.focusChanged += OnFocusChanged;

        FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[PUSH] Could not get a token: " + task.Exception);
                return;
            }

            Save(task.Result);
        });
    }

    private static void OnFocusChanged(bool focused)
    {
        if (focused)
            ClearShownNotifications();
    }

    // Takes down every notification this app has shown. There is nothing left
    // to tell the player: they are looking at the game, and the list of
    // matches says which are waiting on them.
    public static void ClearShownNotifications()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject manager = activity.Call<AndroidJavaObject>("getSystemService", "notification"))
            {
                if (manager != null)
                    manager.Call("cancelAll");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PUSH] Could not clear notifications: " + e.Message);
        }
#endif
    }

    // A token can arrive before anyone has signed in, so it is saved again once
    // someone does - and for whoever it is, if players swap on one phone.
    public static void OnSignedIn()
    {
        if (!string.IsNullOrEmpty(token))
            Save(token);
    }

    // The editor has no real Firebase Messaging: it hands out the word
    // "StubToken". Saving that overwrote the player's real phone address, so
    // signing in on the laptop quietly stopped their phone being reachable -
    // and nothing said so, because from the server's side there was a token,
    // it was simply not a real one.
    private const string EditorStubToken = "StubToken";

    private static void Save(string newToken)
    {
        if (string.IsNullOrEmpty(newToken))
            return;

        if (newToken == EditorStubToken)
        {
            Debug.Log("[PUSH] Running in the editor, which has no real " +
                      "notifications; leaving this account's phone address alone.");
            return;
        }

        token = newToken;

        var user = FirebaseInit.Auth != null ? FirebaseInit.Auth.CurrentUser : null;

        if (user == null || FirebaseInit.Database == null)
            return;

        FirebaseInit.Database.RootReference
            .Child("pushTokens").Child(user.UserId).Child("token")
            .SetValueAsync(newToken)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning("[PUSH] Saving the token failed: " + task.Exception);
                else
                    Debug.Log("[PUSH] Token saved for " + user.UserId + ".");
            });
    }
}
