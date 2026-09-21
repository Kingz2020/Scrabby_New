using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The rules, for someone who has never played this before.
//
// One rule has to land: you and your opponent are dealt the SAME six letters
// and play the same board. Nobody guesses that - a new player assumes
// Scrabble, then cannot make sense of the opponent playing over their word.
// So it is drawn at the top rather than described, and the lines below only
// have to fill in the rest.
//
// Built in code for the same reason the daily result is: it is one card, and
// keeping the words next to the rules they describe means they cannot drift
// apart from each other in a scene file nobody opens.
public static class HowToPlayPanel
{
    private const string SeenKey = "Scrabby.HowToPlay.Seen";

    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.55f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.92f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Fainter = new Color(1f, 1f, 1f, 0.38f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color Sunk = new Color(1f, 1f, 1f, 0.055f);
    private static readonly Color Box = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color WinBox = new Color(0.88f, 0.70f, 0.30f, 0.17f);

    private const float CardWidth = 900f;
    private const float CardHeight = 1368f;

    // The rules, in the order they have to be understood.
    private static readonly string[] Rules =
    {
        "You and your opponent are dealt <b>the same six letters</b>, and play on the same board.",
        "The <b>higher-scoring word wins the round</b>, and that word is placed on the board for the next one. The other scores nothing.",
        "Bonus squares are <b>scattered at random</b> - and scattered again <color=#F1E0BB><b>after every round</b></color>. That double-word won't be there next time.",
        "Use <b>all six letters</b> in one word and you get <color=#F1E0BB><b>20 extra points</b></color>.",
        "Whoever wins <b>the most rounds</b> takes the game, over four rounds - against the computer or a friend.",
        "Play against the <b>computer</b>, or invite a <b>friend online</b> by email.",
        "And there's a <b>daily puzzle</b>: one board, one word, one go a day."
    };

    // Shown once, the first time the game is opened. After that it is only
    // seen by someone who asks for it.
    public static void ShowIfNotSeen()
    {
        if (PlayerPrefs.GetInt(SeenKey, 0) != 0)
            return;

        Show();
    }

    public static void Show()
    {
        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("[HELP] No canvas to show the rules on.");
            return;
        }

        GameObject existing = GameObject.Find("HowToPlayPanel");

        if (existing != null)
            UnityEngine.Object.Destroy(existing);

        Build(canvas);
    }

