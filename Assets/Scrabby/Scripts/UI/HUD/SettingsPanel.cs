using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The switches, such as they are.
//
// One so far - sound - but it needed somewhere that is not a third link under
// the Play button, both because that runs off the bottom of the card and
// because "turn the sound off" is something a player looks for in a settings
// card, not in a row of links about how to play.
//
// Built in code like the rules and the progress cards, so adding the next
// switch is adding a row here and nothing in the scene.
public static class SettingsPanel
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.55f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.95f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Fainter = new Color(1f, 1f, 1f, 0.38f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);
    private static readonly Color Off = new Color(1f, 1f, 1f, 0.14f);
    private static readonly Color Sunk = new Color(1f, 1f, 1f, 0.055f);

    private const float CardWidth = 820f;
    private const float CardHeight = 470f;

    private static TextMeshProUGUI switchLabel;
    private static Image switchFace;

    public static void Show()
    {
        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("[SETTINGS] No canvas to show the settings on.");
            return;
        }

        GameObject existing = GameObject.Find("SettingsPanel");

        if (existing != null)
            Object.Destroy(existing);

        Build(canvas);
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

    private static void Build(Canvas canvas)
    {
        GameObject root = Panel("SettingsPanel", canvas.transform, Dim);
        Stretch(root);

        // Anywhere off the card closes it, which is what everyone tries first.
        Button behind = root.AddComponent<Button>();
        behind.targetGraphic = root.GetComponent<Image>();
        behind.onClick.AddListener(Close);

        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
        rect.anchoredPosition = Vector2.zero;

        // The card itself swallows taps, so a miss inside it does not close.
        card.AddComponent<Button>().transition = Selectable.Transition.None;

        Label(card.transform, "Settings", 46f, Color.white, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, -40f, CardWidth - 80f, 58f);

        // ---- sound ----------------------------------------------------------
        GameObject row = Panel("SoundRow", card.transform, Sunk);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.sizeDelta = new Vector2(CardWidth - 90f, 104f);
        rowRect.anchoredPosition = new Vector2(0f, -130f);

        Label(row.transform, "Sound", 32f, Color.white, FontStyles.Normal,
              TextAlignmentOptions.Left, -110f, -26f, 300f, 44f);

        Label(row.transform, "Tiles, words, and the end of a round", 22f, Fainter,
              FontStyles.Normal, TextAlignmentOptions.Left, -110f, -62f, 420f, 30f);

        GameObject button = Panel("SoundSwitch", row.transform, Sound.Muted ? Off : Amber);
        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.pivot = new Vector2(1f, 0.5f);
        buttonRect.sizeDelta = new Vector2(150f, 62f);
        buttonRect.anchoredPosition = new Vector2(-24f, 0f);

        switchFace = button.GetComponent<Image>();

        Button toggle = button.AddComponent<Button>();
        toggle.targetGraphic = switchFace;
        toggle.onClick.AddListener(ToggleSound);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform),
                                            typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(button.transform, false);

        switchLabel = labelGo.GetComponent<TextMeshProUGUI>();
        switchLabel.fontSize = 28f;
        switchLabel.fontStyle = FontStyles.Bold;
        switchLabel.alignment = TextAlignmentOptions.Center;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        PaintSwitch();

        Label(card.transform,
              "Scrabby also follows the phone's own volume.",
              22f, Fainter, FontStyles.Italic, TextAlignmentOptions.Center,
              0f, -262f, CardWidth - 90f, 30f);

        // ---- and out --------------------------------------------------------
        GameObject done = Panel("Done", card.transform, Cream);
        RectTransform doneRect = done.GetComponent<RectTransform>();
        doneRect.anchorMin = doneRect.anchorMax = new Vector2(0.5f, 0f);
        doneRect.pivot = new Vector2(0.5f, 0f);
        doneRect.sizeDelta = new Vector2(CardWidth - 90f, 88f);
        doneRect.anchoredPosition = new Vector2(0f, 32f);

        Button close = done.AddComponent<Button>();
        close.targetGraphic = done.GetComponent<Image>();
        close.onClick.AddListener(Close);

        Label(done.transform, "Done", 32f, Ink, FontStyles.Bold,
              TextAlignmentOptions.Center, 0f, 0f, CardWidth - 90f, 88f, true);
    }

    private static void ToggleSound()
    {
        Sound.Muted = !Sound.Muted;

        // Said out loud when it comes back on, so the answer to "is it
        // working?" is the sound itself.
        if (!Sound.Muted)
            Sound.Play(Sound.WordAccepted);

        PaintSwitch();
    }

    private static void PaintSwitch()
    {
        if (switchFace != null)
            switchFace.color = Sound.Muted ? Off : Amber;

        if (switchLabel != null)
        {
            switchLabel.text = Sound.Muted ? "Off" : "On";
            switchLabel.color = Sound.Muted ? Faint : Ink;
        }
    }

    private static void Close()
    {
        switchLabel = null;
        switchFace = null;

        GameObject panel = GameObject.Find("SettingsPanel");

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
