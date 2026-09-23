using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The layer the tutorial happens on: a dimmed screen with a hole cut in it,
// a caption, and a hand that points and taps.
//
// The hole is not a mask or a shader - it is four dim panels arranged around
// the rectangle being pointed at, which leaves that rectangle untouched and,
// when the player's turn comes, the only thing on screen they can tap. The
// same four panels swallow every tap that lands outside it, so a guided step
// cannot be wandered away from.
//
// This class knows nothing about Scrabby. It points at rectangles and says
// sentences; TutorialFlow decides which ones.
public class TutorialOverlay : MonoBehaviour
{
    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.72f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Amber = new Color(0.88f, 0.70f, 0.30f, 1f);

    private const float RingThickness = 5f;
    private const float CaptionWidth = 880f;

    private RectTransform root;
    private Image[] shades = new Image[4];      // top, bottom, left, right
    private Image[] ring = new Image[4];
    private Image swallowHole;                  // covers the hole when the player must watch

    private RectTransform captionBox;
    private TextMeshProUGUI captionText;

    private RectTransform hand;
    private CanvasGroup handGroup;

    private GameObject tapCatcher;
    private bool tapped;
    private bool skipPressed;

    private Rect hole;
    private bool hasHole;
    private bool letThemTouch;
    private bool dimmed = true;
    private GameObject markers;

    public System.Action OnSkip;

    // ------------------------------------------------------------- building --

    public static TutorialOverlay Open()
    {
        Canvas canvas = TopCanvas();

        if (canvas == null)
        {
            Debug.LogError("[TUTORIAL] No canvas to run on.");
            return null;
        }

        GameObject old = GameObject.Find("TutorialOverlay");

        if (old != null)
            Destroy(old);

        GameObject go = new GameObject("TutorialOverlay", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        // Its own canvas, sorted above everything, so nothing the game shows
        // later can appear over the tutorial that is explaining it.
        Canvas own = go.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 500;
        go.AddComponent<GraphicRaycaster>();

        TutorialOverlay overlay = go.AddComponent<TutorialOverlay>();
        overlay.Build();

        return overlay;
    }

    private static Canvas TopCanvas()
    {
        Canvas best = null;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas &&
                (best == null || canvas.sortingOrder >= best.sortingOrder))
            {
                best = canvas;
            }
        }

