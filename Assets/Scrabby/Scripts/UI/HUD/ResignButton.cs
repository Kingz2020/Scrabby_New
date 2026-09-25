using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The way out of a game you no longer want to play.
//
// Walking away from an online match left it open for ever: the other player
// kept being told it was their move, and both lists kept a game nobody was
// playing. Main Menu leaves the match alone, which is right for stepping
// away for an hour and wrong for giving up, so this is the second way out
// and it says what it does.
//
// It lives beside Main Menu on the board, and only when there is a live
// online match to give up: not in a solo game, not in the walkthrough, and
// not while a finished round is being watched back.
public class ResignButton : MonoBehaviour
{
    private const string ButtonName = "ResignButton_Gameplay";

    private GameObject button;

    // Added to the board panel, which is what stays alive between games.
    public static void AddTo(GameObject gameplayPanel)
    {
        if (gameplayPanel != null && gameplayPanel.GetComponent<ResignButton>() == null)
            gameplayPanel.AddComponent<ResignButton>();
    }

    private void OnEnable()
    {
        Build();
    }

    private void Update()
    {
        if (button == null)
        {
            Build();
            return;
        }

        // The controller decides whether the match can be given up; the game
        // decides whether an online match is what is being played. Both,
        // because a match watched earlier can still be remembered while a
        // solo game is on the board.
        string why = Reason();
        bool offer = why == null;

        if (button.activeSelf != offer)
            button.SetActive(offer);

        // Said once, when it changes, so a key that does not appear says why
        // in the log instead of leaving it to be guessed at.
        if (why != lastReason)
        {
            lastReason = why;
            Debug.Log("[RESIGN] Key " + (offer ? "shown." : "hidden: " + why));
        }
    }

    private string lastReason = "start";

    private string Reason()
    {
        if (Singleton.Instance == null)
            return "no singleton";

        GameLogic logic = Singleton.Instance.GameLogic;

        if (logic == null)
            return "no game";

        // The daily and the walkthrough are asked about first, and by their
        // own flags. The mode the game was started in is not cleared when the
        // daily opens, so after an online match IsOnlineMatch is still true
        // while today's puzzle is on the board - which put the key on the
        // daily, under its "Back to my result". Neither is a game anybody can
        // resign: the way out of both is the menu.
        if (logic.IsDailyMode)
            return "the daily";

        if (logic.IsTutorialGame)
            return "the walkthrough";

        if (!logic.IsOnlineMatch)
            return "not an online game";

        if (Singleton.Instance.OnlineMatchController == null)
            return "no controller";

        return Singleton.Instance.OnlineMatchController.WhyNotResign();
    }

    private void Build()
    {
        Transform existing = transform.Find(ButtonName);

        if (existing != null)
        {
            button = existing.gameObject;
            return;
        }

        GameObject go = new GameObject(ButtonName,
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 84f);

        // Main Menu is on the left of the row, Back to match in the middle
        // during a replay; this is the right-hand end of the same row.
        rect.anchoredPosition = new Vector2(330f, -1020f);

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));

        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Resign";
        label.fontSize = 32f;
        label.alignment = TextAlignmentOptions.Center;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button pressed = go.GetComponent<Button>();
        pressed.targetGraphic = go.GetComponent<Image>();
        pressed.onClick.AddListener(Ask);

        // Blue, like the other ways off the board: white is for the buttons
        // that play the game.
        ChunkyButton.Dress(pressed, ChunkyButton.Face.Blue, "Resign", null);

        button = go;
        button.SetActive(false);
    }

    private void Ask()
    {
        if (Singleton.Instance != null && Singleton.Instance.OnlineMatchController != null)
            Singleton.Instance.OnlineMatchController.AskToResign();
    }
}
