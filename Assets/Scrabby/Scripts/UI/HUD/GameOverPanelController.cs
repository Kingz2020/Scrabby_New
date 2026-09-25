using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Decides which ways out of a finished game are offered.
//
// Main Menu is always there - a game that has ended should never leave a
// player with nowhere to go but another game. Back to Matches only makes sense
// when the game that just ended was an online one, so it is shown then and
// hidden otherwise rather than offering to return to a match that does not
// exist.
//
// And "Your progress", which is the question a result raises: that was one
// game, so how does it sit with the rest? It is added in code because the row
// of buttons along the bottom is already full and has been rearranged more
// than once.
public class GameOverPanelController : MonoBehaviour
{
    [SerializeField] private GameObject backToMatchesButton;

    private GameObject removeLink;

    private void OnEnable()
    {
        AddStatsLink();
        AddRemoveGameLink();

        // The link is added first, because the card collects it along with
        // everything else the panel already had.
        GameOverCard.DressPanel(gameObject);

        Refresh();

        // If the game that just ended was the one the walkthrough started,
        // there is one thing left to show: the replay rows, and what they are
        // for.
        TutorialFlow.ShowReplayHintIfOwed();
    }

    // Public so whatever shows this panel can ask for a second look: the panel
    // is sometimes enabled before the match result it is about has been set.
    public void Refresh()
    {
        if (backToMatchesButton != null)
            backToMatchesButton.SetActive(ShowingAnOnlineResult());

        RefreshRemoveLink();
    }

    // The result is sometimes put up before the match behind it has arrived,
    // so what the panel offers is checked as it goes rather than once.
    private void Update()
    {
        RefreshRemoveLink();
    }

    private void RefreshRemoveLink()
    {
        if (removeLink == null)
            return;

        bool offer = Singleton.Instance != null &&
                     Singleton.Instance.OnlineMatchController != null &&
                     Singleton.Instance.OnlineMatchController.CanRemoveCurrentMatch();

        if (removeLink.activeSelf != offer)
            removeLink.SetActive(offer);
    }

    private bool AlreadyThere(string name)
    {
        return FindAnywhere(name) != null;
    }

    private static bool ShowingAnOnlineResult()
    {
        if (Singleton.Instance == null)
            return false;

        OnlineMatchController online = Singleton.Instance.OnlineMatchController;

        return online != null && online.IsViewingOnlineMatchResult();
    }

    private void AddStatsLink()
    {
        // Looked for anywhere under the panel, not just among its own
        // children: the card moves this link onto itself, and a guard that
        // only checked one level stopped finding it and added another one
        // every time the panel opened.
        if (AlreadyThere("StatsLink"))
            return;

        GameObject go = new GameObject("StatsLink",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(transform, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = "Your progress";
        label.fontSize = 30f;
        label.color = new Color(0.945f, 0.878f, 0.733f, 0.95f);
        label.fontStyle = FontStyles.Underline;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400f, 52f);

        // Between the round-by-round rows and the row of buttons.
        rect.anchoredPosition = new Vector2(0f, -535f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = label;
        button.onClick.AddListener(ShowStats);
    }

    // A finished game could be looked at for ever and never put away. This
    // is the only place it can be: on the result itself, which is what the
    // finished list opens. It takes the game off this player's list alone -
    // the other player keeps theirs - so it is not offered on a solo game or
    // on a result that is being seen for the first time straight off the
    // board, only on one opened from the finished games.
    private void AddRemoveGameLink()
    {
        Transform found = FindAnywhere("RemoveGameLink");

        if (found != null)
        {
            removeLink = found.gameObject;
            return;
        }

        GameObject go = new GameObject("RemoveGameLink",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(transform, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = "Remove this game";
        label.fontSize = 28f;
        label.color = new Color(1f, 0.72f, 0.66f, 0.95f);
        label.fontStyle = FontStyles.Underline;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400f, 48f);
        rect.anchoredPosition = new Vector2(0f, -590f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = label;
        button.onClick.AddListener(RemoveThisGame);

        removeLink = go;
        removeLink.SetActive(false);
    }

    private Transform FindAnywhere(string name)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    private void RemoveThisGame()
    {
        if (Singleton.Instance != null && Singleton.Instance.OnlineMatchController != null)
            Singleton.Instance.OnlineMatchController.AskToRemoveCurrentMatch();
    }

    // The chart opens on the row this result belongs to - the level just
    // played, or the friend it was against.
    private void ShowStats()
    {
        string key = null;

        if (Singleton.Instance != null)
        {
            if (ShowingAnOnlineResult())
            {
                key = Singleton.Instance.OnlineMatchController.CurrentOpponentStatsKey();
            }
            else if (Singleton.Instance.GameLogic != null)
            {
                key = PlayerStats.SoloKey(
                    Singleton.Instance.GameLogic.CurrentSoloDifficulty);
            }
        }

        StatsPanel.Show(key);
    }
}
