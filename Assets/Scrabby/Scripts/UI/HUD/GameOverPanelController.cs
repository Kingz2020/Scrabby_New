using UnityEngine;

// Opens the result board, and gets out of its way.
//
// This used to decide what a finished game should offer - adding a progress
// link, adding a way to remove the game, switching "Back to matches" on by
// asking the online controller what it was looking at. Those decisions now
// arrive with the result itself, on its ResultSheet, so there is nothing
// here to get out of step: the panel puts the board up and the board reads
// the sheet.
public class GameOverPanelController : MonoBehaviour
{
    // Kept because the scene still holds a reference to the panel's own
    // button. The board builds and places its own, so this one stays off.
    [SerializeField] private GameObject backToMatchesButton;

    private void OnEnable()
    {
        if (backToMatchesButton != null)
            backToMatchesButton.SetActive(false);

        GameOverCard.DressPanel(gameObject);

        // If the game that just ended was the one the walkthrough started,
        // there is one thing left to show: the replay rows, and what they
        // are for.
        TutorialFlow.ShowReplayHintIfOwed();
    }
}
