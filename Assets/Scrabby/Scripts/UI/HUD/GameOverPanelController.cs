using UnityEngine;

// Decides which ways out of a finished game are offered.
//
// Main Menu is always there - a game that has ended should never leave a
// player with nowhere to go but another game. Back to Matches only makes sense
// when the game that just ended was an online one, so it is shown then and
// hidden otherwise rather than offering to return to a match that does not
// exist.
public class GameOverPanelController : MonoBehaviour
{
    [SerializeField] private GameObject backToMatchesButton;

    private void OnEnable()
    {
        Refresh();
    }

    // Public so whatever shows this panel can ask for a second look: the panel
    // is sometimes enabled before the match result it is about has been set.
    public void Refresh()
    {
        if (backToMatchesButton == null)
            return;

        backToMatchesButton.SetActive(ShowingAnOnlineResult());
    }

    private static bool ShowingAnOnlineResult()
    {
        if (Singleton.Instance == null)
            return false;

        OnlineMatchController online = Singleton.Instance.OnlineMatchController;

        return online != null && online.IsViewingOnlineMatchResult();
    }
}
