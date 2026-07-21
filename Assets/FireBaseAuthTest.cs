using UnityEngine;
using Firebase.Auth;
using Firebase.Extensions;

public class FireBaseAuthTest : MonoBehaviour
{
    [SerializeField] private string testEmail = "mystakingz@gmail.com";
    [SerializeField] private string testPassword = "Scrabby123";

    void Start()
    {
        Debug.Log("FireBaseAuthTest Start on " + gameObject.name);

        Invoke(nameof(TestLogin), 2f);
    }

    public void TestRegister()
    {
        if (!FirebaseInit.IsReady)
        {
            Debug.LogError("Firebase not ready yet.");
            return;
        }

        var auth = FirebaseInit.Auth;

        auth.CreateUserWithEmailAndPasswordAsync(testEmail, testPassword)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError("CreateUser canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError("CreateUser error: " + task.Exception);
                    return;
                }

                var user = task.Result.User;
                Debug.Log("User created successfully: " + user.Email + " | " + user.UserId);
            });
    }

    public void TestLogin()
    {
        if (!FirebaseInit.IsReady)
        {
            Debug.LogError("Firebase not ready yet.");
            return;
        }

        var auth = FirebaseInit.Auth;

        Debug.Log("Before sign-in, CurrentUser = " + auth.CurrentUser?.UserId);

        if (auth.CurrentUser != null)
        {
            Debug.Log("User already signed in: " + auth.CurrentUser.Email + " | " + auth.CurrentUser.UserId);
            return;
        }

        auth.SignInWithEmailAndPasswordAsync(testEmail, testPassword)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError("SignIn canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError("SignIn error: " + task.Exception);
                    return;
                }

                var user = task.Result.User;
                Debug.Log("Signed in successfully: " + user.Email + " | " + user.UserId);
                Debug.Log("After sign-in, CurrentUser = " + auth.CurrentUser?.UserId);
            });
    }
} 