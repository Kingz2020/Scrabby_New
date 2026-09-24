using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What the card offers before anybody is signed in.
//
// Almost everyone arriving has a Google account on the phone already, and
// almost nobody wants to type an address and invent a password to play a word
// game. So the card leads with one button, and the email form waits behind a
// link for the people who need it: anyone without Google, anyone on a shared
// phone, the account that already exists - and Google's own reviewers, who
// are given an email and a password in the Play Console.
public partial class PreGamePanel
{
    // False until somebody asks for it, and again whenever the card is
    // shown afresh.
    private bool emailFormShowing;
    private Button emailFormLink;

    private void AddEmailFormLink()
    {
        if (signInActionButton == null)
            return;

        RectTransform anchor = signInActionButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        if (anchor.parent.Find("EmailFormLink") != null)
            return;

        GameObject go = new GameObject("EmailFormLink",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = "or use an email address";
        label.fontSize = 30f;
        label.color = Color.white;
        label.outlineColor = new Color(0.039f, 0.149f, 0.267f, 0.88f);
        label.outlineWidth = 0.18f;
        label.fontStyle = FontStyles.Underline | FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(anchor.sizeDelta.x, 52f);

        emailFormLink = go.GetComponent<Button>();
        emailFormLink.targetGraphic = label;
        emailFormLink.onClick.AddListener(ShowEmailForm);

        ApplyWayIn();
    }

    private void ShowEmailForm()
    {
        emailFormShowing = true;
        ApplyWayIn();
        ApplyTab();
    }

    // Which of the two ways in is on screen. Called whenever the signed-out
    // card is shown, so coming back to it always starts at the simple offer.
    private void ApplyWayIn()
    {
        if (signInActionButton == null)
            return;

        RectTransform anchor = signInActionButton.transform as RectTransform;

        if (anchor == null)
            return;

        bool form = emailFormShowing;

        // The email form, all of it.
        SetVisible(emailInput, form);
        SetVisible(passwordInput, form);
        SetVisible(signInTabButton, form);
        SetVisible(createAccountTabButton, form);
        SetVisible(signInActionButton, form && !creatingAccount);
        SetVisible(createAccountActionButton, form && creatingAccount);
        SetVisible(displayNameInput, form && creatingAccount);

        Transform forgot = anchor.parent != null
            ? anchor.parent.Find("Forgot password?") : null;

        if (forgot != null)
            forgot.gameObject.SetActive(form);

        // The link offers the form, so it goes once the form is there.
        if (emailFormLink != null)
            emailFormLink.gameObject.SetActive(!form);

        PlaceWayIn(form, anchor);
    }

    // With the form hidden there is a card full of space, so Google's button
    // sits up where the tabs were, with the link under it. With the form
    // shown it drops back under the action button, as an alternative rather
    // than the headline.
    private void PlaceWayIn(bool form, RectTransform anchor)
    {
        Transform google = anchor.parent != null
            ? anchor.parent.Find("GoogleSignInButton") : null;

        if (google == null)
            return;

        RectTransform rect = google as RectTransform;

        if (rect == null)
            return;

        RectTransform tabs = signInTabButton != null
            ? signInTabButton.transform as RectTransform : null;

        if (form || tabs == null)
        {
            rect.anchoredPosition = anchor.anchoredPosition +
                new Vector2(0f, -(anchor.sizeDelta.y + 16f));
            return;
        }

        // Where the tabs would have been, across the width of the card.
        rect.anchoredPosition = new Vector2(
            anchor.anchoredPosition.x,
            tabs.anchoredPosition.y - anchor.sizeDelta.y / 2f);

        if (emailFormLink != null)
        {
            RectTransform linkRect = emailFormLink.transform as RectTransform;

            if (linkRect != null)
            {
                linkRect.anchoredPosition = new Vector2(
                    rect.anchoredPosition.x,
                    rect.anchoredPosition.y - anchor.sizeDelta.y / 2f - 44f);
            }
        }
    }
}
