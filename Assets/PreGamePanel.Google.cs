using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;

// Signing in with the Google account already on the phone.
//
// The email and password way stays exactly as it was, for anyone who has an
// account that way or would rather not use Google. This is the one-tap way
// beside it, and it is the one most people will take: no address to type, no
// password to invent, no password to forget.
//
// The one knot is that Firebase counts "google.com" and "password" as
// different ways of being the same person. Signing in with Google using an
// address that already has a password account gives a fresh, empty player
// unless the two are joined - so when Firebase says as much, the player is
// asked for their password once and the two are linked for good.
public partial class PreGamePanel
{
    private Button googleButton;

    // Built here rather than in the scene: the scene can only be edited with
    // Unity closed, and this button belongs beside two others that are
    // already arranged by code.
    private void AddGoogleButton()
    {
        // Built everywhere, not only on Android: in the editor it cannot sign
        // anybody in, but it has to be on screen to be laid out, and pressing
        // it there says so rather than doing nothing.
        if (signInActionButton == null)
            return;

        RectTransform anchor = signInActionButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        if (anchor.parent.Find("GoogleSignInButton") != null)
            return;

        GameObject go = new GameObject("GoogleSignInButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = anchor.sizeDelta;

        // Under whichever action button is showing - they share a slot - and
        // the forgotten-password link moves down to make room.
        rect.anchoredPosition = anchor.anchoredPosition +
            new Vector2(0f, -(anchor.sizeDelta.y + 16f));

        Transform forgot = anchor.parent.Find("Forgot password?");

        if (forgot != null)
        {
            RectTransform forgotRect = forgot as RectTransform;

            if (forgotRect != null)
            {
                forgotRect.anchoredPosition = new Vector2(
                    forgotRect.anchoredPosition.x,
                    rect.anchoredPosition.y - anchor.sizeDelta.y / 2f - 34f);
            }
        }

        Image face = go.GetComponent<Image>();
        face.color = Color.white;

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);

        TMPro.TextMeshProUGUI label = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
        label.text = "Continue with Google";
        label.fontSize = 32f;
        label.color = new Color(0.227f, 0.173f, 0.094f, 1f);
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.raycastTarget = false;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        googleButton = go.GetComponent<Button>();
        googleButton.targetGraphic = face;
        googleButton.onClick.AddListener(OnGoogleSignInPressed);

        ChunkyButton.Deepen(googleButton);
    }

    public void OnGoogleSignInPressed()
    {
        if (!GoogleSignInBridge.Available)
        {
            SetStatus("Signing in with Google only works on the phone.");
            return;
        }

        if (!EnsureFirebaseReady())
        {
            SetStatus("Not connected yet - try again in a moment.");
            return;
        }

        SetStatus("Opening your Google accounts...");

        if (googleButton != null)
            googleButton.interactable = false;

        GoogleSignInBridge.SignIn(SignInToFirebaseWith, GoogleSignInFailed);
    }

    private void GoogleSignInFailed(string message)
    {
        if (googleButton != null)
            googleButton.interactable = true;

        string said = message ?? "";

        // Play services says "cancelled" for two quite different things: the
        // player backing out of the account sheet, and its own refusal to
        // hand over a token for an account it wants re-checked. Only the
        // first is silent - the second looked exactly like a button that did
        // nothing at all.
        if (said.Contains("reauth"))
        {
            SetStatus("Google could not confirm that account on this phone. " +
                      "Sign in with your email and password instead.");
            return;
        }

        bool cancelled = said.IndexOf("cancel", System.StringComparison.OrdinalIgnoreCase) >= 0;

        SetStatus(cancelled
            ? "Google sign-in cancelled."
            : "Google sign-in did not work. Try your email and password.");
    }

    private void SignInToFirebaseWith(string idToken)
    {
        SetStatus("Signing in...");

        Credential credential = GoogleAuthProvider.GetCredential(idToken, null);

        auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
        {
            if (googleButton != null)
                googleButton.interactable = true;

            if (task.IsCanceled)
            {
                SetStatus("");
                return;
            }

            if (task.IsFaulted)
            {
                string reason = task.Exception != null
                    ? task.Exception.GetBaseException().Message
                    : "unknown";

                // The address already belongs to a password account: the two
                // are the same person and want joining, not replacing.
                if (reason.Contains("account-exists") ||
                    reason.Contains("different credential"))
                {
                    pendingGoogleCredential = credential;
                    SetStatus("This email already has a password. Type it once to join the two.");
                    return;
                }

                Debug.LogError("[GOOGLE] Firebase refused the token: " + reason);
                SetStatus("Google sign-in failed: " + reason);
                return;
            }

            // Whoever is signed in now is who the token was for; taken from
            // auth rather than the task, whose result type has changed
            // between Firebase versions.
            AfterGoogleSignIn(auth.CurrentUser);
        });
    }

    // Kept between the refusal above and the player typing their password.
    private Credential pendingGoogleCredential;

    // Called by the ordinary email-and-password sign-in once it succeeds, so
    // the Google account is joined to the player who just proved who they are.
    private void LinkGoogleIfOneIsWaiting()
    {
        if (pendingGoogleCredential == null || auth == null || auth.CurrentUser == null)
            return;

        Credential credential = pendingGoogleCredential;
        pendingGoogleCredential = null;

        auth.CurrentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[GOOGLE] Could not link the accounts: " +
                                 (task.Exception != null ? task.Exception.GetBaseException().Message : "cancelled"));
                return;
            }

            SetStatus("Google is now linked to this account.");
        });
    }

    private void AfterGoogleSignIn(FirebaseUser user)
    {
        if (user == null)
        {
            SetStatus("Signed in, but no account came back.");
            return;
        }

        // Google knows their name, so nobody has to invent one. Whatever the
        // rest of the game reads comes from the profile, which this writes if
        // it is the player's first time.
        RepairCurrentUserProfileIfMissing();

        string shownName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email
            : user.DisplayName;

        SetStatus("Signed in as " + shownName + ".");

        if (signedInAsText != null)
            signedInAsText.text = "Signed in as: " + shownName;

        RefreshUI();
        RefreshStartButton();

        // A Google account carries somebody's real name, and that name would
        // otherwise sit over the board for every opponent to read. So the
        // first time they arrive they are asked what to be called, with the
        // real name offered as the answer if they do not mind it.
        AskForANameIfNew(user);
    }
}
