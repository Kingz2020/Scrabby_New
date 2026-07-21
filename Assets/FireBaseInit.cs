using System;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;

public class FirebaseInit : MonoBehaviour
{
    public static FirebaseAuth Auth;
    public static FirebaseDatabase Database;
    public static bool IsReady;

    private FirebaseUser currentUser;

    void Start()
    {
        Debug.Log("FirebaseInit Start on " + gameObject.name);


        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var status = task.Result;
            if (status != DependencyStatus.Available)
            {
                Debug.LogError("Could not resolve Firebase dependencies: " + status);
                return;
            }

            Auth = FirebaseAuth.DefaultInstance;
            //Database = FirebaseDatabase.DefaultInstance;
            Database = FirebaseDatabase.GetInstance("https://partyscrabby-default-rtdb.europe-west1.firebasedatabase.app/");
            Auth.StateChanged += AuthStateChanged;
            IsReady = true;

            Debug.Log("Firebase ready.");
            AuthStateChanged(this, EventArgs.Empty);
        });
    }

    private void AuthStateChanged(object sender, EventArgs eventArgs)
    {
        if (Auth == null) return;

        if (Auth.CurrentUser != currentUser)
        {
            bool signedIn = currentUser != Auth.CurrentUser && Auth.CurrentUser != null && Auth.CurrentUser.IsValid();

            if (!signedIn && currentUser != null)
                Debug.Log("Signed out: " + currentUser.UserId);

            currentUser = Auth.CurrentUser;

            if (signedIn)
                Debug.Log("Auth state signed in: " + currentUser.Email + " | " + currentUser.UserId);
            else
                Debug.Log("Auth state: no user signed in.");
        }
    }

    void OnDestroy()
    {
        if (Auth != null)
            Auth.StateChanged -= AuthStateChanged;
    }
}