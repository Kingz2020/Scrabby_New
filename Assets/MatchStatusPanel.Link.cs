using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Invite by link", beside the invitation by email.
//
// Inviting by email assumes you know your friend's address. Most people are
// reached by phone number - a WhatsApp message - so this makes a room and
// hands over a link to send however they like. The link is copied to the
// clipboard as well as offered to the share sheet, so it can be pasted
// anywhere the sheet does not reach.
//
// Built in code, like the other additions to this panel: the scene can only
// be edited with Unity closed.
public partial class MatchStatusPanel
{
    private Button linkInviteButton;

    private void AddInviteByLinkButton()
    {
        if (inviteButton == null)
            return;

        RectTransform anchor = inviteButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        if (anchor.parent.Find("InviteByLinkButton") != null)
            return;

        GameObject go = new GameObject("InviteByLinkButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = anchor.sizeDelta;

        // Directly under "Send invite", which is the other way of asking
        // somebody to play.
        rect.anchoredPosition = anchor.anchoredPosition +
            new Vector2(0f, -(anchor.sizeDelta.y + 16f));

        Image face = go.GetComponent<Image>();
        Image anchorFace = inviteButton.GetComponent<Image>();

        if (anchorFace != null)
        {
            face.sprite = anchorFace.sprite;
            face.type = anchorFace.type;
            face.color = anchorFace.color;
        }
        else
        {
            face.color = new Color(0.945f, 0.878f, 0.733f, 1f);
        }

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Invite by link";
        label.fontSize = 34f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = InviteLabelColour();

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        linkInviteButton = go.GetComponent<Button>();
        linkInviteButton.targetGraphic = face;
        linkInviteButton.onClick.AddListener(OnInviteByLinkPressed);

        ChunkyButton.Deepen(linkInviteButton);
    }

    // The same ink as the button it sits under, so the two read as a pair.
    private Color InviteLabelColour()
    {
        if (inviteButton != null)
        {
            TextMeshProUGUI theirs = inviteButton.GetComponentInChildren<TextMeshProUGUI>(true);

            if (theirs != null)
                return theirs.color;
        }

        return new Color(0.227f, 0.173f, 0.094f, 1f);
    }

    private void OnInviteByLinkPressed()
    {
        if (preGamePanel == null)
        {
            ShowStatus("Not ready yet - try again in a moment.");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            ShowStatus("Sign in first.");
            return;
        }

        ShowStatus("Making a game to share...");

        // Creating the room is what produces the code, and the code is the
        // link. Sharing happens as soon as the room exists (PreGamePanel).
        preGamePanel.OnCreateRoomPressed();
    }
}
