using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundReplayRow : MonoBehaviour
{
    [SerializeField] private Button replayButton;
    [SerializeField] private TextMeshProUGUI roundText;

    // The button that plays the round back - what the tutorial points at.
    public Button ReplayButton
    {
        get { return replayButton; }
    }

    // The rows moved onto the game-over card, which is dark glass; the prefab
    // was coloured for the light blue panel they used to sit on. Told what to
    // wear rather than deciding, so the card owns the palette.
    public void DressForDarkCard(Color rowFace, Color textColour,
                                 Color buttonFace, Color buttonInk)
    {
        Image face = GetComponent<Image>();

        if (face != null)
            face.color = rowFace;

        // The list is a vertical layout that decides each row's height, so
        // taller text needs a taller row asking for the space.
        LayoutElement size = GetComponent<LayoutElement>();

        if (size == null)
            size = gameObject.AddComponent<LayoutElement>();

        size.minHeight = 92f;
        size.preferredHeight = 92f;

        if (roundText != null)
        {
            roundText.color = textColour;

            // The line people actually read: which word won the round, and by
            // how much.
            //
            // It shrinks itself to fit rather than wrapping, so setting the
            // size alone changed nothing - the range it is allowed to shrink
            // within is what decides. Raised here, and it only comes down
            // from 46 when a round has two long words in it.
            roundText.enableAutoSizing = true;
            roundText.fontSizeMin = 28f;
            roundText.fontSizeMax = 40f;
            roundText.enableWordWrapping = false;
        }

        if (replayButton != null)
        {
            Image buttonImage = replayButton.GetComponent<Image>();

            if (buttonImage != null && buttonImage.sprite == null)
                buttonImage.color = buttonFace;

            // "REVIEW", not "PLAY": on a card full of finished rounds, Play
            // reads as "play another", and the button watches one back.
            foreach (TextMeshProUGUI label in
                     replayButton.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.color = buttonInk;
                label.text = "REVIEW";
                label.enableAutoSizing = true;
                label.fontSizeMin = 22f;
                label.fontSizeMax = 32f;
                label.enableWordWrapping = false;
            }

            // Wide enough for the longer word.
            RectTransform rect = replayButton.transform as RectTransform;

            if (rect != null)
            {
                rect.sizeDelta = new Vector2(168f, 66f);
                rect.anchoredPosition = new Vector2(-94f, 0f);
            }

            ChunkyButton.Deepen(replayButton);
        }
    }

    // The row only needs a label and something to run; which game mode produced
    // it, and what it replays, are the caller's business.
    public void Setup(string rowText, System.Action replayAction)
    {
        if (roundText != null)
            roundText.text = rowText;

        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();
            replayButton.onClick.AddListener(() => replayAction?.Invoke());
        }
    }
}