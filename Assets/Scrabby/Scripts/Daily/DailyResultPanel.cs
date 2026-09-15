using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What you got, against what was there to get.
//
// The bar is the day's best move, and the filled part is yours - so the
// question the puzzle asked ("how many points can you get?") is answered in
// the same terms it was asked. Cut across it are the rungs of the solo
// difficulty ladder, so a score is not just a number: it says you played this
// one about as well as the Hard opponent would have.
//
// Built in code rather than laid out in the scene: it is one panel with a
// handful of parts, and keeping it here means it cannot drift away from the
// bands it draws.
public static class DailyResultPanel
{
    private static readonly GameLogic.SoloDifficulty[] Ladder =
    {
        GameLogic.SoloDifficulty.Easy,
        GameLogic.SoloDifficulty.Medium,
        GameLogic.SoloDifficulty.Hard,
        GameLogic.SoloDifficulty.Expert
    };

    // The card, the glass it is made of, and the letter-tile cream that the
    // rest of the game uses. Kept close to the pregame card's palette.
    private static readonly Color Dim = new Color(0.02f, 0.06f, 0.12f, 0.55f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.88f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Track = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color Rung = new Color(1f, 1f, 1f, 0.32f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Good = new Color(0.88f, 0.70f, 0.30f, 1f);

    public static void Show(DailyBoard day, int playerScore, string playerWord)
    {
        if (day == null)
            return;

        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("[DAILY] No canvas to show the result on.");
            return;
        }

        GameObject existing = GameObject.Find("DailyResultPanel");

        if (existing != null)
            UnityEngine.Object.Destroy(existing);

        Build(canvas, day, playerScore, playerWord);
    }

    private static Canvas FindCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in
                 UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas && (best == null || canvas.sortingOrder >= best.sortingOrder))
                best = canvas;
        }

        return best;
    }

    private static void Build(Canvas canvas, DailyBoard day, int playerScore, string playerWord)
    {
        int best = Mathf.Max(1, day.bestScore);
        float share = Mathf.Clamp01((float)playerScore / best);
        GameLogic.SoloDifficulty level = GameLogic.BandFor(playerScore, day.bestScore);

        // ---- the dimmed backdrop --------------------------------------------
        GameObject root = Panel("DailyResultPanel", canvas.transform, Dim);
        Stretch(root);

        // ---- the card --------------------------------------------------------
        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(860f, 660f);
        cardRect.anchoredPosition = Vector2.zero;

        float y = -54f;

        Label(card.transform, "Daily #" + day.dayNumber, 34f, Faint,
              FontStyles.Normal, y, 60f);
        y -= 66f;

        // What you played, given the weight it deserves.
        Label(card.transform, playerWord.ToUpperInvariant(), 78f, Cream,
              FontStyles.Bold, y, 96f);
        y -= 104f;

        Label(card.transform, playerScore + " of " + day.bestScore + " points",
              40f, Color.white, FontStyles.Normal, y, 56f);
        y -= 92f;

        // ---- the ladder bar ---------------------------------------------------
        BuildBar(card.transform, y, share, level);
        y -= 150f;

        Label(card.transform, "You played this one at " + level, 44f, Cream,
              FontStyles.Bold, y, 60f);
        y -= 64f;

        string bestLine = string.IsNullOrEmpty(day.bestWord)
            ? ""
            : "The best here was " + day.bestWord.ToUpperInvariant() +
              " for " + day.bestScore;

        Label(card.transform, bestLine, 32f, Faint, FontStyles.Normal, y, 48f);
        y -= 78f;

        // ---- what to do next --------------------------------------------------
        // Offering a game at the level just played turns a score into a next
        // step, which is the point of grading it against the ladder at all.
        Button(card.transform, "Play a " + level + " game", -300f, y, Cream, Ink,
               delegate { StartGameAt(level); });

        Button(card.transform, "Done", 300f, y, new Color(1f, 1f, 1f, 0.18f),
               Color.white, delegate { Done(); });
    }

    // The bar is the whole of the day's best move. Yours is the filled part.
    private static void BuildBar(
        Transform parent, float y, float share, GameLogic.SoloDifficulty level)
    {
        const float width = 720f;
        const float height = 46f;

        GameObject track = Panel("Track", parent, Track);
        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.5f, 1f);
        trackRect.anchorMax = new Vector2(0.5f, 1f);
        trackRect.pivot = new Vector2(0.5f, 1f);
        trackRect.sizeDelta = new Vector2(width, height);
        trackRect.anchoredPosition = new Vector2(0f, y);

        GameObject fill = Panel("Fill", track.transform, Good);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.sizeDelta = new Vector2(width * share, 0f);
        fillRect.anchoredPosition = Vector2.zero;

        // ---- the rungs --------------------------------------------------------
        foreach (GameLogic.SoloDifficulty rung in Ladder)
        {
            float at = GameLogic.ScoreFractionFor(rung);

            // Expert sits at the far end, where the bar already ends.
            if (at < 1f)
            {
                GameObject tick = Panel("Rung", track.transform, Rung);
                RectTransform tickRect = tick.GetComponent<RectTransform>();
                tickRect.anchorMin = new Vector2(0f, 0f);
                tickRect.anchorMax = new Vector2(0f, 1f);
                tickRect.pivot = new Vector2(0.5f, 0.5f);
                tickRect.sizeDelta = new Vector2(3f, 0f);
                tickRect.anchoredPosition = new Vector2(width * at, 0f);
            }

            GameObject caption = new GameObject(rung.ToString(),
                typeof(RectTransform), typeof(TextMeshProUGUI));
            caption.transform.SetParent(track.transform, false);

            TextMeshProUGUI text = caption.GetComponent<TextMeshProUGUI>();
            text.text = rung.ToString();
            text.fontSize = 26f;
            text.color = rung == level ? Cream : Rung;
            text.fontStyle = rung == level ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = at < 1f ? TextAlignmentOptions.Left
                                     : TextAlignmentOptions.Right;

            RectTransform capRect = caption.GetComponent<RectTransform>();
            capRect.anchorMin = new Vector2(0f, 0f);
            capRect.anchorMax = new Vector2(0f, 0f);
            capRect.pivot = new Vector2(0f, 1f);
            capRect.sizeDelta = new Vector2(180f, 34f);
            capRect.anchoredPosition =
                new Vector2(width * at + (at < 1f ? 8f : -180f), -10f);
        }
    }

    private static void StartGameAt(GameLogic.SoloDifficulty level)
    {
        Close();

        if (Singleton.Instance == null || Singleton.Instance.GameLogic == null)
            return;

        Singleton.Instance.GameLogic.LeaveDailyMode();

        if (Singleton.Instance.DebugManager != null)
        {
            Singleton.Instance.DebugManager.LoadFromJson();
            Singleton.Instance.DebugManager.StartNewGame(level);
        }
    }

    // Done means done: the day has been answered and cannot be played again,
    // so there is nothing to go back to on the board. The option panel is
    // where the next choice gets made.
    private static void Done()
    {
        Close();

        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.LeaveDailyMode();

        OptionPanelController options =
            UnityEngine.Object.FindAnyObjectByType<OptionPanelController>(
                FindObjectsInactive.Include);

        if (options != null)
            options.ShowOptionPanel();
    }

    private static void Close()
    {
        GameObject panel = GameObject.Find("DailyResultPanel");

        if (panel != null)
            UnityEngine.Object.Destroy(panel);
    }

    // ------------------------------------------------------------- building --
    private static GameObject Panel(string name, Transform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        return go;
    }

    private static void Stretch(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Label(
        Transform parent, string content, float size, Color colour,
        FontStyles style, float y, float height)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform),
                                       typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(780f, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    private static void Button(
        Transform parent, string content, float x, float y,
        Color face, Color ink, Action onClick)
    {
        GameObject go = Panel("Button", parent, face);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(360f, 84f);
        rect.anchoredPosition = new Vector2(x, y);

        UnityEngine.UI.Button button = go.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.onClick.AddListener(delegate { onClick(); });

        GameObject labelGo = new GameObject("Label", typeof(RectTransform),
                                            typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = 34f;
        text.color = ink;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;

        Stretch(labelGo);
    }
}
