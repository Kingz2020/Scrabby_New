using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The way back from the sign-in card.
//
// There was none. Everything else that asks something of the player has a way
// out - the board, the matches list, the result - but the card you meet on the
// way to multiplayer could only be left by signing in, which is exactly the
// thing somebody who changed their mind, or has no signal, or cannot remember
// a password, is unable to do. They were left closing the app.
//
// Solo and the daily need no account at all, so this leads back to them.
public partial class PreGamePanel
{
    private void AddWayOutButton()
    {
        if (pregamePanelRoot == null && gameObject == null)
            return;

        Transform card = transform;

        if (card.Find("BackToMenuButton") != null)
            return;

        GameObject go = new GameObject("BackToMenuButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(card, false);

        // Bottom left, where a way back belongs, and clear of the cards in
        // the middle whichever one is showing.
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(340f, 88f);
        rect.anchoredPosition = new Vector2(-300f, 120f);

        Image face = go.GetComponent<Image>();
        face.color = new Color(0.039f, 0.149f, 0.267f, 0.88f);

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Main menu";
        label.fontSize = 32f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(BackToMainMenu);

        ChunkyButton.Deepen(button);
    }

    public void BackToMainMenu()
    {
        // Nothing is undone by leaving: an account half-typed is no account,
        // and anybody already signed in stays signed in for next time.
        OptionPanelController options =
            FindFirstObjectByType<OptionPanelController>(FindObjectsInactive.Include);

        if (options != null)
        {
            options.ShowOptionPanel();
            return;
        }

        // No menu to go back to - better to close the card than to trap them.
        if (pregamePanel != null)
            pregamePanel.SetActive(false);

        Debug.LogWarning("[PreGamePanel] No option panel found to go back to.");
    }
}
