using System;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Looking after an account rather than playing: a forgotten password, and
// leaving for good.
//
// Both are required rather than nice: a player who forgets their password has
// no way back in otherwise, and Google will not publish an app that lets
// people create an account but not delete it.
//
// The two links are built in code and hung off buttons already in the scene -
// "Forgot password?" under Sign in, "Delete my account" beside Log out - so
// the sign-in card does not have to be rebuilt to reach them.
public partial class PreGamePanel
{
    private const string FormerPlayerName = "Former player";

    // The same navy edge as the main menu's links.
    private static readonly Color LinkEdge = new Color(0.039f, 0.149f, 0.267f, 0.88f);

    private void BuildAccountLinks()
    {
        AddLink(signInActionButton, "Forgot password?", 44f,
                Color.white, OnForgotPasswordPressed);

        Button logout = FindLogoutButton();

        // White like the other links. It was a pale red, meant as a warning,
        // and on the card's glass it could not be read at all; the panel it
        // opens is where the warning is.
        AddLink(logout, "Delete my account", 68f,
                Color.white, OnDeleteAccountPressed);

        // The scene still carries the names the buttons were given when they
        // were made. Only a caption that is plainly one of those is replaced.
        Rename(signInActionButton, "Sign in");
        Rename(createAccountActionButton, "Create account");
        Rename(logout, "Log out");
    }

