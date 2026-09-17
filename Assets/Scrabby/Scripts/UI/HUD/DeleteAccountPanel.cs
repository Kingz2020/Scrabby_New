using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Delete my account", asked properly.
//
// Google requires that an app where people can make an account also lets them
// delete it, and deleting is the one action in Scrabby that cannot be undone.
// So it asks for the password again: it proves whoever is holding the phone is
// the owner of the account, and it is a moment's pause before something
// final. Firebase asks for it anyway when a sign-in is more than a few
// minutes old.
//
// Built in code like the other cards here, so the warning lives next to what
// it warns about.
public static class DeleteAccountPanel
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.62f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.95f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.74f);
    private static readonly Color Fainter = new Color(1f, 1f, 1f, 0.45f);
    private static readonly Color Danger = new Color(0.78f, 0.24f, 0.20f, 1f);
    private static readonly Color Field = new Color(1f, 1f, 1f, 0.10f);

    private const string PanelName = "DeleteAccountPanel";
    private const float CardWidth = 860f;
    private const float CardHeight = 700f;

    private static readonly string[] Warnings =
    {
        "Your account, your alias and your notifications are deleted.",
        "Your invitations disappear, sent and received.",
        "Games you played stay with the other player, but your name is taken off them.",
        "Nothing can be recovered afterwards, and the same email can start again from scratch."
    };

    private static TMP_InputField password;
    private static TextMeshProUGUI status;
    private static Button confirm;

    public static void Show(string email, Action<string> onConfirm)
    {
        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("[ACCOUNT] No canvas to show the delete card on.");
            return;
        }

        Close();
        Build(canvas, email, onConfirm);
    }

    // Called while the deletion runs, and again if it fails.
    public static void SetStatus(string message, bool bad = false)
    {
        if (status == null)
            return;

        status.text = message;
        status.color = bad ? new Color(1f, 0.55f, 0.5f, 1f) : Faint;
    }

    public static void SetBusy(bool busy)
    {
        if (confirm != null)
            confirm.interactable = !busy;

        if (password != null)
            password.interactable = !busy;
    }

    public static void Close()
    {
        GameObject existing = GameObject.Find(PanelName);

        if (existing != null)
            UnityEngine.Object.Destroy(existing);

        password = null;
        status = null;
        confirm = null;
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

    private static void Build(Canvas canvas, string email, Action<string> onConfirm)
    {
        GameObject root = Panel(PanelName, canvas.transform, Dim);
        Stretch(root);

        GameObject card = Panel("Card", root.transform, Glass);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
        rect.anchoredPosition = Vector2.zero;

        float y = -40f;

        Label(card.transform, "Delete your account", 46f, Color.white,
              FontStyles.Bold, TextAlignmentOptions.Center, 0f, y, CardWidth - 80f, 56f);
        y -= 64f;

        Label(card.transform, string.IsNullOrEmpty(email) ? "" : email, 26f, Fainter,
              FontStyles.Normal, TextAlignmentOptions.Center, 0f, y, CardWidth - 80f, 34f);
        y -= 54f;

        foreach (string warning in Warnings)
        {
            Label(card.transform, "-  " + warning, 27f, Faint, FontStyles.Normal,
                  TextAlignmentOptions.TopLeft, 10f, y, CardWidth - 120f, 62f);
            y -= 62f;
        }

        y -= 16f;

        Label(card.transform, "Type your password to confirm", 26f, Fainter,
              FontStyles.Normal, TextAlignmentOptions.Left, 10f, y, CardWidth - 120f, 32f);
        y -= 40f;

        password = InputField(card.transform, 0f, y, CardWidth - 120f, 74f);
        y -= 96f;

        status = LabelOf(Label(card.transform, "", 24f, Faint, FontStyles.Normal,
                               TextAlignmentOptions.Center, 0f, y, CardWidth - 100f, 32f));
        y -= 48f;

        // Cancel sits on the left and is the plain one; the red button is the
        // one that does something irreversible, so it is never the easy tap.
        TextButton(card.transform, "Cancel", -180f, y, 300f,
                   new Color(1f, 1f, 1f, 0.12f), Color.white, Close);

        confirm = TextButton(card.transform, "Delete my account", 180f, y, 300f,
                             Danger, Color.white, () =>
        {
            string typed = password != null ? password.text : "";

            if (string.IsNullOrEmpty(typed))
            {
                SetStatus("Type your password first.", true);
                return;
            }

            if (onConfirm != null)
                onConfirm(typed);
        });
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

    private static GameObject Label(
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

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        return go;
    }

    private static TextMeshProUGUI LabelOf(GameObject go)
    {
        return go.GetComponent<TextMeshProUGUI>();
    }

    private static Button TextButton(
        Transform parent, string caption, float x, float y, float width,
        Color face, Color ink, Action onClick)
    {
        GameObject go = Panel("Button", parent, face);
        go.AddComponent<Button>();

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, 78f);
        rect.anchoredPosition = new Vector2(x, y);

        GameObject label = Label(go.transform, caption, 30f, ink, FontStyles.Bold,
                                 TextAlignmentOptions.Center, 0f, 0f, width, 78f);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.onClick.AddListener(() => onClick());

        return button;
    }

    // A password box, assembled the way Unity assembles one: the field, a
    // viewport that clips the text, and the text and its placeholder inside.
    private static TMP_InputField InputField(
        Transform parent, float x, float y, float width, float height)
    {
        GameObject go = Panel("PasswordField", parent, Field);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        GameObject area = new GameObject("Text Area",
                                         typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);

        RectTransform areaRect = area.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(20f, 8f);
        areaRect.offsetMax = new Vector2(-20f, -8f);

        TextMeshProUGUI placeholder = LabelOf(
            Label(area.transform, "Password", 28f, Fainter, FontStyles.Italic,
                  TextAlignmentOptions.Left, 0f, 0f, width - 40f, height - 16f));
        FillParent(placeholder.rectTransform);

        TextMeshProUGUI text = LabelOf(
            Label(area.transform, "", 28f, Cream, FontStyles.Normal,
                  TextAlignmentOptions.Left, 0f, 0f, width - 40f, height - 16f));
        FillParent(text.rectTransform);

        // Added last: the component wires itself up on enable, and wants its
        // parts to exist by then.
        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRect;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.contentType = TMP_InputField.ContentType.Password;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.caretColor = Cream;
        field.selectionColor = new Color(0.945f, 0.878f, 0.733f, 0.35f);
        field.targetGraphic = go.GetComponent<Image>();
        field.text = "";

        return field;
    }

    private static void FillParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
