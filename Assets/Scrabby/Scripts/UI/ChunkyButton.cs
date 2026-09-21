using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A button that looks like a thick key and squashes like a sponge when
// pressed. Every button in the game wears it.
//
// The look is the 9-slice art in Resources/Buttons: white shaded in greys, or
// blue for getting around and for the chosen tab. The lettering is Lilita
// One. Put on in code, onto buttons that already exist and already do their
// jobs, so nothing in the scene has to change and every click still goes
// where it always went.
//
// The press: the button squashes down onto its bottom edge and widens a
// little, as something soft would, then springs back past where it started
// and settles. Squashed from the bottom rather than the middle, so it reads
// as being pushed down into the screen rather than shrinking in place.
public class ChunkyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Face
    {
        White,
        Blue
    }

    // The art is 150 tall: 122 of face and 28 of the thickness below it.
    private const float ArtHeight = 150f;
    private const float BaseShare = 28f / 150f;

    private static readonly Color Ink = new Color(0.173f, 0.204f, 0.251f, 1f);
    private static readonly Color Navy = new Color(0.055f, 0.220f, 0.431f, 1f);

    // How far it squashes, and how it springs back. Tuned for about a fifth
    // of overshoot and a third of a second to settle: soft, not wobbly.
    private const float SquashDown = 0.12f;
    private const float Widen = 0.05f;
    private const float Stiffness = 420f;
    private const float Damping = 18f;

    private static TMP_FontAsset lilita;
    private static Sprite whiteArt;
    private static Sprite blueArt;

    private Face face;
    private TextMeshProUGUI label;
    private RectTransform icon;

    private Button button;
    private Image image;
    private RectTransform rect;
    private Vector3 restPosition;
    private bool resting = true;
    private bool held;
    private float squash;          // 0 at rest, 1 fully pressed, below 0 overshooting
    private float speed;
    private Coroutine motion;

    // --------------------------------------------------------------- dress --

    // label is the text to show, or null to keep whatever text it has; icon
    // is a picture to show instead of text.
    //
    // Safe to call before the button has been laid out: it fits itself again
    // whenever its size changes, so a button sized by a layout group, or on a
    // panel that has never been shown, comes out right once it is.
    public static ChunkyButton Dress(Button button, Face face, string label, Sprite icon)
    {
        if (button == null)
            return null;

        RectTransform rect = button.transform as RectTransform;
        Image image = button.image != null ? button.image : button.GetComponent<Image>();

        if (rect == null || image == null)
            return null;

        if (Art(face) == null)
        {
            Debug.LogWarning("[BUTTONS] The button art is missing from Resources/Buttons.");
            return null;
        }

        image.type = Image.Type.Sliced;
        image.preserveAspect = false;

        // The squash is the feedback now. A tint on top of it made the white
        // face go grey for a moment, which read as a fault.
        button.transition = Selectable.Transition.None;

        TextMeshProUGUI text = null;
        RectTransform iconRect = null;

        if (icon != null)
        {
            // Whatever text it had goes, and the picture takes its place.
            foreach (TextMeshProUGUI old in button.GetComponentsInChildren<TextMeshProUGUI>(true))
                old.gameObject.SetActive(false);

            iconRect = AddIcon(button.transform, icon);
        }
        else
        {
            text = StyleLabel(button, label);
        }

        ChunkyButton chunky = button.GetComponent<ChunkyButton>();

        if (chunky == null)
            chunky = button.gameObject.AddComponent<ChunkyButton>();

        chunky.button = button;
        chunky.image = image;
        chunky.rect = rect;
        chunky.face = face;
        chunky.label = text;
        chunky.icon = iconRect;

        if (text != null)
            ColourLabel(text, face);

        chunky.Fit();
        chunky.Keep();

        return chunky;
    }

    // Dresses a button and says which colour it is in one go, for the many
    // places that have a button in hand and nothing else to decide.
    public static ChunkyButton Dress(Button button, Face face)
    {
        return Dress(button, face, null, null);
    }

    // The same thickness, light and press, on a button that keeps its own
    // sprite, colour and lettering - the main menu's tile and glass, which
    // the white and blue keys do not suit.
    //
    // The thickness is the button's own sprite again, darker, peeking out
    // below it; the light is ChunkyShade on the face. Pressing sinks the face
    // onto its thickness as well as squashing it.
    public static ChunkyButton Deepen(Button button)
    {
        if (button == null)
            return null;

        RectTransform rect = button.transform as RectTransform;
        Image image = button.image != null ? button.image : button.GetComponent<Image>();

        if (rect == null || image == null)
            return null;

        ChunkyButton chunky = button.GetComponent<ChunkyButton>();

        if (chunky == null)
            chunky = button.gameObject.AddComponent<ChunkyButton>();

        chunky.button = button;
        chunky.image = image;
        chunky.rect = rect;
        chunky.ownArt = true;

        if (button.GetComponent<ChunkyShade>() == null)
            button.gameObject.AddComponent<ChunkyShade>();

        // A colour tint on press would fight the squash, as on the keys.
        button.transition = Selectable.Transition.None;

        chunky.MakeDepth();
        chunky.SyncDepth();

        return chunky;
    }

    // ------------------------------------------------------------- depth --

    // Own-art buttons: the thickness is a sibling drawn just before the
    // button, so it sits behind it, and it stays put while the face moves.
    private bool ownArt;
    private Image depth;
    private Vector2 restAnchored;

    private float DepthPixels
    {
        get
        {
            float height = rect != null ? rect.rect.height : 0f;
            return Mathf.Clamp(height * 0.10f, 6f, 16f);
        }
    }

    private void MakeDepth()
    {
        if (depth != null || transform.parent == null)
            return;

        GameObject go = new GameObject(name + " (depth)", typeof(RectTransform),
                                       typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(transform.parent, false);
        go.transform.SetSiblingIndex(transform.GetSiblingIndex());

        // Not a member of any layout group the button is in: it follows the
        // button rather than taking a place of its own in the row.
        go.GetComponent<LayoutElement>().ignoreLayout = true;

        depth = go.GetComponent<Image>();
        depth.raycastTarget = false;
        go.SetActive(gameObject.activeSelf);
    }

    private void SyncDepth()
    {
        if (depth == null || image == null || rect == null)
            return;

        depth.sprite = image.sprite;
        depth.type = image.type;
        depth.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        depth.preserveAspect = image.preserveAspect;

        // The underside: the face's own colour, much darker, and never
        // fainter than a shadow needs to be to show on glass.
        Color c = image.color;
        depth.color = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.55f,
                                Mathf.Max(c.a, 0.45f));

        RectTransform d = depth.rectTransform;
        d.anchorMin = rect.anchorMin;
        d.anchorMax = rect.anchorMax;
        d.pivot = rect.pivot;
        d.sizeDelta = rect.sizeDelta;
        d.localScale = Vector3.one;

        Vector2 at = resting ? rect.anchoredPosition : restAnchored;
        d.anchoredPosition = at - new Vector2(0f, DepthPixels);
    }

    private void OnEnable()
    {
        if (depth != null)
            depth.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        if (depth != null)
            Destroy(depth.gameObject);
    }

    // White or blue, for a button whose colour says something - a tab or a
    // chip, blue while it is the one chosen. Only the face and the ink
    // change; the shape, the size and the press stay as they are.
    public void SetFace(Face newFace)
    {
        if (newFace == face)
            return;

        face = newFace;

        if (label != null)
            ColourLabel(label, face);

        Keep();
    }

    private static Sprite Art(Face face)
    {
        if (face == Face.White)
        {
            if (whiteArt == null)
                whiteArt = Resources.Load<Sprite>("Buttons/Button_white");

            return whiteArt;
        }

        if (blueArt == null)
            blueArt = Resources.Load<Sprite>("Buttons/Button_blue");

        return blueArt;
    }

    private static void ColourLabel(TextMeshProUGUI text, Face face)
    {
        if (face == Face.White)
        {
            text.color = Ink;
            text.outlineWidth = 0f;
        }
        else
        {
            text.color = Color.white;
            text.outlineColor = Navy;
            text.outlineWidth = 0.22f;
        }
    }

    private static TextMeshProUGUI StyleLabel(Button button, string label)
    {
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (text == null)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform),
                                           typeof(TextMeshProUGUI));
            go.transform.SetParent(button.transform, false);
            text = go.GetComponent<TextMeshProUGUI>();
        }

        text.gameObject.SetActive(true);

        if (label != null)
            text.text = label;

        TMP_FontAsset font = Lilita();

        if (font != null)
            text.font = font;

        // Capitals by the font style rather than by changing the words, so a
        // label the game rewrites later - "Find an opponent" - is in capitals
        // too.
        text.fontStyle = FontStyles.UpperCase;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.raycastTarget = false;

        RectTransform r = text.rectTransform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);

        return text;
    }

    private static RectTransform AddIcon(Transform parent, Sprite icon)
    {
        Transform existing = parent.Find("Icon");

        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject("Icon", typeof(RectTransform), typeof(Image));

        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;

        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);

        return r;
    }

    // Lilita One - rounded, heavy, and free to ship under the SIL Open Font
    // License. Made into a font asset when first needed rather than baked in
    // the editor, so there is one file to keep and nothing to regenerate.
    private static TMP_FontAsset Lilita()
    {
        if (lilita != null)
            return lilita;

        Font font = Resources.Load<Font>("Fonts/LilitaOne");

        if (font == null)
        {
            Debug.LogWarning("[BUTTONS] Resources/Fonts/LilitaOne is missing; keeping the old font.");
            return null;
        }

        lilita = TMP_FontAsset.CreateFontAsset(font);

        if (lilita != null)
            lilita.name = "LilitaOne (made at runtime)";

        return lilita;
    }

    // ----------------------------------------------------------------- fit --

    // Everything that depends on the button's height, worked out again
    // whenever the height changes.
    private void Fit()
    {
        if (ownArt || rect == null || image == null)
            return;

        float height = rect.rect.height;

        if (height <= 1f)
            return;

        // The art is drawn 150 tall; this scales its borders to the button's
        // own height, so a short button gets the same proportions as a tall
        // one rather than ends that overlap in the middle.
        image.pixelsPerUnitMultiplier = ArtHeight / height;

        float baseHeight = height * BaseShare;
        float faceHeight = height - baseHeight;

        if (label != null)
        {
            label.fontSizeMax = faceHeight * 0.55f;

            // On the face, not the whole button: the bottom of the button is
            // its thickness, and letters centred over both sit visibly low.
            RectTransform r = label.rectTransform;
            r.offsetMin = new Vector2(height * 0.30f, baseHeight + 2f);
            r.offsetMax = new Vector2(-height * 0.30f, -4f);
        }

        if (icon != null)
        {
            float size = faceHeight * 0.66f;
            icon.sizeDelta = new Vector2(size, size);

            // The middle of the face, which is above the middle of the button
            // by half the thickness.
            icon.anchoredPosition = new Vector2(0f, baseHeight / 2f);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        Fit();
    }

    // The button's look is this component's to keep. Several screens tint
    // their buttons - green when ready, gold while busy, grey when not
    // allowed - and a tint over white art comes out as mud. So whatever they
    // set goes back to the art, and the one state that still shows is
    // whether it can be pressed: faded when it cannot.
    private void LateUpdate()
    {
        if (ownArt)
            SyncDepth();
        else
            Keep();
    }

    private void Keep()
    {
        if (ownArt || image == null)
            return;

        Sprite art = Art(face);

        if (art != null && image.sprite != art)
            image.sprite = art;

        if (image.type != Image.Type.Sliced)
            image.type = Image.Type.Sliced;

        bool live = button == null || button.interactable;

        image.color = live ? Color.white : new Color(1f, 1f, 1f, 0.5f);

        if (label != null)
        {
            Color ink = face == Face.White ? Ink : Color.white;
            ink.a = live ? 1f : 0.6f;
            label.color = ink;
        }
    }

    // --------------------------------------------------------------- press --

    private void Awake()
    {
        button = GetComponent<Button>();
        rect = transform as RectTransform;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable)
            return;

        held = true;
        Move();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!held)
            return;

        held = false;
        Move();
    }

    // Dragging a finger off the button lets it go, as a real one would.
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!held)
            return;

        held = false;
        Move();
    }

    private void OnDisable()
    {
        // Put back exactly, so a button hidden mid-press does not come back
        // squashed the next time its screen opens.
        if (motion != null)
            StopCoroutine(motion);

        motion = null;
        held = false;
        squash = 0f;
        speed = 0f;
        Apply();
        resting = true;

        if (depth != null)
            depth.gameObject.SetActive(false);
    }

    private void Move()
    {
        if (resting)
        {
            restPosition = transform.localPosition;

            if (rect != null)
                restAnchored = rect.anchoredPosition;

            resting = false;
        }

        if (motion == null)
            motion = StartCoroutine(Spring());
    }

    private IEnumerator Spring()
    {
        while (true)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float target = held ? 1f : 0f;

            if (held)
            {
                // Going down is quick and does not bounce: a finger pressing
                // is firm, and the give is in the coming back.
                squash = Mathf.MoveTowards(squash, target, dt / 0.07f);
                speed = 0f;
            }
            else
            {
                float accel = -Stiffness * (squash - target) - Damping * speed;
                speed += accel * dt;
                squash += speed * dt;
            }

            Apply();

            bool settled = !held && Mathf.Abs(squash) < 0.002f && Mathf.Abs(speed) < 0.01f;

            if (settled)
            {
                squash = 0f;
                speed = 0f;
                Apply();
                resting = true;
                motion = null;
                yield break;
            }

            yield return null;
        }
    }

    private void Apply()
    {
        if (rect == null)
            return;

        float sy = 1f - SquashDown * squash;
        float sx = 1f + Widen * squash;

        transform.localScale = new Vector3(sx, sy, 1f);

        if (resting)
            return;

        // Squashed from the bottom edge: moved down by exactly what the
        // scale takes off below the pivot, so the bottom stays put.
        float height = rect.rect.height;
        float drop = rect.pivot.y * height * (1f - sy);

        // An own-art button also sinks onto its thickness, most of the way,
        // and rises off it a little on the rebound.
        if (ownArt)
            drop += squash * DepthPixels * 0.8f;

        transform.localPosition = restPosition - new Vector3(0f, drop, 0f);
    }
}
