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

    // The card's palette, kept here because the row wears it whoever built it.
    //
    // It used to be pushed in from the game-over card, which re-dressed the
    // rows when it noticed their number had changed. Both builders make their
    // rows after the panel is already up, and Unity destroys the old ones at
    // the end of the frame, so a second game of the same length ended with
    // the same count as the first - no change noticed, no dressing done, and
    // the prefab's own "PLAY" left on a card that says REVIEW everywhere
    // else. A row that dresses itself cannot be missed that way.
    private static readonly Color CardRowFace = new Color(1f, 1f, 1f, 0.055f);
    private static readonly Color CardRowInk = Color.white;
    private static readonly Color CardButtonFace = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color CardButtonInk = new Color(0.227f, 0.173f, 0.094f, 1f);

    // The rows sit on the game-over card, which is dark glass; the prefab was
    // coloured for the light blue panel they used to sit on. The card may
    // still say what it wants - it owns the look - but a row that is never
    // told comes out right anyway.
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
        // Dressed as it is filled: every row that exists has been through
        // here, whichever builder made it, so this is the one place that
        // cannot be skipped.
        DressForDarkCard(CardRowFace, CardRowInk, CardButtonFace, CardButtonInk);

        if (roundText != null)
            roundText.text = rowText;

        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();
            replayButton.onClick.AddListener(() => replayAction?.Invoke());
        }
    }
}