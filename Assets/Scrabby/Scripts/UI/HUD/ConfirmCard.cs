using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Are you sure?", for the two things that cannot be taken back.
//
// Resigning hands the game to the other player; removing a finished game
// takes it off your list for good. Both are one tap away from things people
// tap all the time, so both ask first - once, plainly, naming what will
// happen rather than asking whether they are sure in the abstract.
public static class ConfirmCard
{
    private const string PanelName = "ConfirmCard";

    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.72f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.78f);

    public static void Ask(string question, string detail, string yes, Action onYes)
    {
        Canvas canvas = TopCanvas();

        if (canvas == null)
        {
            // Nowhere to ask, so nothing is done: better than doing something
            // irreversible without asking.
            Debug.LogWarning("[CONFIRM] No canvas to ask on.");
            return;
        }

        GameObject existing = GameObject.Find(PanelName);

        if (existing != null)
            UnityEngine.Object.Destroy(existing);

        GameObject root = Panel(PanelName, canvas.transform, Dim);
        Stretch(root);

        // Nothing behind it is reachable while it is up.
        root.AddComponent<Button>().transition = Selectable.Transition.None;

        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(840f, 460f);

        Label(card.transform, question, 44f, Color.white, FontStyles.Bold, 0f, -46f, 740f, 120f);
        Label(card.transform, detail, 28f, Faint, FontStyles.Normal, 0f, -180f, 740f, 110f);

        // The one that does it, and the one that does not. Cancel is the
        // wider, nearer, plainer of the two.
        Button no = TextButton(card.transform, "Cancel", -186f, 46f, 360f, Cream, Ink);
        Button go = TextButton(card.transform, yes, 186f, 46f, 360f,
                               new Color(0.62f, 0.20f, 0.18f, 1f), Color.white);

        no.onClick.AddListener(delegate { UnityEngine.Object.Destroy(root); });

        go.onClick.AddListener(delegate
        {
            UnityEngine.Object.Destroy(root);

            if (onYes != null)
                onYes();
        });
    }

    private static Button TextButton(Transform parent, string caption, float x, float y,
                                     float width, Color face, Color ink)
    {
        GameObject go = Panel("Button", parent, face);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(width, 96f);
        rect.anchoredPosition = new Vector2(x, y);

        Label(go.transform, caption, 32f, ink, FontStyles.Bold, 0f, 0f, width, 96f, true);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();

        ChunkyButton.Deepen(button);

        return button;
    }

    private static Canvas TopCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas &&
                (best == null || canvas.sortingOrder >= best.sortingOrder))
            {
                best = canvas;
            }
        }

        return best;
    }

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

    private static void Label(Transform parent, string content, float size, Color colour,
                              FontStyles style, float x, float y, float width, float height,
                              bool fill = false)
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
        text.raycastTarget = false;

        RectTransform rect = go.GetComponent<RectTransform>();

        if (fill)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return;
        }

        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
