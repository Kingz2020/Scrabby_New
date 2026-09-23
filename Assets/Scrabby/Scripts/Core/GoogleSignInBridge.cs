using System;
using UnityEngine;

// The Unity end of signing in with Google.
//
// Android shows the account sheet and hands back an ID token
// (ScrabbyGoogleSignIn.java); this turns that token into a signed-in Firebase
// player. Nobody types an email address and nobody invents a password.
//
// The answer comes back through UnitySendMessage, which needs a GameObject
// with this exact name to shout at, so one is made on first use and kept for
// the life of the app.
public class GoogleSignInBridge : MonoBehaviour
{
    // The project's WEB client id, from google-services.json (client_type 3).
    // Not the Android client id: Google mints the token for the web client,
    // and the Android one fails in a way that reads as "no accounts found".
    //
    // Not a secret - it ships in every copy of every app that signs in with
    // Google, and is useless without the signing certificate it is tied to.
    public const string WebClientId =
        "926763938521-g9p46rthqolhts3cpt35t8oskjt1vrua.apps.googleusercontent.com";

    private const string ObjectName = "GoogleSignInBridge";

    private static GoogleSignInBridge instance;
    private Action<string> onToken;
    private Action<string> onError;

    public static bool Available
    {
        get
        {
            return Application.platform == RuntimePlatform.Android &&
                   !WebClientId.StartsWith("PASTE_");
        }
    }

    // Asks Android for an account. Exactly one of the two callbacks is called,
    // on the main thread; a player who backs out of the sheet arrives at
    // onError, which is a cancellation rather than a fault.
    public static void SignIn(Action<string> token, Action<string> failed)
    {
        if (!Available)
        {
            if (failed != null)
                failed("Google sign-in is not set up in this build.");

            return;
        }

        if (instance == null)
        {
            GameObject go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GoogleSignInBridge>();
        }

        instance.onToken = token;
        instance.onError = failed;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaClass bridge = new AndroidJavaClass("com.kingz.scrabby.ScrabbyGoogleSignIn"))
            {
                bridge.CallStatic("signIn", activity, WebClientId);
            }
        }
        catch (Exception ex)
        {
            instance.OnGoogleError("could not reach Android: " + ex.Message);
        }
#endif
    }

    // ---- called from Java, by name. Do not rename without renaming there. ----

    public void OnGoogleToken(string token)
    {
        Action<string> waiting = onToken;

        onToken = null;
        onError = null;

        if (waiting != null)
            waiting(token);
    }

    public void OnGoogleError(string message)
    {
        Action<string> waiting = onError;

        onToken = null;
        onError = null;

        Debug.Log("[GOOGLE] " + message);

        if (waiting != null)
            waiting(message);
    }
}