        return best;
    }

    private void Build()
    {
        root = GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        for (int i = 0; i < 4; i++)
        {
            shades[i] = Block("Shade", Dim);
            shades[i].raycastTarget = true;
        }

        for (int i = 0; i < 4; i++)
        {
            ring[i] = Block("Ring", Amber);
            ring[i].raycastTarget = false;
        }

        swallowHole = Block("Swallow", new Color(0f, 0f, 0f, 0f));
        swallowHole.raycastTarget = true;

        BuildCaption();
        BuildHand();
        BuildTapCatcher();
        BuildSkip();

        NoSpotlight();
    }

    private Image Block(string name, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

        Image image = go.GetComponent<Image>();
        image.color = colour;

        return image;
    }

    private void BuildCaption()
    {
        GameObject box = new GameObject("Caption", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(root, false);

        captionBox = box.GetComponent<RectTransform>();
        captionBox.anchorMin = captionBox.anchorMax = new Vector2(0.5f, 0.5f);
        captionBox.pivot = new Vector2(0.5f, 0.5f);
        captionBox.sizeDelta = new Vector2(CaptionWidth, 230f);

        box.GetComponent<Image>().color = Glass;

        // Words are for reading, not for catching touches. The box was an
        // Image like any other, so anything dragged under it - a tile on its
        // way to the board - was stopped at the caption.
        box.GetComponent<Image>().raycastTarget = false;

        GameObject text = new GameObject("Text", typeof(RectTransform),
                                         typeof(TextMeshProUGUI));
        text.transform.SetParent(box.transform, false);

        captionText = text.GetComponent<TextMeshProUGUI>();
        // Big enough to read at arm's length on a phone. 32 was chosen on a
        // monitor, and read as small print on the device.
        captionText.fontSize = 54f;
        captionText.color = Color.white;
        captionText.alignment = TextAlignmentOptions.Center;
        captionText.richText = true;
        captionText.raycastTarget = false;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(30f, 18f);
        textRect.offsetMax = new Vector2(-30f, -18f);

        captionBox.gameObject.SetActive(false);
    }

    private void BuildHand()
    {
        GameObject go = new GameObject("Hand", typeof(RectTransform), typeof(Image),
                                       typeof(CanvasGroup));
        go.transform.SetParent(root, false);

        Image image = go.GetComponent<Image>();
        image.sprite = Resources.Load<Sprite>("Tutorial/hand");
        image.raycastTarget = false;

        if (image.sprite == null)
        {
            // Rather a square than nothing: the tutorial still works, it just
            // points with a block.
            Debug.LogWarning("[TUTORIAL] Resources/Tutorial/hand is missing.");
            image.color = Cream;
        }

        hand = go.GetComponent<RectTransform>();
        hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);

        // The pivot is the fingertip, so telling the hand to go somewhere puts
        // the tip of the finger there and not the middle of the fist.
        hand.pivot = new Vector2(0.40f, 0.85f);
        hand.sizeDelta = new Vector2(116f, 145f);
        hand.localRotation = Quaternion.Euler(0f, 0f, -12f);

        handGroup = go.GetComponent<CanvasGroup>();
        handGroup.alpha = 0f;
        handGroup.blocksRaycasts = false;
    }

    // Invisible, full-screen, and only there while a caption is waiting to be
    // read. Built before Skip so Skip stays on top of it and still works.
    private void BuildTapCatcher()
    {
        tapCatcher = new GameObject("TapToContinue", typeof(RectTransform),
                                    typeof(Image), typeof(Button));
        tapCatcher.transform.SetParent(root, false);

        RectTransform rect = tapCatcher.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image face = tapCatcher.GetComponent<Image>();
        face.color = new Color(0f, 0f, 0f, 0f);

        Button button = tapCatcher.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(delegate { tapped = true; });

        tapCatcher.SetActive(false);
    }

    private void BuildSkip()
    {
        GameObject go = new GameObject("Skip", typeof(RectTransform), typeof(Image),
                                       typeof(Button));
        go.transform.SetParent(root, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(150f, 62f);
        rect.anchoredPosition = new Vector2(-34f, -42f);

        Image face = go.GetComponent<Image>();
        face.color = new Color(1f, 1f, 1f, 0.12f);

        GameObject label = new GameObject("Label", typeof(RectTransform),
                                          typeof(TextMeshProUGUI));
        label.transform.SetParent(go.transform, false);

        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        text.text = "Skip";
        text.fontSize = 34f;
        text.color = Cream;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(delegate
        {
            // Ends any wait in progress as well, or Skip would appear to do
            // nothing until the screen was tapped too.
            skipPressed = true;

            if (OnSkip != null)
                OnSkip();
        });
    }

    // ------------------------------------------------------------ the light --

    // Everything dark, nothing singled out.
    // Out of the way entirely - no dimming, no frame - while still able to
    // say something. The round being played out is the lesson; covering it in
    // grey to talk over it would be teaching with the lights off.
    public void Undim()
    {
        dimmed = false;
        hasHole = false;
        hole = new Rect(0f, 0f, 0f, 0f);

        Layout();
    }

    public void NoSpotlight()
    {
        dimmed = true;
        hasHole = false;
        hole = new Rect(0f, 0f, 0f, 0f);

        Layout();
    }

    public void Spotlight(RectTransform target, float padding = 18f)
    {
        SpotlightAll(new RectTransform[] { target }, padding);
    }

    // One rectangle around several things. The step where the player drags
    // their own tiles needs the rack and the squares lit at once, and two
    // holes in one sheet of dimming is a great deal of arithmetic for a
    // frame that is only there to say "this part of the screen".
    public void SpotlightAll(RectTransform[] targets, float padding = 18f)
    {
        Rect union = new Rect();
        bool any = false;

        if (targets != null)
        {
            foreach (RectTransform target in targets)
            {
                if (target == null)
                    continue;

                Rect one = LocalRectOf(target);

                if (!any)
                {
                    union = one;
                    any = true;
                    continue;
                }

                float xMin = Mathf.Min(union.xMin, one.xMin);
                float yMin = Mathf.Min(union.yMin, one.yMin);
                float xMax = Mathf.Max(union.xMax, one.xMax);
                float yMax = Mathf.Max(union.yMax, one.yMax);

                union = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
        }

        if (!any)
        {
            NoSpotlight();
            return;
        }

        hole = new Rect(union.xMin - padding, union.yMin - padding,
                        union.width + padding * 2f, union.height + padding * 2f);
        hasHole = true;
        dimmed = true;

        Layout();
    }

    // Whether a tap reaches the game. False while the hand is doing the
    // tapping, true when it is the player's turn.
    public void LetThemTouch(bool allowed)
    {
        letThemTouch = allowed;
        Layout();
    }

    // A frame drawn around something without dimming anything - for the two
    // squares a tile is meant to go on, which sit inside a lit area that also
    // holds the rack.
    public void Mark(RectTransform target)
    {
        if (target == null)
            return;

        if (markers == null)
        {
            markers = new GameObject("Markers", typeof(RectTransform));
            markers.transform.SetParent(root, false);

            RectTransform rect = markers.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
        }

        Rect where = LocalRectOf(target);
        float t = RingThickness;

        Rect[] bars =
        {
            new Rect(where.xMin - t, where.yMax, where.width + t * 2f, t),
            new Rect(where.xMin - t, where.yMin - t, where.width + t * 2f, t),
            new Rect(where.xMin - t, where.yMin, t, where.height),
            new Rect(where.xMax, where.yMin, t, where.height)
        };

        foreach (Rect bar in bars)
        {
            GameObject go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(markers.transform, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = go.GetComponent<Image>();
            image.color = Amber;
            image.raycastTarget = false;

            Place(image, bar);
        }
    }

    public void ClearMarks()
    {
        if (markers != null)
            Destroy(markers);

        markers = null;
    }

    private Rect LocalRectOf(RectTransform target)
    {
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);

        Canvas canvas = root.GetComponentInParent<Canvas>();
        Camera camera = canvas != null &&
                        canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;

        foreach (Vector3 corner in corners)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
            Vector2 local;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    root, screen, camera, out local))
            {
                continue;
            }

            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    private void Layout()
    {
        float w = root.rect.width;
        float h = root.rect.height;

        float left = -w / 2f, right = w / 2f, bottom = -h / 2f, top = h / 2f;

        Rect lit = hasHole ? hole : new Rect(right, top, 0f, 0f);

        Place(shades[0], new Rect(left, lit.yMax, w, top - lit.yMax));
        Place(shades[1], new Rect(left, bottom, w, lit.yMin - bottom));
        Place(shades[2], new Rect(left, lit.yMin, lit.xMin - left, lit.height));
        Place(shades[3], new Rect(lit.xMax, lit.yMin, right - lit.xMax, lit.height));

        Place(swallowHole, lit);

        // With something lit, everything outside it is off limits and the lit
        // part is the player's, or not, depending on whose turn it is to act.
        // With nothing lit, the whole screen goes one way or the other.
        foreach (Image shade in shades)
        {
            shade.enabled = dimmed;
            shade.raycastTarget = dimmed && (hasHole || !letThemTouch);
        }

        swallowHole.enabled = !letThemTouch;
        swallowHole.raycastTarget = !letThemTouch;

        // A thin amber frame, so the lit rectangle reads as chosen rather than
        // as a hole someone forgot to dim.
        float t = RingThickness;

        Place(ring[0], new Rect(lit.xMin - t, lit.yMax, lit.width + t * 2f, t));
        Place(ring[1], new Rect(lit.xMin - t, lit.yMin - t, lit.width + t * 2f, t));
        Place(ring[2], new Rect(lit.xMin - t, lit.yMin, t, lit.height));
        Place(ring[3], new Rect(lit.xMax, lit.yMin, t, lit.height));

        foreach (Image bar in ring)
            bar.enabled = hasHole && dimmed;
    }

    private static void Place(Image image, Rect rect)
    {
        RectTransform t = image.rectTransform;

        t.sizeDelta = new Vector2(Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        t.anchoredPosition = new Vector2(rect.xMin + rect.width / 2f,
                                         rect.yMin + rect.height / 2f);
    }

    // ------------------------------------------------------------- speaking --

    // Where the words go. Auto puts them beside whatever is lit, which suits
    // the menu; Top pins them out of the way, which is what the board needs -
    // a caption sitting over the middle of the board covers the very squares
    // the player is being asked to build a word on.
    public enum Where
    {
        Auto,
        Top,
        Bottom
    }

    public void Say(string words)
    {
        Say(words, Where.Auto);
    }

    public void Say(string words, Where where)
    {
        captionBox.gameObject.SetActive(true);
        captionText.text = words;

        // Roughly how tall the words will be: about 34 characters to a line
        // at this size in this width, and a line is about 52 tall. Guessed
        // rather than measured because the box is set before the text has
        // been laid out.
        float height = 140f + 66f * Mathf.Floor(words.Length / 27f);
        captionBox.sizeDelta = new Vector2(CaptionWidth, Mathf.Min(560f, height));

        float h = root.rect.height;
        float halfCaption = captionBox.sizeDelta.y / 2f;
        float y;

        if (where == Where.Top)
        {
            y = h / 2f - halfCaption - 36f;
        }
        else if (where == Where.Bottom)
        {
            y = -h / 2f + halfCaption + 36f;
        }
        else
        {
            // Under the lit rectangle, or over it when that would fall off the
            // bottom of the screen.
            y = hasHole ? hole.yMin - halfCaption - 40f : 0f;

            if (hasHole && y - halfCaption < -h / 2f + 40f)
                y = hole.yMax + halfCaption + 40f;
        }

        captionBox.anchoredPosition = new Vector2(0f, y);
    }

    // ------------------------------------------------------- the player's pace --

    private const string TapHint =
        "\n<size=70%><color=#FFFFFF80>Tap anywhere to continue</color></size>";

    // Words that ask for nothing but reading. They stay until the player
    // taps, which is the whole point: "too fast" was a caption that left
    // before somebody had finished it.
    public IEnumerator SayAndWait(string words, Where where)
    {
        Say(words + TapHint, where);

        // A beat before listening, so the tap that finished the last step is
        // not taken as the answer to this one.
        float settle = 0f;

        while (settle < 0.35f)
        {
            settle += Time.deltaTime;
            yield return null;
        }

        tapped = false;
        tapCatcher.SetActive(true);
        tapCatcher.transform.SetSiblingIndex(root.childCount - 2);

        while (!tapped && !skipPressed && tapCatcher != null)
            yield return null;

        tapCatcher.SetActive(false);
    }

    // The hand points at a real button and taps the air above it until the
    // player presses the button themselves - which runs whatever that button
    // always runs. The tutorial never presses anything for them.
    public IEnumerator WaitForPress(Button button, System.Func<bool> giveUp)
    {
        if (button == null)
            yield break;

        bool pressed = false;
        UnityEngine.Events.UnityAction watcher = delegate { pressed = true; };
        button.onClick.AddListener(watcher);

        yield return MoveHandTo(button.transform as RectTransform);

        float sincePulse = 1f;

        while (!pressed && (giveUp == null || !giveUp()))
        {
            sincePulse += Time.deltaTime;

            if (sincePulse > 1.3f)
            {
                sincePulse = 0f;
                StartCoroutine(Tap());
            }

            yield return null;
        }

        button.onClick.RemoveListener(watcher);
    }

    public void Hush()
    {
        captionBox.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- hands --

    public void HandAt(RectTransform target)
    {
        Rect where = LocalRectOf(target);
        hand.anchoredPosition = new Vector2(where.center.x, where.center.y);
        handGroup.alpha = 1f;
    }

    public void PutHandAt(Vector2 local)
    {
        hand.anchoredPosition = local;
        handGroup.alpha = 1f;
    }

    // Where a point on this overlay is in the world, so a tile can be carried
    // along with the hand.
    public Vector3 WorldPointOf(Vector2 local)
    {
        return root.TransformPoint(local);
    }

    public IEnumerator MoveHandTo(RectTransform target, float seconds = 0.65f)
    {
        if (target == null)
            yield break;

        Rect where = LocalRectOf(target);

        yield return MoveHandToPoint(new Vector2(where.center.x, where.center.y), seconds);
    }

    public IEnumerator MoveHandToPoint(Vector2 to, float seconds = 0.65f)
    {
        // Coming from nowhere in particular the first time: fade it in where
        // it is going rather than flying it in from a corner.
        if (handGroup.alpha < 0.5f)
        {
            hand.anchoredPosition = to + new Vector2(70f, -90f);

            float fade = 0f;

            while (fade < 1f)
            {
                fade += Time.deltaTime * 4f;
                handGroup.alpha = Mathf.Clamp01(fade);
                yield return null;
            }
        }

        Vector2 from = hand.anchoredPosition;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, seconds);

            // Ease in and out, because a hand does not move at a constant
            // speed and a cursor that does looks like a machine.
            float e = t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

            hand.anchoredPosition = Vector2.Lerp(from, to, Mathf.Clamp01(e));
            yield return null;
        }

        hand.anchoredPosition = to;
    }

    // The press itself: the hand dips towards the screen and comes back.
    public IEnumerator Tap()
    {
        Vector3 up = Vector3.one;
        Vector3 down = new Vector3(0.84f, 0.84f, 1f);

        yield return Scale(up, down, 0.10f);
        yield return new WaitForSeconds(0.06f);
        yield return Scale(down, up, 0.12f);
    }

    private IEnumerator Scale(Vector3 from, Vector3 to, float seconds)
    {
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / seconds;
            hand.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }

        hand.localScale = to;
    }

    public void HideHand()
    {
        handGroup.alpha = 0f;
    }

    public Vector2 HandPoint
    {
        get { return hand.anchoredPosition; }
    }

    public Vector2 PointOf(RectTransform target)
    {
        Rect where = LocalRectOf(target);
        return new Vector2(where.center.x, where.center.y);
    }

    public void Close()
    {
        Destroy(gameObject);
    }
}