    private static Canvas FindCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                     FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas &&
                (best == null || canvas.sortingOrder >= best.sortingOrder))
            {
                best = canvas;
            }
        }

        return best;
    }

    private static void Build(Canvas canvas)
    {
        GameObject root = Panel("HowToPlayPanel", canvas.transform, Dim);
        Stretch(root);

        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
        rect.anchoredPosition = Vector2.zero;

        float y = -44f;

        Label(card.transform, "How to play", 52f, Color.white,
              FontStyles.Bold, TextAlignmentOptions.Center, 0f, y, CardWidth - 80f, 62f);
        y -= 70f;

        Label(card.transform, "It looks like Scrabble. It isn't.", 30f, Faint,
              FontStyles.Normal, TextAlignmentOptions.Center, 0f, y, CardWidth - 80f, 40f);
        y -= 62f;

        y = BuildDuel(card.transform, y);
        y -= 26f;

        // ---- the rules ------------------------------------------------------
        for (int i = 0; i < Rules.Length; i++)
        {
            float height = HeightOf(Rules[i]);

            Bubble(card.transform, (i + 1).ToString(), -CardWidth / 2f + 66f, y);

            Label(card.transform, Rules[i], 27f, Faint,
                  FontStyles.Normal, TextAlignmentOptions.TopLeft,
                  46f, y, CardWidth - 152f, height);

            y -= height + 14f;
        }

        // ---- and out --------------------------------------------------------
        GameObject got = Panel("GotIt", card.transform, Cream);
        RectTransform gotRect = got.GetComponent<RectTransform>();
        gotRect.anchorMin = gotRect.anchorMax = new Vector2(0.5f, 0f);
        gotRect.pivot = new Vector2(0.5f, 0f);
        gotRect.sizeDelta = new Vector2(CardWidth - 90f, 92f);
        gotRect.anchoredPosition = new Vector2(0f, 34f);

        Button button = got.AddComponent<Button>();
        button.targetGraphic = got.GetComponent<Image>();
        button.onClick.AddListener(Close);
        ChunkyButton.Deepen(button);

        Label(got.transform, "Got it", 34f, Ink, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, CardWidth - 90f, 92f, true);
    }

    // The rule nobody guesses, drawn: one rack, two words, one winner.
    private static float BuildDuel(Transform parent, float y)
    {
        const float height = 320f;

        GameObject block = Panel("Duel", parent, Sunk);
        RectTransform rect = block.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(CardWidth - 80f, height);
        rect.anchoredPosition = new Vector2(0f, y);

        Label(block.transform, "YOU BOTH GET THE SAME SIX", 22f, Fainter,
              FontStyles.Normal, TextAlignmentOptions.Center, 0f, -16f,
              CardWidth - 120f, 30f);

        Rack(block.transform, "RATESK", -54f);

        // the two answers to it
        float boxWidth = (CardWidth - 130f) / 2f - 24f;

        SideBox(block.transform, "YOU", "SKATE", "28", true,
                -(boxWidth / 2f + 26f), -150f, boxWidth);

        SideBox(block.transform, "OPPONENT", "RATE", "12", false,
                boxWidth / 2f + 26f, -150f, boxWidth);

        Label(block.transform, "VS", 20f, Fainter, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, -212f, 60f, 30f);

        return y - height;
    }

    private static void Rack(Transform parent, string letters, float y)
    {
        const float size = 54f;
        const float gap = 7f;

        float total = letters.Length * size + (letters.Length - 1) * gap;
        float x = -total / 2f + size / 2f;

        foreach (char letter in letters)
        {
            Tile(parent, letter.ToString(), x, y, size);
            x += size + gap;
        }
    }

    private static void SideBox(
        Transform parent, string who, string word, string points,
        bool winner, float x, float y, float width)
    {
        GameObject box = Panel("Side", parent, winner ? WinBox : Box);
        RectTransform rect = box.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, winner ? 150f : 122f);
        rect.anchoredPosition = new Vector2(x, y);

        Label(box.transform, who, 20f, Fainter, FontStyles.Normal,
              TextAlignmentOptions.Center, 0f, -10f, width, 26f);

        const float size = 34f;
        const float gap = 4f;
        float total = word.Length * size + (word.Length - 1) * gap;
        float tx = -total / 2f + size / 2f;

        foreach (char letter in word)
        {
            Tile(box.transform, letter.ToString(), tx, -44f, size);
            tx += size + gap;
        }

        Label(box.transform, points, 32f, winner ? Amber : Color.white,
              FontStyles.Bold, TextAlignmentOptions.Center, 0f, -88f, width, 40f);

        if (winner)
        {
            Label(box.transform, "WINS THE ROUND", 18f, Amber, FontStyles.Bold,
                  TextAlignmentOptions.Center, 0f, -124f, width, 24f);
        }
    }

    private static void Tile(Transform parent, string letter, float x, float y, float size)
    {
        GameObject tile = Panel("Tile", parent, Cream);
        RectTransform rect = tile.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = new Vector2(x, y);

        Label(tile.transform, letter, size * 0.56f, Ink, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, size, size, true);
    }

    private static void Bubble(Transform parent, string number, float x, float y)
    {
        GameObject bubble = Panel("Num", parent, new Color(0.945f, 0.878f, 0.733f, 0.15f));
        RectTransform rect = bubble.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(38f, 38f);
        rect.anchoredPosition = new Vector2(x, y - 2f);

        Label(bubble.transform, number, 22f, Cream, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, 38f, 38f, true);
    }

    // Rough, but the lines are known and short; a layout group would cost more
    // than it saves for six fixed strings.
    private static float HeightOf(string rule)
    {
        int length = rule.Length;
        return length > 118 ? 96f : (length > 74 ? 70f : 44f);
    }

    private static void Close()
    {
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();

        GameObject panel = GameObject.Find("HowToPlayPanel");

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
        FontStyles style, TextAlignmentOptions align,
        float x, float y, float width, float height, bool centred = false)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform),
                                       typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = style;
        text.alignment = align;
        text.richText = true;

        RectTransform rect = go.GetComponent<RectTransform>();

        if (centred)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return;
        }

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
