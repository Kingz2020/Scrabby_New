using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "What should people call you?", asked once.
//
// Signing in with Google hands over the name on the account, which is usually
// somebody's real name. Left alone it would sit over the board for every
// opponent, and in the matches list, and on their progress card - a thing
// people only notice once it is too late to mind quietly.
//
// So a new player is asked at the moment it matters, with their real name
// already in the box: keep it with one tap, or type something else. It is
// asked once, on the first sign-in, and never again; afterwards the name is
// changed on the signed-in card (PreGamePanel.Alias.cs).
public partial class PreGamePanel
{
    private const string PanelName = "ChooseANamePanel";

    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.70f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color CardInk = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.75f);

    private void AskForANameIfNew(FirebaseUser user)
    {
        if (user == null || !EnsureFirebaseReady())
            return;

        string uid = user.UserId;

        // New here means "has no profile in the database yet". Somebody who
        // has played before keeps whatever they chose then.
        dbRoot.Child("users").Child(uid).Child("chosenName").GetValueAsync()
              .ContinueWithOnMainThread(task =>
        {
            bool alreadyChosen = !task.IsFaulted && !task.IsCanceled &&
                                 task.Result != null && task.Result.Exists;

            if (alreadyChosen)
                return;

            AskForAName(user.DisplayName);
        });
    }

    private void AskForAName(string suggestion)
    {
        if (GameObject.Find(PanelName) != null)
            return;

        Canvas canvas = FindTopCanvas();

        if (canvas == null)
            return;

        GameObject root = new GameObject(PanelName,
            typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        root.GetComponent<Image>().color = Dim;

        // Eats taps, so nothing behind is pressed by accident.
        root.GetComponent<Button>().transition = Selectable.Transition.None;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(root.transform, false);

        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(840f, 470f);
        card.GetComponent<Image>().color = Glass;

        CardText(card.transform, "What should people call you?", 46f, Color.white,
                 FontStyles.Bold, 0f, -50f, 740f, 70f);

        CardText(card.transform,
                 "This is the name your opponents see. You can change it later.",
                 28f, Faint, FontStyles.Normal, 0f, -132f, 740f, 70f);

        // The box, copied from the sign-up card so it matches everything else.
        TMP_InputField box = null;

        if (displayNameInput != null)
        {
            GameObject copy = Instantiate(displayNameInput.gameObject, card.transform);
            copy.name = "NameBox";
            copy.SetActive(true);

            box = copy.GetComponent<TMP_InputField>();
            box.characterLimit = AliasLongest;

            TMP_Text hint = box.placeholder as TMP_Text;

            if (hint != null)
                hint.text = "Your player name";

            RectTransform boxRect = copy.GetComponent<RectTransform>();
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 1f);
            boxRect.sizeDelta = new Vector2(700f, 92f);
            boxRect.anchoredPosition = new Vector2(0f, -220f);

            string first = FirstNameOf(suggestion);

            box.SetTextWithoutNotify(first);
            box.ForceLabelUpdate();
        }

        // And the way out.
        GameObject go = new GameObject("ThatsMe",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(card.transform, false);

        RectTransform buttonRect = go.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.sizeDelta = new Vector2(700f, 96f);
        buttonRect.anchoredPosition = new Vector2(0f, 46f);

        go.GetComponent<Image>().color = Cream;

        CardText(go.transform, "That's me", 34f, CardInk, FontStyles.Bold,
                 0f, 0f, 700f, 96f, true);

        Button done = go.GetComponent<Button>();
        done.targetGraphic = go.GetComponent<Image>();

        TMP_InputField chosen = box;

        done.onClick.AddListener(delegate { KeepTheName(chosen, root); });

        ChunkyButton.Deepen(done);
    }

    // "Kingsley Obeng" offered as "Kingsley": a first name is a name, a full
    // name is a document.
    private static string FirstNameOf(string full)
    {
        if (string.IsNullOrWhiteSpace(full))
            return "";

        string[] parts = full.Trim().Split(' ');
        string first = parts[0].Trim();

        return first.Length > AliasLongest ? first.Substring(0, AliasLongest) : first;
    }

    private void KeepTheName(TMP_InputField box, GameObject panel)
    {
        string wanted = box != null ? box.text.Trim() : "";

        if (wanted.Length < AliasShortest || wanted.Contains("@"))
        {
            // Said on the card itself, since the panel covers the status line.
            CardText(panel.transform.Find("Card"), "A name of two letters or more, please.",
                     26f, new Color(1f, 0.72f, 0.36f, 1f), FontStyles.Normal,
                     0f, -330f, 700f, 40f);
            return;
        }

        if (wanted.Length > AliasLongest)
            wanted = wanted.Substring(0, AliasLongest);

        FirebaseUser user = auth != null ? auth.CurrentUser : null;

        if (user == null)
        {
            Destroy(panel);
            return;
        }

        Firebase.Auth.UserProfile profile =
            new Firebase.Auth.UserProfile { DisplayName = wanted };

        user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
        {
            if (EnsureFirebaseReady())
            {
                dbRoot.Child("users").Child(user.UserId).Child("displayName")
                      .SetValueAsync(wanted);

                // Remembered, so the question is asked once and not at every
                // sign-in on every phone they own.
                dbRoot.Child("users").Child(user.UserId).Child("chosenName")
                      .SetValueAsync(true);
            }

            if (signedInAsText != null)
                signedInAsText.text = "Signed in as: " + wanted;

            SetStatus("Welcome, " + wanted + ".");

            RefreshUI();
            Destroy(panel);
        });
    }

    private static Canvas FindTopCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas &&
                (best == null || canvas.sortingOrder >= best.sortingOrder))
            {
                best = canvas;
            }
        }

        return best;
    }

    private static TextMeshProUGUI CardText(
        Transform parent, string content, float size, Color colour,
        FontStyles style, float x, float y, float width, float height,
        bool fill = false)
    {
        if (parent == null)
            return null;

        GameObject go = new GameObject("Text", typeof(RectTransform),
                                       typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        RectTransform rect = go.GetComponent<RectTransform>();

        if (fill)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        return text;
    }
}
