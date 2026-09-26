using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

// What this player is called, and one place that knows it.
//
// The name a player chooses is written twice: onto the Firebase account, and
// into their own profile under users/{uid}/displayName. Everything that put
// a name on a match, a room or an invitation read it off the account - and
// signing in with Google refreshes the account profile from the Google
// account, which quietly put a real name back over a chosen one. The alias
// survived in the copy nobody read.
//
// So the profile copy is the one that counts. This reads it once per player
// per session, hands it to everything that needs a name, and pushes it back
// onto the account so the two cannot disagree again - whatever Google says
// the next time they sign in.
public static class PlayerName
{
    private const string Field = "displayName";

    private static string chosen;
    private static string chosenFor;
    private static bool asking;

    // The name to write on anything this player does. Safe to call at any
    // time: before the profile has been read it falls back to the account,
    // which is what every caller used to do on its own.
    public static string Of(FirebaseUser user)
    {
        if (user == null)
            return "";

        if (!string.IsNullOrWhiteSpace(chosen) && chosenFor == user.UserId)
            return chosen;

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;

        return user.Email;
    }

    // Read the chosen name and make the account agree with it. Called when
    // the player signs in, however they signed in.
    public static void Settle(DatabaseReference dbRoot, FirebaseUser user)
    {
        if (dbRoot == null || user == null || asking)
            return;

        // Once per player per session: the answer only changes when they
        // change it, and Remember is told when they do.
        if (chosenFor == user.UserId)
            return;

        asking = true;
        string uid = user.UserId;

        dbRoot.Child("users").Child(uid).Child(Field).GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            asking = false;

            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[NAME] Could not read the chosen name: " + task.Exception);
                return;
            }

            string stored = task.Result != null && task.Result.Exists && task.Result.Value != null
                ? task.Result.Value.ToString()
                : "";

            FirebaseUser me = user;

            if (string.IsNullOrWhiteSpace(stored))
            {
                // Nothing chosen yet, so whatever the account says becomes
                // the chosen name - a first Google sign-in lands here.
                string fromAccount = !string.IsNullOrWhiteSpace(me.DisplayName)
                    ? me.DisplayName
                    : me.Email;

                Remember(fromAccount, uid);

                if (!string.IsNullOrWhiteSpace(fromAccount))
                    dbRoot.Child("users").Child(uid).Child(Field).SetValueAsync(fromAccount);

                return;
            }

            Remember(stored, uid);

            // The account disagrees, which is what a Google sign-in leaves
            // behind. The chosen name wins.
            if (stored != me.DisplayName)
            {
                me.UpdateUserProfileAsync(new Firebase.Auth.UserProfile { DisplayName = stored })
                  .ContinueWithOnMainThread(write =>
                {
                    if (write.IsFaulted)
                    {
                        Debug.LogWarning("[NAME] Could not put the chosen name back on " +
                                         "the account: " + write.Exception);
                        return;
                    }

                    Debug.Log("[NAME] The account now agrees: " + stored + ".");
                });
            }
        });
    }

    // Told when the player changes their name, so nothing has to wait for a
    // read to come back before it is right.
    public static void Remember(string name, string uid)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        chosen = name.Trim();
        chosenFor = uid;
    }

    // On the way out: the next player to sign in on this phone is not this
    // one.
    public static void Forget()
    {
        chosen = null;
        chosenFor = null;
    }
}
