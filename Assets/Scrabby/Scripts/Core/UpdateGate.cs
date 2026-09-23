using System.Collections;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stops a copy of the game that is too old to play with everyone else.
//
// One value in the database, config/minimumVersion - "1.1", say - and every
// copy older than that shows "Please update" and nothing else. Nothing is set
// today, so nothing is stopped. It is here for the day a change needs every
// player on the same version: new languages, where an old English-only copy
// invited into a French match would check French words against English.
//
// Only ever stops a copy it is sure about. No value, no connection, a value
// it cannot read: the game plays on. Being locked out by a bad signal on a
// train is worse than anything an old version could do.
//
// Asked at launch and again whenever the app comes back to the front, so
// raising the number reaches people who leave the game open for days.
//
// To stop old copies:  Firebase console -> Realtime Database -> add
//     config / minimumVersion = "1.1"     (a string, in quotes)
// and publish a build whose Version (Player Settings) is at least that.
public class UpdateGate : MonoBehaviour
{
    private const string Path = "config/minimumVersion";

    private static readonly Color Dim = new Color(0.01f, 0.04f, 0.08f, 0.80f);
    private static readonly Color Glass = new Color(0.039f, 0.149f, 0.267f, 0.97f);
    private static readonly Color Cream = new Color(0.945f, 0.878f, 0.733f, 1f);
    private static readonly Color Ink = new Color(0.227f, 0.173f, 0.094f, 1f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.78f);

    private GameObject wall;
    private bool asking;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        GameObject go = new GameObject("UpdateGate");
        DontDestroyOnLoad(go);
        go.AddComponent<UpdateGate>().Ask();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
            Ask();
    }

    private void Ask()
    {
        if (!asking && wall == null)
            StartCoroutine(AskWhenReady());
    }

    private IEnumerator AskWhenReady()
    {
        asking = true;

        // Firebase starts itself up elsewhere; this waits for it rather than
        // starting a second one.
        float gaveUpAt = Time.realtimeSinceStartup + 60f;

        while (!FirebaseInit.IsReady || FirebaseInit.Database == null)
        {
            if (Time.realtimeSinceStartup > gaveUpAt)
            {
                asking = false;
                yield break;
            }

            yield return null;
        }

        FirebaseInit.Database.GetReference(Path).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                asking = false;

                if (task.IsFaulted || task.IsCanceled || task.Result == null ||
                    !task.Result.Exists || task.Result.Value == null)
                {
                    return;
                }

                string minimum = task.Result.Value.ToString();

                if (IsOlder(Application.version, minimum))
                {
                    Debug.Log("[UPDATE] This is " + Application.version +
                              "; the oldest allowed is " + minimum + ".");
                    ShowWall();
                }
            });
    }

    // "1.0" against "1.1": compared number by number, so 1.10 is newer than
    // 1.9, which comparing them as text gets wrong. A part that is not a
    // number counts as 0, and a minimum that cannot be read stops nobody.
    public static bool IsOlder(string mine, string minimum)
    {
        if (string.IsNullOrEmpty(minimum))
            return false;

        string[] a = mine.Split('.');
        string[] b = minimum.Split('.');
        int parts = Mathf.Max(a.Length, b.Length);

        for (int i = 0; i < parts; i++)
        {
            int x = i < a.Length ? Number(a[i]) : 0;
            int y = i < b.Length ? Number(b[i]) : 0;

            if (x != y)
                return x < y;
        }

        return false;
    }

    private static int Number(string part)
    {
        int value;
        return int.TryParse(part.Trim(), out value) ? value : 0;
    }

    // ---------------------------------------------------------------- wall --

    // Its own canvas, above everything else in the game, taking every touch:
    // the one thing left to do is update.
    private void ShowWall()
    {
        if (wall != null)
            return;

        wall = new GameObject("UpdateWall", typeof(Canvas), typeof(CanvasScaler),
                              typeof(GraphicRaycaster));
        DontDestroyOnLoad(wall);

        Canvas canvas = wall.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = wall.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2280f);     // as the game's canvas
        scaler.matchWidthOrHeight = 0.5f;

        GameObject dim = Box("Dim", wall.transform, Dim);
        RectTransform dimRect = dim.GetComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        GameObject card = Box("Card", dim.transform, Glass);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(820f, 520f);

        Text(card.transform, "A new Scrabby is out", 48f, Color.white, FontStyles.Bold,
             0f, -60f, 740f, 70f);
        Text(card.transform,
             "This version is too old to play with everyone else.\nPlease update to keep playing.",
             30f, Faint, FontStyles.Normal, 0f, -150f, 740f, 110f);

        GameObject button = Box("Update", card.transform, Cream);
        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.sizeDelta = new Vector2(560f, 104f);
        buttonRect.anchoredPosition = new Vector2(0f, 56f);

        Text(button.transform, "Update", 38f, Ink, FontStyles.Bold, 0f, 0f, 560f, 104f, true);

        Button press = button.AddComponent<Button>();
        press.targetGraphic = button.GetComponent<Image>();
        press.onClick.AddListener(OpenStore);
        ChunkyButton.Deepen(press);
    }

    private static void OpenStore()
    {
        // The Play Store app where there is one, the website where there is not.
        string id = Application.identifier;

        if (Application.platform == RuntimePlatform.Android)
            Application.OpenURL("market://details?id=" + id);
        else
            Application.OpenURL("https://play.google.com/store/apps/details?id=" + id);
    }

    private static GameObject Box(string name, Transform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        return go;
    }

    private static void Text(Transform parent, string content, float size, Color colour,
                             FontStyles style, float x, float y, float width, float height,
                             bool fill = false)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
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