    private static void Rename(Button button, string caption)
    {
        if (button == null)
            return;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null && label.text.Trim().EndsWith("Button"))
            label.text = caption;
    }

    // The Log Out button is wired in the scene, not held in a field. It is
    // found the same way the daily locks the new-game buttons: by what its
    // click actually calls.
    private Button FindLogoutButton()
    {
        Transform root = signedInRoot != null ? signedInRoot.transform : transform;

        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == "OnLogoutPressed")
                    return button;
            }
        }

        return null;
    }

    private void AddLink(Button anchorButton, string caption, float gap,
                         Color colour, Action onClick)
    {
        if (anchorButton == null)
        {
            Debug.LogWarning("[ACCOUNT] No button to hang \"" + caption + "\" under.");
            return;
        }

        RectTransform anchor = anchorButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        GameObject go = new GameObject(caption,
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = caption;
        label.fontSize = 30f;
        label.color = colour;
        label.outlineColor = LinkEdge;
        label.outlineWidth = 0.18f;
        label.fontStyle = FontStyles.Underline | FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(anchor.sizeDelta.x, 40f);

        // Measured off the button rather than placed at a guessed height: this
        // panel has been rearranged more than once.
        rect.anchoredPosition = anchor.anchoredPosition +
            new Vector2(0f, -(anchor.sizeDelta.y * 0.5f + gap));

        Button button = go.GetComponent<Button>();
        button.targetGraphic = label;
        button.onClick.AddListener(() => onClick());
    }

    // ------------------------------------------------------ forgotten password
    public void OnForgotPasswordPressed()
    {
        if (auth == null)
        {
            SetStatus("Not connected yet - try again in a moment.");
            return;
        }

        string email = emailInput != null ? emailInput.text.Trim() : "";

        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            SetStatus("Type your email address first, then tap it again.");
            return;
        }

        SetStatus("Sending a reset link...");

        auth.SendPasswordResetEmailAsync(email).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogWarning("[ACCOUNT] Password reset failed: " + task.Exception);

                // Whether an address has an account is not something to tell
                // whoever is holding the phone, so the answer reads the same
                // either way.
                SetStatus("If that address has an account, a reset link is on its way.");
                return;
            }

            SetStatus("Check your email for the reset link, then sign in.");
        });
    }

    // --------------------------------------------------------- deleting it all
    public void OnDeleteAccountPressed()
    {
        if (auth == null || auth.CurrentUser == null)
        {
            SetStatus("Sign in first.");
            return;
        }

        DeleteAccountPanel.Show(auth.CurrentUser.Email, DeleteAccount);
    }

    private void DeleteAccount(string password)
    {
        FirebaseUser signedIn = auth != null ? auth.CurrentUser : null;

        if (signedIn == null)
        {
            DeleteAccountPanel.SetStatus("You are no longer signed in.", true);
            return;
        }

        string uid = signedIn.UserId;
        string email = signedIn.Email;

        DeleteAccountPanel.SetBusy(true);
        DeleteAccountPanel.SetStatus("Checking your password...");

        Credential credential = EmailAuthProvider.GetCredential(email, password);

        signedIn.ReauthenticateAsync(credential).ContinueWithOnMainThread(authTask =>
        {
            if (authTask.IsCanceled || authTask.IsFaulted)
            {
                Debug.LogWarning("[ACCOUNT] Re-authentication failed: " + authTask.Exception);
                DeleteAccountPanel.SetBusy(false);
                DeleteAccountPanel.SetStatus("That password was not right.", true);
                return;
            }

            DeleteAccountPanel.SetStatus("Deleting your data...");

            // The player's own data goes while they are still signed in: once
            // the account is gone, the database will not accept writes from
            // them at all.
            EraseUserData(uid, () => DeleteTheUser(uid));
        });
    }

    private void EraseUserData(string uid, Action onDone)
    {
        if (dbRoot == null)
        {
            onDone();
            return;
        }

        // Their name has to come off the games they played, but the games
        // themselves belong to the other player as much as to them.
        dbRoot.Child("users").Child(uid).Child("activeMatchIds")
              .GetValueAsync().ContinueWithOnMainThread(task =>
        {
            List<string> matchIds = new List<string>();

            if (!task.IsFaulted && !task.IsCanceled && task.Result != null)
            {
                foreach (var child in task.Result.Children)
                {
                    string matchId = child.Value != null ? child.Value.ToString() : "";

                    if (!string.IsNullOrEmpty(matchId))
                        matchIds.Add(matchId);
                }
            }

            foreach (string matchId in matchIds)
                LeaveMatchBehind(matchId, uid);

            StopWatchingRoom();

            if (Singleton.Instance != null && Singleton.Instance.OnlineMatchController != null)
                Singleton.Instance.OnlineMatchController.StopWatchingCurrentMatch();

            // An open quick game of theirs would otherwise sit in the queue
            // waiting for an opponent who no longer exists.
            dbRoot.Child("quickQueue").Child("waiting").GetValueAsync()
                  .ContinueWithOnMainThread(queueTask =>
            {
                if (!queueTask.IsFaulted && !queueTask.IsCanceled &&
                    queueTask.Result != null && queueTask.Result.Exists)
                {
                    object waitingUid = queueTask.Result.Child("uid").Value;

                    if (waitingUid != null && waitingUid.ToString() == uid)
                        dbRoot.Child("quickQueue").Child("waiting").RemoveValueAsync();
                }

                dbRoot.Child("pushTokens").Child(uid).RemoveValueAsync();

                dbRoot.Child("users").Child(uid).RemoveValueAsync()
                      .ContinueWithOnMainThread(removeTask =>
                {
                    if (removeTask.IsFaulted)
                        Debug.LogWarning("[ACCOUNT] Removing the profile failed: " +
                                         removeTask.Exception);

                    onDone();
                });
            });
        });
    }

    // The other player keeps the game, without the name of someone who is no
    // longer there, and sees it as over rather than waiting on a move that is
    // never coming.
    private void LeaveMatchBehind(string matchId, string uid)
    {
        dbRoot.Child("matches").Child(matchId).GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists)
                return;

            object player1 = task.Result.Child("player1Uid").Value;
            bool amPlayer1 = player1 != null && player1.ToString() == uid;

            Dictionary<string, object> changes = new Dictionary<string, object>
            {
                { amPlayer1 ? "player1DisplayName" : "player2DisplayName", FormerPlayerName },
                { "status", "completed" }
            };

            dbRoot.Child("matches").Child(matchId).UpdateChildrenAsync(changes)
                  .ContinueWithOnMainThread(updateTask =>
            {
                if (updateTask.IsFaulted)
                    Debug.LogWarning("[ACCOUNT] Could not take the name off match " +
                                     matchId + ": " + updateTask.Exception);
            });
        });
    }

    private void DeleteTheUser(string uid)
    {
        FirebaseUser signedIn = auth != null ? auth.CurrentUser : null;

        if (signedIn == null)
        {
            DeleteAccountPanel.Close();
            RefreshUI();
            return;
        }

        DeleteAccountPanel.SetStatus("Deleting your account...");

        signedIn.DeleteAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogError("[ACCOUNT] Deleting the account failed: " + task.Exception);
                DeleteAccountPanel.SetBusy(false);
                DeleteAccountPanel.SetStatus(
                    "Your data is deleted, but the sign-in could not be removed. " +
                    "Try again, or write to us.", true);
                return;
            }

            Debug.Log("[ACCOUNT] Account " + uid + " deleted.");

            DeleteAccountPanel.Close();

            // Everything local goes too, so the next person to open the game on
            // this phone starts clean.
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            creatingAccount = false;

            if (passwordInput != null)
            {
                passwordInput.SetTextWithoutNotify("");
                passwordInput.ForceLabelUpdate();
            }

            if (emailInput != null)
            {
                emailInput.SetTextWithoutNotify("");
                emailInput.ForceLabelUpdate();
            }

            SetStatus("Your account has been deleted.");
            RefreshUI();
            RefreshStartButton();
        });
    }
}
