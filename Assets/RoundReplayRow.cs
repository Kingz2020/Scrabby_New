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

        if (roundText != null)
            roundText.color = textColour;

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
                label.enableWordWrapping = false;
            }

            // A little wider than it was, because the word is longer; the row
            // keeps the size it had otherwise.
            RectTransform rect = replayButton.transform as RectTransform;

            if (rect != null)
            {
                rect.sizeDelta = new Vector2(132f, 60f);
                rect.anchoredPosition = new Vector2(-72f, 0f);
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