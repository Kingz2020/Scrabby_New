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

    private void OnEnable()
    {
        AddStatsLink();
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

    private void AddStatsLink()
    {
        if (transform.Find("StatsLink") != null)
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
