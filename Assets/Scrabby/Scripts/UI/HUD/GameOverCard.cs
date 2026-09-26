using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The result board. One card, whatever was played.
//
// It reads a ResultSheet and nothing else: the verdict, the rounds, and
// which ways out this game deserves. It does not ask the game or the match
// controller what sort of game it was, and it no longer borrows the panel's
// own buttons by name and drags them onto itself - it builds its own, which
// is why the buttons on it can be relied on to be there, be pressable, and
// mean the same thing on every card.
//
// What it inherited and kept: the panel's round rows, because the rows are
// spawned into the panel's container by UIManager and keeping that means one
// prefab and one spawner.
public class GameOverCard : MonoBehaviour
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.72f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color Silver = new Color(0.90f, 0.92f, 0.94f, 1f);
    private static readonly Color Warn = new Color(1f, 0.72f, 0.66f, 0.95f);
    private static readonly Color Sunk = new Color(1f, 1f, 1f, 0.055f);

    private const float CardWidth = 960f;
    private const float RowsTop = 286f;      // below the headline block
    private const float RowHeight = 78f;     // one replay row, near enough

    // The panel's own furniture, which the card replaces.
    private static readonly string[] PanelFurniture =
    {
        "Background", "gameOverSummaryText", "newgame", "MainMenuButton",
        "Back2MatchButton", "StatsLink", "RemoveGameLink"
    };

    private RectTransform card;
    private TextMeshProUGUI headline;
    private TextMeshProUGUI detail;
    private RectTransform rows;

    private Button mainMenu;
    private Button anotherGame;
    private Button backToMatches;
    private Button removeGame;
    private Button progress;

    private ResultSheet shown;
    private Transform lastFirstRow;
    private int lastRowCount = -1;

    // Added by the panel the first time it opens.
    public static void DressPanel(GameObject panel)
    {
        if (panel != null && panel.GetComponent<GameOverCard>() == null)
            panel.AddComponent<GameOverCard>();
    }

    private void OnEnable()
    {
        Build();
        Apply(UIManager.CurrentResult, true);
    }

    private void Update()
    {
        // A result can arrive after the panel is up - an online one is read
        // from the match - and the rows are spawned a moment after that. Both
        // are noticed here rather than assumed to have happened already.
        Apply(UIManager.CurrentResult, false);
    }

    // ------------------------------------------------------------- building --

    private void Build()
    {
        if (card != null)
            return;

        RectTransform panel = transform as RectTransform;

        foreach (string name in PanelFurniture)
        {
            Transform found = FindAnywhere(name);

            if (found != null)
                found.gameObject.SetActive(false);
        }

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

        MoveRows();
        BuildWaysOut();
    }

    // The rows keep their prefab and their spawner; they hang under the card.
    private void MoveRows()
    {
        Transform container = FindAnywhere("RoundListContainer");

        if (container == null)
            return;

        rows = container as RectTransform;
        rows.gameObject.SetActive(true);
        rows.SetParent(card, false);
        rows.anchorMin = rows.anchorMax = new Vector2(0.5f, 1f);
        rows.pivot = new Vector2(0.5f, 1f);
        rows.sizeDelta = new Vector2(CardWidth - 110f, 0f);
        rows.anchoredPosition = new Vector2(0f, -RowsTop);
    }

    // Built once, shown or hidden per sheet. Its own buttons, with its own
    // geometry: nothing here can be moved, disabled or covered by something
    // else on the panel.
    private void BuildWaysOut()
    {
        mainMenu = Key("MainMenuKey", "Main Menu", Cream, Ink, 320f, 96f, 38f);

        // A word, not a symbol: the font has no glyph for a circular arrow,
        // so it drew the empty box that means "no such character". It says
        // what it does now, which the symbol never did either.
        anotherGame = Key("AnotherGameKey", "New game", Amber, Ink, 280f, 96f, 34f);
        backToMatches = Key("BackToMatchesKey", "Back to Match", Silver, Ink, 360f, 88f, 36f);

        removeGame = Link("RemoveGameLink", "Remove this game", Warn, 28f);
        progress = Link("ProgressLink", "Your progress", new Color(0.945f, 0.878f, 0.733f, 0.95f), 30f);
    }

    // ------------------------------------------------------------- content --

    private void Apply(ResultSheet sheet, bool force)
    {
        if (sheet == null || card == null)
            return;

        if (force || sheet != shown)
        {
            shown = sheet;

            headline.text = sheet.Headline ?? "";
            detail.text = sheet.Detail ?? "";

            Wire(mainMenu, sheet.MainMenu, true);
            Wire(anotherGame, sheet.AnotherGame, sheet.OfferAnotherGame);
            Wire(backToMatches, sheet.BackToMatches, sheet.OfferBackToMatches);
            Wire(removeGame, sheet.RemoveGame, sheet.OfferRemoveGame);
            Wire(progress, () => StatsPanel.Show(sheet.ProgressKey), sheet.OfferProgress);

            LayOutWaysOut(sheet);
        }

        // The rows are spawned into the container after the sheet arrives, and
        // four rows replaced by four rows is a change the count alone cannot
        // see - which is how a second game of the same length used to keep the
        // first game's dressing.
        int count = rows != null ? rows.childCount : 0;
        Transform firstRow = count > 0 ? rows.GetChild(0) : null;

        if (force || count != lastRowCount || firstRow != lastFirstRow)
        {
            lastRowCount = count;
            lastFirstRow = firstRow;

            DressRows();
            Fit(count, shown);
        }
    }

    // From the bottom up, so what is not offered leaves no hole behind it.
    private void LayOutWaysOut(ResultSheet sheet)
    {
        float y = 30f;

        // The way out everybody needs, with another game beside it when there
        // is one to deal.
        Place(mainMenu, sheet.OfferAnotherGame ? -160f : 0f, y + 48f);
        Place(anotherGame, 160f, y + 48f);
        y += 96f;

        if (sheet.OfferBackToMatches)
        {
            y += 26f;
            Place(backToMatches, 0f, y + 44f);
            y += 88f;
        }

        if (sheet.OfferProgress)
        {
            y += 34f;
            Place(progress, 0f, y + 26f);
            y += 52f;
        }

        if (sheet.OfferRemoveGame)
        {
            y += 12f;
            Place(removeGame, 0f, y + 24f);
            y += 48f;
        }

        band = y + 14f;
    }

    private float band = 300f;

    // The card is as tall as what is on it: one round after the walkthrough,
    // four after a real game, and taller again when there is more to offer.
    private void Fit(int rowCount, ResultSheet sheet)
    {
        if (card == null)
            return;

        float height = RowsTop + Mathf.Max(1, rowCount) * RowHeight + band;

        card.sizeDelta = new Vector2(CardWidth, Mathf.Clamp(height, 700f, 1500f));
    }

    // The rows were coloured for the light blue panel they used to sit on.
    // They dress themselves now; this is the card having its say about a row
    // it can see, and costs nothing when there is nothing to change.
    private void DressRows()
    {
        if (rows == null)
            return;

        foreach (RoundReplayRow row in rows.GetComponentsInChildren<RoundReplayRow>(true))
            row.DressForDarkCard(Sunk, Color.white, Amber, Ink);
    }

    // ------------------------------------------------------------- plumbing --

    private void Wire(Button button, System.Action action, bool offered)
    {
        if (button == null)
            return;

        button.gameObject.SetActive(offered);
        button.onClick.RemoveAllListeners();

        if (offered && action != null)
            button.onClick.AddListener(() => action());
    }

    private static void Place(Button button, float x, float y)
    {
        if (button == null)
            return;

        RectTransform rect = button.transform as RectTransform;
        rect.anchoredPosition = new Vector2(x, y);
    }

    private Button Key(string name, string caption, Color face, Color ink,
                       float width, float height, float size)
    {
        GameObject go = Panel(name, card, face);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);

        Label(go.transform as RectTransform, caption, size, ink, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, width, height, true);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();

        ChunkyButton.Deepen(button);

        return button;
    }

    private Button Link(string name, string caption, Color colour, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform),
                                       typeof(TextMeshProUGUI), typeof(Button));
        go.transform.SetParent(card, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = caption;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = FontStyles.Underline;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, size + 20f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = text;

        return button;
    }

    // Anywhere under the panel, not just the top level: the row container is
    // moved onto the card on the first open, so looking only at direct
    // children finds it once and never again.
    private Transform FindAnywhere(string name)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

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
        float x, float y, float width, float height, bool fill = false)
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

        if (fill)
        {
            Stretch(rect);
            return text;
        }

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        return text;
    }
}
