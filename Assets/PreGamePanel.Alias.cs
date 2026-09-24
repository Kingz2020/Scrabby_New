using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Choosing what other players call you.
//
// Signing in with Google hands over the name on the Google account, which is
// usually somebody's real name and rarely what they want over a scoreboard.
// Signing up by email asks for a name once and then never again. So the
// signed-in card carries a box with the current name in it and a button to
// change it.
//
// The name lives in two places and both are written: the Firebase account, so
// it survives a reinstall, and the player's profile in the database, which is
// what the rest of the game reads. Games already played keep the name they
// were played under - those are their records, not this one's.
public partial class PreGamePanel
{
    private TMP_InputField aliasInput;
    private Button aliasButton;

    private const int AliasShortest = 2;
    private const int AliasLongest = 16;

    private void AddAliasControls()
    {
        Button logout = FindLogoutButton();

        if (logout == null || displayNameInput == null)
            return;

        RectTransform anchor = logout.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        if (anchor.parent.Find("AliasInput") != null)
            return;

        // One row under Log out, not two: the card is full, and a box with a
        // button beside it takes the height of a single button.
        float width = anchor.sizeDelta.x;
        float height = anchor.sizeDelta.y;
        float rowY = anchor.anchoredPosition.y - (height + 14f);

        float boxWidth = width * 0.60f;
        float buttonWidth = width * 0.36f;
        float gap = width - boxWidth - buttonWidth;

        // The box is a copy of the one the sign-up card already has, so it
        // matches without rebuilding an input field from parts.
        GameObject box = Instantiate(displayNameInput.gameObject, anchor.parent);
        box.name = "AliasInput";
        box.SetActive(true);

        aliasInput = box.GetComponent<TMP_InputField>();
        aliasInput.characterLimit = AliasLongest;

        TMP_Text placeholder = aliasInput.placeholder as TMP_Text;

        // Empty, with the prompt in grey: the name they have is already on
        // the line above, and printing it twice reads as a mistake.
        if (placeholder != null)
            placeholder.text = "New player name";

        RectTransform boxRect = box.GetComponent<RectTransform>();
        boxRect.anchorMin = anchor.anchorMin;
        boxRect.anchorMax = anchor.anchorMax;
        boxRect.pivot = anchor.pivot;
        boxRect.sizeDelta = new Vector2(boxWidth, height);
        boxRect.anchoredPosition = new Vector2(
            anchor.anchoredPosition.x - (width - boxWidth) / 2f, rowY);

        // And the button beside it.
        GameObject go = new GameObject("AliasButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(buttonWidth, height);
        rect.anchoredPosition = new Vector2(
            anchor.anchoredPosition.x + (width - buttonWidth) / 2f, rowY);

        Image face = go.GetComponent<Image>();
        Image logoutFace = logout.GetComponent<Image>();

        if (logoutFace != null)
        {
            face.sprite = logoutFace.sprite;
            face.type = logoutFace.type;
            face.color = logoutFace.color;
        }
        else
        {
            face.color = new Color(0.945f, 0.878f, 0.733f, 1f);
        }

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Save";
        label.fontSize = 30f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = LogoutInkColour(logout);

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        aliasButton = go.GetComponent<Button>();
        aliasButton.targetGraphic = face;
        aliasButton.onClick.AddListener(OnChangeNamePressed);

        ChunkyButton.Deepen(aliasButton);

        // "Delete my account" was put directly under Log out before this row
        // existed; it moves down so the two do not share a line.
        Transform link = anchor.parent.Find("Delete my account");

        if (link != null)
        {
            RectTransform linkRect = link as RectTransform;

            if (linkRect != null)
            {
                linkRect.anchoredPosition = new Vector2(
                    linkRect.anchoredPosition.x, rowY - (height / 2f) - 38f);
            }
        }

        // Unused, but it keeps the gap in one place if the row is ever
        // rearranged.
        if (gap < 0f)
            Debug.LogWarning("[ALIAS] The name row is wider than the card.");
    }

    private static Color LogoutInkColour(Button logout)
    {
        TextMeshProUGUI theirs = logout.GetComponentInChildren<TextMeshProUGUI>(true);

        return theirs != null ? theirs.color : new Color(0.227f, 0.173f, 0.094f, 1f);
    }

    // Whatever they are called now, so changing it starts from the truth
    // rather than from an empty box.
    public void ShowCurrentAlias()
    {
        if (aliasInput == null || auth == null || auth.CurrentUser == null)
            return;

        // Left empty on purpose: "Signed in as: Zia" is the line above, and a
        // box already holding Zia invites them to change nothing.
        aliasInput.SetTextWithoutNotify("");
        aliasInput.ForceLabelUpdate();
    }

    private void OnChangeNamePressed()
    {
        if (auth == null || auth.CurrentUser == null)
        {
            SetStatus("Sign in first.");
            return;
        }

        string wanted = aliasInput != null ? aliasInput.text.Trim() : "";

        if (wanted.Length < AliasShortest)
        {
            SetStatus("A name needs at least " + AliasShortest + " letters.");
            return;
        }

        if (wanted.Length > AliasLongest)
            wanted = wanted.Substring(0, AliasLongest);

        // An address as a name would put somebody's email over the board for
        // every opponent to read.
        if (wanted.Contains("@"))
        {
            SetStatus("A name cannot be an email address.");
            return;
        }

        if (wanted == auth.CurrentUser.DisplayName)
        {
            SetStatus("That is already your name.");
            return;
        }

        if (aliasButton != null)
            aliasButton.interactable = false;

        SetStatus("Changing your name...");

        FirebaseUser user = auth.CurrentUser;
        Firebase.Auth.UserProfile profile =
            new Firebase.Auth.UserProfile { DisplayName = wanted };

        user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                if (aliasButton != null)
                    aliasButton.interactable = true;

                SetStatus("Could not change your name: " +
                          (task.Exception != null
                              ? task.Exception.GetBaseException().Message
                              : "cancelled"));
                return;
            }

            // The account has it; now the profile the rest of the game reads.
            if (EnsureFirebaseReady())
            {
                dbRoot.Child("users").Child(user.UserId).Child("displayName")
                      .SetValueAsync(wanted);
            }

            if (aliasButton != null)
                aliasButton.interactable = true;

            if (signedInAsText != null)
                signedInAsText.text = "Signed in as: " + wanted;

            SetStatus("You are " + wanted + " from now on.");

            RefreshUI();
        });
    }
}
