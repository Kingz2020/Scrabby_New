using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The result, dressed like the rest of the game.
//
// The game-over screen was the one panel that never got the glass-card
// treatment: a cartoon frame, a sky, text in four colours at three sizes and
// buttons scattered along the bottom. Everything on it worked, so this does
// not rebuild any of it - it turns off the frame, puts a card in the middle,
// and moves what is already there onto the card: the summary, the round rows
// with their replay buttons, and the way out.
//
// Nothing here knows whether the game was solo, online or a tutorial. It
// reads what the panel was given and lays it out.
public class GameOverCard : MonoBehaviour
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.72f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Fainter = new Color(1f, 1f, 1f, 0.38f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color Sunk = new Color(1f, 1f, 1f, 0.055f);

    private const float CardWidth = 960f;
    private const float RowsTop = 286f;      // below the headline block
    private const float RowHeight = 78f;     // one replay row, near enough
    private const float ButtonBand = 300f;   // the space kept for the way out

    private RectTransform card;
    private TextMeshProUGUI headline;
    private TextMeshProUGUI detail;

    private TextMeshProUGUI summary;         // the panel's own text, now hidden
    private RectTransform rows;
    private RectTransform removeLink;

    private string lastText = "";
    private int lastRowCount = -1;
    private bool lastRemoveShowing;

    // Added by the panel the first time it opens.
    public static void DressPanel(GameObject panel)
    {
        if (panel != null && panel.GetComponent<GameOverCard>() == null)
            panel.AddComponent<GameOverCard>();
    }

    private void OnEnable()
    {
        Build();
        Sync(true);
    }

    private void Update()
    {
        // The summary is written before the panel is shown, and again when an
        // online result arrives late, so the card follows it rather than
        // reading it once.
        Sync(false);
    }

    // ------------------------------------------------------------- building --

    private void Build()
    {
        if (card != null)
            return;

        RectTransform panel = transform as RectTransform;

        // The cartoon frame and its sky go. Everything that matters moves onto
        // the card below.
        Transform background = transform.Find("Background");

        if (background != null)
            background.gameObject.SetActive(false);

        GameObject dim = Panel("Dim", panel, Dim);
        Stretch(dim.GetComponent<RectTransform>());
        dim.transform.SetSiblingIndex(0);

        GameObject cardGo = Panel("Card", panel, Glass);
        card = cardGo.GetComponent<RectTransform>();
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardWidth, 900f);
        card.anchoredPosition = new Vector2(0f, -40f);
        card.SetSiblingIndex(1);

        headline = Label(card, "", 56f, Cream, FontStyles.Bold,
                         TextAlignmentOptions.Center, 0f, -46f, CardWidth - 90f, 76f);

        detail = Label(card, "", 38f, Faint, FontStyles.Normal,
                       TextAlignmentOptions.Center, 0f, -132f, CardWidth - 90f, 116f);

        GameObject line = Panel("Line", card, new Color(0.88f, 0.70f, 0.30f, 0.30f));
        RectTransform lineRect = line.GetComponent<RectTransform>();
        lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 1f);
        lineRect.pivot = new Vector2(0.5f, 1f);
        lineRect.sizeDelta = new Vector2(CardWidth - 120f, 2f);
        lineRect.anchoredPosition = new Vector2(0f, -232f);

        // No "ROUND BY ROUND" heading: the rows underneath are plainly a
        // round-by-round list, and the line above already separates them
        // from the score.

        MoveRows();
        MoveButtons();
    }

    // The rows keep their prefab, their replay buttons and their wiring; they
    // simply hang under the card now.
    private void MoveRows()
    {
        Transform container = transform.Find("RoundListContainer");

        if (container == null)
            return;

        rows = container as RectTransform;
        rows.SetParent(card, false);
        rows.anchorMin = rows.anchorMax = new Vector2(0.5f, 1f);
        rows.pivot = new Vector2(0.5f, 1f);
        rows.sizeDelta = new Vector2(CardWidth - 110f, 0f);
        rows.anchoredPosition = new Vector2(0f, -RowsTop);
    }

    private void MoveButtons()
    {
        // Main Menu is the one everybody wants, so it is the solid one.
        Dress("MainMenuButton", Cream, Ink, new Vector2(320f, 96f),
              new Vector2(-178f, 78f));

        // Another game: the icon button that was in the corner.
        Dress("newgame", Amber, Ink, new Vector2(110f, 96f),
              new Vector2(150f, 78f));

        // Only ever on screen for an online result; the panel decides.
        // Dark lettering, not cream: the button's own face is pale silver,
        // and cream on silver could not be read at all.
        Dress("Back2MatchButton", new Color(1f, 1f, 1f, 0.10f), Ink,
              new Vector2(360f, 88f), new Vector2(0f, 196f));

        // The progress link the panel adds for itself.
        Transform stats = transform.Find("StatsLink");

        if (stats != null)
        {
            RectTransform rect = stats as RectTransform;
            rect.SetParent(card, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400f, 52f);
            rect.anchoredPosition = new Vector2(0f, 300f);
        }

        // And the way to put a finished game away, above it - only there on
        // a match opened from the finished list, so the card grows the extra
        // line only when it is showing.
        Transform remove = transform.Find("RemoveGameLink");

        if (remove != null)
        {
            removeLink = remove as RectTransform;
            removeLink.SetParent(card, false);
            removeLink.anchorMin = removeLink.anchorMax = new Vector2(0.5f, 0f);
            removeLink.pivot = new Vector2(0.5f, 0.5f);
            removeLink.sizeDelta = new Vector2(400f, 48f);
            removeLink.anchoredPosition = new Vector2(0f, 362f);
        }
    }

    private void Dress(string name, Color face, Color ink, Vector2 size, Vector2 where)
    {
        Transform found = transform.Find(name);

        if (found == null)
            return;

        RectTransform rect = found as RectTransform;
        rect.SetParent(card, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = where;

        Image image = found.GetComponent<Image>();

        // A button with its own artwork keeps it; a plain one takes the
        // card's colours.
        if (image != null && image.sprite == null)
            image.color = face;
        else if (image != null)
            image.color = Color.white;

        foreach (TextMeshProUGUI text in found.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.color = ink;
            text.fontSize = 38f;
        }

        // Its own colours, with the thickness, light and press every button
        // has. After the move onto the card, so its thickness goes with it.
        ChunkyButton.Deepen(found.GetComponent<Button>());
    }

    // -------------------------------------------------------------- content --

    private void Sync(bool force)
    {
        if (summary == null)
        {
            Transform found = transform.Find("gameOverSummaryText");

            if (found != null)
            {
                summary = found.GetComponent<TextMeshProUGUI>();

                // Kept alive so whatever writes it can go on writing it; the
                // card is what gets read.
                if (summary != null)
                    summary.enabled = false;
            }
        }

        if (summary != null && (force || summary.text != lastText))
        {
            lastText = summary.text;
            Split(lastText);
        }

        int count = rows != null ? rows.childCount : 0;
        bool removeShowing = removeLink != null && removeLink.gameObject.activeSelf;

        if (force || count != lastRowCount || removeShowing != lastRemoveShowing)
        {
            lastRowCount = count;
            lastRemoveShowing = removeShowing;
            Fit(count);
            DressRows();
        }
    }

    // First line is the verdict, the rest is the detail under it.
    private void Split(string text)
    {
        if (headline == null || detail == null)
            return;

        string[] lines = (text ?? "").Split('\n');
        string first = "";
        string rest = "";

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (first.Length == 0)
                first = line.Trim();
            else
                rest += (rest.Length > 0 ? "\n" : "") + line.Trim();
        }

        // "Game over." said over a panel that says GAME OVER is a word wasted.
        first = first.Replace("Game over. ", "").Replace("Game over.", "Game over");

        headline.text = first;
        detail.text = rest;
    }

    // The card is as tall as what is on it: one round after the tutorial, four
    // after a real game.
    private void Fit(int rowCount)
    {
        if (card == null)
            return;

        // The extra is what the removal link needs: its own line, the gap to
        // Your progress under it, and the gap to the rounds above.
        float band = ButtonBand +
                     (removeLink != null && removeLink.gameObject.activeSelf ? 100f : 0f);

        float height = RowsTop + Mathf.Max(1, rowCount) * RowHeight + band;

        card.sizeDelta = new Vector2(CardWidth, Mathf.Clamp(height, 760f, 1500f));
    }

    // The rows were coloured for the light blue panel they used to sit on.
    private void DressRows()
    {
        if (rows == null)
            return;

        foreach (RoundReplayRow row in rows.GetComponentsInChildren<RoundReplayRow>(true))
            row.DressForDarkCard(Sunk, Color.white, Amber, Ink);
    }

    // ------------------------------------------------------------- plumbing --

    private static GameObject Panel(string name, Transform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        return go;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI Label(
        Transform parent, string content, float size, Color colour,
        FontStyles style, TextAlignmentOptions align,
        float x, float y, float width, float height)
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
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        return text;
    }
}
