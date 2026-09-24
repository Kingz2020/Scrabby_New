using System.Collections;
using UnityEngine;

// Inviting somebody by sending them a link.
//
// Asking for a friend's email address assumes you know it. People are
// reached by phone number now - a WhatsApp message, a text - so the
// invitation is a link: tap it and the game opens on the invitation, or,
// if they have not got Scrabby yet, the web page offers it to them.
//
//     https://scrabby.gamer.free/join.html?code=ABCDEF
//
// Android is told in the manifest that Scrabby owns that address, and the
// website carries a file naming this app, so the link opens the game rather
// than a browser. Both halves have to agree or Android quietly falls back to
// the browser, which still works - the page has the store link on it.
//
// The room code is the same one the game has always used, so a link is only
// a tidier way of passing it along, and typing it in by hand still works.
public class InviteLinks : MonoBehaviour
{
    public const string Site = "https://scrabby.gamer.free";

    private const string ObjectName = "InviteLinks";

    // Held from the moment the link arrives until there is somebody signed in
    // to join with. A link can arrive before the game has finished starting,
    // or while the player is still signing in.
    private static string waitingCode;
    private static InviteLinks instance;

    public static string LinkFor(string roomCode)
    {
        // join.html rather than /join: the site serves plain files, and the
        // manifest's rule matches any path starting "/join" either way.
        return Site + "/join.html?code=" + (roomCode ?? "").Trim().ToUpperInvariant();
    }

    // -------------------------------------------------------- sending one --

    // Android's own share sheet, so the player picks WhatsApp, or a text, or
    // whatever else they use, rather than the game guessing.
    public static void Share(string roomCode, string fromName)
    {
        string link = LinkFor(roomCode);
        string who = string.IsNullOrWhiteSpace(fromName) ? "Someone" : fromName.Trim();

        string message =
            who + " has challenged you to a game of Scrabby.\n\n" +
            "Same six letters, both players - best word wins.\n\n" +
            link;

        // Copied first, always: the share sheet reaches most places, and the
        // clipboard reaches the rest.
        GUIUtility.systemCopyBuffer = message;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
            using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
            {
                intent.Call<AndroidJavaObject>("setAction",
                    intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra",
                    intentClass.GetStatic<string>("EXTRA_SUBJECT"), "A game of Scrabby");
                intent.Call<AndroidJavaObject>("putExtra",
                    intentClass.GetStatic<string>("EXTRA_TEXT"), message);

                using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>(
                           "createChooser", intent, "Invite a friend"))
                {
                    activity.Call("startActivity", chooser);
                }
            }

            return;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[INVITE] Could not open the share sheet: " + ex.Message);
        }
#endif

        Debug.Log("[INVITE] Link copied: " + link);
    }

    // ------------------------------------------------------ receiving one --

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Listen()
    {
        if (instance != null)
            return;

        GameObject go = new GameObject(ObjectName);
        DontDestroyOnLoad(go);
        instance = go.AddComponent<InviteLinks>();

        Application.deepLinkActivated += OnLink;

        // A link that started the game arrives before anything can subscribe.
        if (!string.IsNullOrEmpty(Application.absoluteURL))
            OnLink(Application.absoluteURL);
    }

    private static void OnLink(string url)
    {
        string code = CodeIn(url);

        if (string.IsNullOrEmpty(code))
            return;

        Debug.Log("[INVITE] Arrived by link, room " + code + ".");

        waitingCode = code;

        if (instance != null)
            instance.StartCoroutine(instance.JoinWhenPossible());
    }

    public static string CodeIn(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        int at = url.IndexOf("code=", System.StringComparison.OrdinalIgnoreCase);

        if (at < 0)
            return null;

        string code = url.Substring(at + 5);
        int end = code.IndexOfAny(new[] { '&', '#', ' ' });

        if (end >= 0)
            code = code.Substring(0, end);

        return code.Trim().ToUpperInvariant();
    }

    // Nothing can be joined until somebody is signed in and the panels exist,
    // and a link often arrives before either. So it waits - for a few minutes,
    // which is long enough to sign in or make an account first.
    private IEnumerator JoinWhenPossible()
    {
        float waited = 0f;

        while (waited < 300f)
        {
            if (!string.IsNullOrEmpty(waitingCode) &&
                FirebaseInit.IsReady &&
                FirebaseInit.Auth != null &&
                FirebaseInit.Auth.CurrentUser != null)
            {
                PreGamePanel pregame = FindFirstObjectByType<PreGamePanel>(FindObjectsInactive.Include);

                if (pregame != null)
                {
                    string code = waitingCode;
                    waitingCode = null;

                    Debug.Log("[INVITE] Joining room " + code + " from the link.");

                    pregame.SetRoomCodeInput(code);
                    pregame.OnJoinRoomPressed();
                    yield break;
                }
            }

            waited += Time.deltaTime;
            yield return null;
        }

        if (!string.IsNullOrEmpty(waitingCode))
            Debug.Log("[INVITE] Nobody signed in, so room " + waitingCode + " was not joined.");
    }
}
