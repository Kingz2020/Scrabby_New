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
        dbRoot = FirebaseDatabase.DefaultInstance.RootReference;
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

        if (string.IsNullOrWhiteSpace(displayName))
        {
            SetStatus("Enter a display name.");
            return;
        }

        SetStatus("Registering...");

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Registration canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                RunOnMainThread(() => SetStatus("Registration failed: " + task.Exception?.GetBaseException().Message));
                return;
            }

            FirebaseUser createdUser = task.Result.User;

            /*UserProfile profile = new UserProfile
            {
                DisplayName = displayName
            };*/
            Firebase.Auth.UserProfile profile = new Firebase.Auth.UserProfile();
            profile.DisplayName = displayName;

            createdUser.UpdateUserProfileAsync(profile).ContinueWith(profileTask =>
            {
                if (profileTask.IsCanceled)
                {
                    RunOnMainThread(() => SetStatus("User created, but name update canceled."));
                    return;
                }

                if (profileTask.IsFaulted)
                {
                    RunOnMainThread(() => SetStatus("User created, but name update failed."));
                    return;
                }

                RunOnMainThread(() =>
                {
                    SetStatus("Registered successfully.");
                    if (signedInAsText != null)
                        signedInAsText.text = "Signed in as: " + displayName;
                    RefreshUI();
                });
            });
        });
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

    private void SetStatus(string message)
    {
        Debug.Log("[PregamePanel] " + message);

        if (statusText != null)
            statusText.text = message;
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