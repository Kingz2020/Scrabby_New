using System;
using TMPro;
using UnityEngine;
using Firebase.Auth;
using Firebase.Database;
using System.Collections.Generic;

public class PreGamePanel : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField displayNameInput;
    [SerializeField] private TMP_InputField roomCodeInput;

    [Header("UI")]
    [SerializeField] private GameObject authSection;
    [SerializeField] private GameObject lobbySection;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI signedInAsText;

    private DatabaseReference dbRoot;

    private FirebaseAuth auth;
    private FirebaseUser user;

    [Serializable]
    public class RoomPlayerData
    {
        public string uid;
        public string displayName;
    }

    [System.Serializable]
    public class UserData
    {
        public string email;
        public string displayName;
        public long createdAt;
        public long lastSeenAt;
        public string currentRoomId;
        public string currentMatchId;
        public string presenceState;
    }

    [Serializable]
    public class RoomData
    {
        public string code;
        public string hostUid;
        public string hostDisplayName;
        public string guestUid;
        public string guestDisplayName;
        public string status;
        public long createdAtUnix;
    }

    private void Start()
    {
        InitializeFirebase();
    
        string dbUrl = "https://partyscrabby-default-rtdb.europe-west1.firebasedatabase.app/";
        dbRoot = FirebaseDatabase.GetInstance(dbUrl).RootReference;
    }


    private void InitializeFirebase()
    {
        auth = FirebaseAuth.DefaultInstance;
        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    private void AuthStateChanged(object sender, EventArgs eventArgs)
    {
        if (auth.CurrentUser != user)
        {
            bool signedIn = user != auth.CurrentUser && auth.CurrentUser != null;

            if (!signedIn && user != null)
            {
                SetStatus("Signed out.");
            }

            user = auth.CurrentUser;

            if (signedIn)
            {
                string shownName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
                SetStatus("Signed in.");
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;
            }
            else
            {
                if (signedInAsText != null)
                    signedInAsText.text = "Not signed in";
            }

            RefreshUI();
        }
    }

    private void OnDestroy()
    {
        if (auth != null)
        {
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }
    }

    public void OnRegisterPressed()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        string displayName = displayNameInput.text.Trim();

        Debug.Log("[PregamePanel] Register button pressed.");

        if (auth == null)
        {
            SetStatus("Firebase Auth not initialized.");
            Debug.LogError("[PregamePanel] auth is NULL.");
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an email.");
            Debug.LogWarning("[PregamePanel] Email is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter a password.");
            Debug.LogWarning("[PregamePanel] Password is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            SetStatus("Enter a display name.");
            Debug.LogWarning("[PregamePanel] Display name is empty.");
            return;
        }

        SetStatus("Registering...");
        Debug.Log("[PregamePanel] Trying to register email: " + email);

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                Debug.LogError("[PregamePanel] Register canceled.");
                RunOnMainThread(() => SetStatus("Register canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                string errorMessage = GetFirebaseErrorMessage(task.Exception);
                Debug.LogError("[PregamePanel] Register failed: " + errorMessage);
                RunOnMainThread(() => SetStatus("Register failed: " + errorMessage));
                return;
            }

            Firebase.Auth.AuthResult result = task.Result;
            FirebaseUser createdUser = result.User;
            string uid = createdUser.UserId;

            Debug.Log("[PregamePanel] User created successfully. UID: " + uid);

            Firebase.Auth.UserProfile profile = new Firebase.Auth.UserProfile
            {
                DisplayName = displayName
            };

            createdUser.UpdateUserProfileAsync(profile).ContinueWith(profileTask =>
            {
                if (profileTask.IsCanceled)
                {
                    Debug.LogWarning("[PregamePanel] Profile update canceled.");
                    RunOnMainThread(() => SetStatus("User created, name update canceled."));
                    return;
                }

                if (profileTask.IsFaulted)
                {
                    string profileError = GetFirebaseErrorMessage(profileTask.Exception);
                    Debug.LogError("[PregamePanel] Profile update failed: " + profileError);
                    RunOnMainThread(() => SetStatus("User created, name update failed: " + profileError));
                    return;
                }

                Debug.Log("[PregamePanel] Profile updated successfully.");

                UserData userData = new UserData
                {
                    email = createdUser.Email,
                    displayName = displayName,
                    createdAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    lastSeenAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    currentRoomId = "",
                    currentMatchId = "",
                    presenceState = "online"
                };

                string json = JsonUtility.ToJson(userData);

                dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(json).ContinueWith(dbTask =>
                {
                    if (dbTask.IsCanceled)
                    {
                        Debug.LogError("[PregamePanel] Database write canceled.");
                        RunOnMainThread(() => SetStatus("User created, but DB write canceled."));
                        return;
                    }

                    if (dbTask.IsFaulted)
                    {
                        string dbError = GetFirebaseErrorMessage(dbTask.Exception);
                        Debug.LogError("[PregamePanel] Database write failed: " + dbError);
                        RunOnMainThread(() => SetStatus("User created, but DB write failed: " + dbError));
                        return;
                    }

                    Debug.Log("[PregamePanel] User profile saved to database.");

                    RunOnMainThread(() =>
                    {
                        SetStatus("Registered successfully.");
                        RefreshUI();
                    });
                });
            });
        });
    }

    private string GetFirebaseErrorMessage(Exception exception)
    {
        if (exception == null)
            return "Unknown error";

        AggregateException aggregate = exception as AggregateException;
        if (aggregate != null)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                Firebase.FirebaseException firebaseEx = inner as Firebase.FirebaseException;
                if (firebaseEx != null)
                {
                    return firebaseEx.Message + " (Code: " + firebaseEx.ErrorCode + ")";
                }

                if (!string.IsNullOrWhiteSpace(inner.Message))
                {
                    return inner.Message;
                }
            }
        }

        return exception.Message;
    }

    private void SetStatus(string message)
    {
        Debug.Log("[PregamePanel STATUS] " + message);

        if (statusText != null)
            statusText.text = message;
        else
            Debug.LogWarning("[PregamePanel] statusText is not assigned in Inspector.");
    }


    public void OnLoginPressed()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an email.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter a password.");
            return;
        }

        SetStatus("Logging in...");

        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Login canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                RunOnMainThread(() => SetStatus("Login failed: " + task.Exception?.GetBaseException().Message));
                return;
            }

            FirebaseUser signedInUser = task.Result.User;

            RunOnMainThread(() =>
            {
                string shownName = string.IsNullOrWhiteSpace(signedInUser.DisplayName)
                    ? signedInUser.Email
                    : signedInUser.DisplayName;

                SetStatus("Login successful.");
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                RefreshUI();
            });
        });
    }

    public void OnCreateRoomPressed()
    {
        if (!IsSignedIn())
        {
            SetStatus("You must be logged in first.");
            return;
        }

        string roomCode = GenerateRoomCode();
        string hostName = GetBestDisplayName();

        RoomData room = new RoomData
        {
            code = roomCode,
            hostUid = auth.CurrentUser.UserId,
            hostDisplayName = hostName,
            guestUid = "",
            guestDisplayName = "",
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        string json = JsonUtility.ToJson(room);

        SetStatus("Creating room...");

        dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(json).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Create room canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                RunOnMainThread(() => SetStatus("Create room failed: " + task.Exception?.GetBaseException().Message));
                return;
            }

            RunOnMainThread(() =>
            {
                roomCodeInput.text = roomCode;
                SetStatus("Room created: " + roomCode);
            });
        });
    }

    public void OnJoinRoomPressed()
    {
        if (!IsSignedIn())
        {
            SetStatus("You must be logged in first.");
            return;
        }

        string roomCode = roomCodeInput.text.Trim().ToUpper();

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            SetStatus("Enter a room code.");
            return;
        }

        SetStatus("Joining room...");

        dbRoot.Child("rooms").Child(roomCode).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Join room canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                RunOnMainThread(() => SetStatus("Join room failed: " + task.Exception?.GetBaseException().Message));
                return;
            }

            DataSnapshot snapshot = task.Result;

            if (!snapshot.Exists)
            {
                RunOnMainThread(() => SetStatus("Room not found."));
                return;
            }

            string json = snapshot.GetRawJsonValue();
            RoomData room = JsonUtility.FromJson<RoomData>(json);

            if (room == null)
            {
                RunOnMainThread(() => SetStatus("Room data invalid."));
                return;
            }

            if (!string.IsNullOrEmpty(room.guestUid))
            {
                RunOnMainThread(() => SetStatus("Room is already full."));
                return;
            }

            room.guestUid = auth.CurrentUser.UserId;
            room.guestDisplayName = GetBestDisplayName();
            room.status = "full";

            string updatedJson = JsonUtility.ToJson(room);

            dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(updatedJson).ContinueWith(updateTask =>
            {
                if (updateTask.IsCanceled)
                {
                    RunOnMainThread(() => SetStatus("Join update canceled."));
                    return;
                }

                if (updateTask.IsFaulted)
                {
                    RunOnMainThread(() => SetStatus("Join update failed: " + updateTask.Exception?.GetBaseException().Message));
                    return;
                }

                RunOnMainThread(() =>
                {
                    SetStatus("Joined room: " + roomCode);
                });
            });
        });
    }

    public void OnLogoutPressed()
    {
        if (auth == null)
            return;

        auth.SignOut();
        SetStatus("Logged out.");
        RefreshUI();
    }

    private bool IsSignedIn()
    {
        return auth != null && auth.CurrentUser != null;
    }

    private string GetBestDisplayName()
    {
        if (auth == null || auth.CurrentUser == null)
            return "Unknown";

        if (!string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName))
            return auth.CurrentUser.DisplayName;

        if (!string.IsNullOrWhiteSpace(displayNameInput.text))
            return displayNameInput.text.Trim();

        return auth.CurrentUser.Email;
    }

    private void RefreshUI()
    {
        bool signedIn = IsSignedIn();

        if (authSection != null)
            authSection.SetActive(!signedIn);

        if (lobbySection != null)
            lobbySection.SetActive(signedIn);
    }

   

    private void RunOnMainThread(Action action)
    {
        UnityMainThreadDispatcher.Enqueue(action);
    }

    private string GenerateRoomCode(int length = 6)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[length];

        for (int i = 0; i < length; i++)
        {
            code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
        }

        return new string(code);
    }
}