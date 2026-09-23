using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The player's own record, as bars.
//
// Four levels and a list of friends is a lot of numbers to read as numbers.
// Drawn as bars they can be taken in at a glance: how much has been played
// against each, and how much of it was won - and the levels sit above each
// other, so the climb from Easy to Expert is the shape of the chart.
//
// Built in code, like the rules card and the daily result, so the words stay
// next to what they describe rather than in a scene file nobody opens.
public static class StatsPanel
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.55f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.92f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Fainter = new Color(1f, 1f, 1f, 0.38f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color Rust = new Color(0.78f, 0.33f, 0.28f, 1f);
    private static readonly Color Grey = new Color(1f, 1f, 1f, 0.28f);
    private static readonly Color Sunk = new Color(1f, 1f, 1f, 0.055f);
    private static readonly Color Highlight = new Color(0.88f, 0.70f, 0.30f, 0.13f);

    // Not "StatsPanel": the scene already has a GameObject by that name - the
    // heading strip over the board with the round, the clock and the two
    // scores. This card opens by finding its own old copy and destroying it,
    // so sharing the name meant every look at "Your progress" destroyed the
    // scoreboard instead, for good, until the game was restarted.
    private const string PanelName = "YourProgressPanel";

    private const float CardWidth = 900f;

    private const float RowHeight = 62f;
    private const float BarHeight = 34f;
    private const float TrackWidth = 470f;

    // A bar for one game would otherwise be a sliver; this is the least that
    // still reads as a bar.
    private const float LeastBarWidth = 64f;

    // Enough friends to see who is being played, few enough to fit on a phone
    // without a scroll view.
    private const int MostOpponents = 6;

    private const float LeftEdge = -CardWidth / 2f + 44f;
    private const float NameWidth = 150f;
    private const float TrackLeft = LeftEdge + NameWidth + 20f;
    private const float PercentLeft = TrackLeft + TrackWidth + 16f;

    // Opened from the main menu, where nothing in particular is being asked
    // about.
    public static void Show()
    {
        Show(null);
    }

    // highlightKey singles out the row for the game just finished - the level
    // that was played, or the friend it was against - so the panel answers
    // "and where does that leave me?" without the player hunting for the line.
    public static void Show(string highlightKey)
    {
        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("[STATS] No canvas to show the chart on.");
            return;
        }

        GameObject existing = GameObject.Find(PanelName);

        if (existing != null)
            Object.Destroy(existing);

        Build(canvas, highlightKey);
    }

    private static Canvas FindCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas &&
                (best == null || canvas.sortingOrder >= best.sortingOrder))
            {
                best = canvas;
            }
        }

        return best;
    }

    private static void Build(Canvas canvas, string highlightKey)
    {
        List<PlayerStats.Record> solo = PlayerStats.Solo();
        List<PlayerStats.Record> opponents = PlayerStats.Opponents();

        if (opponents.Count > MostOpponents)
            opponents = opponents.GetRange(0, MostOpponents);

        DailyProgress.DailyRecord daily = DailyProgress.Load();
        bool hasStreak = daily != null && daily.bestStreak > 0;

        int friendRows = Mathf.Max(1, opponents.Count);

        float height =
            44f + 70f + 40f +                        // padding, title, the line under it
            (hasStreak ? 48f : 0f) +
            46f + solo.Count * RowHeight + 22f +     // the computer, and its rows
            46f + friendRows * RowHeight +           // friends, and theirs
            150f;                                    // the way out

        GameObject root = Panel(PanelName, canvas.transform, Dim);
        Stretch(root);

        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, height);
        rect.anchoredPosition = Vector2.zero;

        float y = -44f;

        Label(card.transform, "Your progress", 52f, Color.white, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, y, CardWidth - 80f, 62f);
        y -= 70f;

        Label(card.transform,
              PlayerStats.AnythingRecorded()
                  ? "Every finished game, counted."
                  : "Nothing yet - finish a game and it lands here.",
              28f, Faint, FontStyles.Normal, TextAlignmentOptions.Center,
              0f, y, CardWidth - 80f, 40f);
        y -= 40f;

        if (hasStreak)
        {
            Label(card.transform,
                  "Daily streak <color=#E0B34D><b>" + daily.streak + "</b></color>" +
                  "   -   best <color=#E0B34D><b>" + daily.bestStreak + "</b></color>",
                  26f, Faint, FontStyles.Normal, TextAlignmentOptions.Center,
                  0f, y, CardWidth - 80f, 36f);
            y -= 48f;
        }

        // ---- against the computer -------------------------------------------
        y = Heading(card.transform, "AGAINST THE COMPUTER", y);
        y = Rows(card.transform, solo, y, highlightKey, "not played yet");

        y -= 22f;

        // ---- against friends ------------------------------------------------
        y = Heading(card.transform, "AGAINST FRIENDS", y);

        if (opponents.Count == 0)
        {
            Label(card.transform, "No online game has finished yet.", 26f, Fainter,
                  FontStyles.Italic, TextAlignmentOptions.Left,
                  0f, y - 12f, CardWidth - 88f, 40f);

            y -= RowHeight;
        }
        else
        {
            y = Rows(card.transform, opponents, y, highlightKey, "");
        }

        // ---- and out --------------------------------------------------------
        GameObject done = Panel("Done", card.transform, Cream);
        RectTransform doneRect = done.GetComponent<RectTransform>();
        doneRect.anchorMin = doneRect.anchorMax = new Vector2(0.5f, 0f);
        doneRect.pivot = new Vector2(0.5f, 0f);
        doneRect.sizeDelta = new Vector2(CardWidth - 90f, 92f);
        doneRect.anchoredPosition = new Vector2(0f, 34f);

        Button button = done.AddComponent<Button>();
        button.targetGraphic = done.GetComponent<Image>();
        button.onClick.AddListener(Close);
        ChunkyButton.Deepen(button);

        Label(done.transform, "Close", 34f, Ink, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, CardWidth - 90f, 92f, true);
    }

    private static float Heading(Transform card, string text, float y)
    {
        Label(card, text, 22f, Fainter, FontStyles.Normal,
              TextAlignmentOptions.Left, 0f, y, CardWidth - 88f, 30f);

        return y - 40f;
    }

    // Bars are measured against the busiest row rather than each being full
    // width, so a level played twenty times does not look the same as one
    // played twice.
    private static float Rows(
        Transform card, List<PlayerStats.Record> rows, float y,
        string highlightKey, string emptyNote)
    {
        int most = 1;

        foreach (PlayerStats.Record record in rows)
            most = Mathf.Max(most, record.Games);

        foreach (PlayerStats.Record record in rows)
        {
            if (record.key == highlightKey)
            {
                Block(card, Highlight, LeftEdge - 12f, y + 6f,
                      CardWidth - 64f, RowHeight - 6f);
            }

            Label(card, record.name, 27f, record.Games > 0 ? Color.white : Fainter,
                  FontStyles.Normal, TextAlignmentOptions.Left,
                  LeftEdge + NameWidth / 2f, y - 14f, NameWidth, 36f);

            if (record.Games == 0)
            {
                Block(card, Sunk, TrackLeft, y - 8f, TrackWidth, BarHeight);

                if (!string.IsNullOrEmpty(emptyNote))
                {
                    Label(card, emptyNote, 21f, Fainter, FontStyles.Italic,
                          TextAlignmentOptions.Left,
                          TrackLeft + 110f, y - 12f, 200f, 30f);
                }

                y -= RowHeight;
                continue;
            }

            float width = Mathf.Max(
                LeastBarWidth, TrackWidth * record.Games / (float)most);

            float x = TrackLeft;

            x = Segment(card, x, y, width, record.won, record.Games, Amber, Ink);
            x = Segment(card, x, y, width, record.lost, record.Games, Rust, Color.white);
            Segment(card, x, y, width, record.tied, record.Games, Grey, Color.white);

            Label(card, record.WinPercent + "%", 26f, Amber, FontStyles.Bold,
                  TextAlignmentOptions.Left,
                  PercentLeft + 45f, y - 14f, 90f, 36f);

            y -= RowHeight;
        }

        return y;
    }

    // One part of a bar - games won, lost or tied - with its count inside it
    // when there is room to print it.
    private static float Segment(
        Transform card, float x, float y, float barWidth,
        int count, int games, Color colour, Color ink)
    {
        if (count <= 0)
            return x;

        float width = barWidth * count / games;

        GameObject block = Block(card, colour, x, y - 8f, width, BarHeight);

        if (width >= 34f)
        {
            Label(block.transform, count.ToString(), 22f, ink, FontStyles.Bold,
                  TextAlignmentOptions.Center, 0f, 0f, width, BarHeight, true);
        }

        return x + width;
    }

    private static void Close()
    {
        GameObject panel = GameObject.Find(PanelName);

        if (panel != null)
            Object.Destroy(panel);
    }

    // ------------------------------------------------------------- building --
    private static GameObject Panel(string name, Transform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        return go;
    }

    // A rectangle measured from its left edge, which is how a bar is read and
    // how one is drawn: each segment starts where the last one ended.
    private static GameObject Block(
        Transform parent, Color colour, float x, float y, float width, float height)
    {
        GameObject go = Panel("Block", parent, colour);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

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
        text.raycastTarget = false;

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
