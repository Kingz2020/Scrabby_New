using System;
using TMPro;
using UnityEngine;
using Firebase.Auth;
using Firebase.Database;
using System.Collections.Generic;
using Firebase.Extensions;
using System.Threading.Tasks;
using UnityEngine.UI;


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
    public Button startGameButton;

    private DatabaseReference currentRoomRef;
    private string watchedRoomCode = "";

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
        public string status;      // waiting, full, in_game, finished
        public string matchId;     // empty until game starts
        public long createdAtUnix;
    }

    [Serializable]
    public class MatchData
    {
        public string matchId;
        public string roomCode;

        public string player1Uid;
        public string player1DisplayName;
        public string player2Uid;
        public string player2DisplayName;

        public string currentTurnUid;
        public string status; // waiting, active, finished

        public int turnNumber;

        public int player1Score;
        public int player2Score;

        public string boardStateJson;
        public string bagStateJson;
        public string player1RackJson;
        public string player2RackJson;

        public string lastMoveJson;

        public long createdAtUnix;
        public long updatedAtUnix;
    }

    private void Start()
    {


        if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }
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

        dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(json).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Create room canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                SetStatus("Create room failed: " + task.Exception?.GetBaseException().Message);
                Debug.LogError("[PregamePanel] Create room failed: " + task.Exception);
                return;
            }

            Debug.Log("[PregamePanel] Room created successfully: " + roomCode);

            roomCodeInput.text = roomCode;
            roomCodeInput.SetTextWithoutNotify(roomCode);
            roomCodeInput.ForceLabelUpdate();

            SetStatus("Room created: " + roomCode);
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

        var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

        SetStatus("Joining room...");

        dbRoot.Child("rooms").Child(roomCode).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Join room canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                SetStatus("Join room failed: " + task.Exception?.GetBaseException().Message);
                Debug.LogError("[PregamePanel] Join room read failed: " + task.Exception);
                return;
            }

            DataSnapshot snapshot = task.Result;

            if (!snapshot.Exists)
            {
                SetStatus("Room not found.");
                return;
            }

            string json = snapshot.GetRawJsonValue();
            RoomData room = JsonUtility.FromJson<RoomData>(json);

            if (room == null)
            {
                SetStatus("Room data invalid.");
                return;
            }

            if (!string.IsNullOrEmpty(room.guestUid))
            {
                SetStatus("Room is already full.");
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
                    SetStatus("Join update canceled.");
                    return;
                }

                if (updateTask.IsFaulted)
                {
                    SetStatus("Join update failed: " + updateTask.Exception?.GetBaseException().Message);
                    Debug.LogError("[PregamePanel] Join room write failed: " + updateTask.Exception);
                    return;
                }

                Debug.Log("[PregamePanel] Joined room successfully: " + roomCode);
                SetStatus("Joined room successfully: " + roomCode);

            }, uiScheduler);

        }, uiScheduler);
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


    public void WatchRoom(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            Debug.LogWarning("[PregamePanel] WatchRoom called with empty room code.");
            return;
        }

        roomCode = roomCode.Trim().ToUpper();

        StopWatchingRoom();

        watchedRoomCode = roomCode;
        currentRoomRef = dbRoot.Child("rooms").Child(roomCode);
        currentRoomRef.ValueChanged += OnRoomValueChanged;

        Debug.Log("[PregamePanel] Now watching room: " + roomCode);
    }

    public void StopWatchingRoom()
    {
        if (currentRoomRef != null)
        {
            currentRoomRef.ValueChanged -= OnRoomValueChanged;
            Debug.Log("[PregamePanel] Stopped watching room: " + watchedRoomCode);
            currentRoomRef = null;
        }

        watchedRoomCode = "";

        if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }
    }

    private void OnRoomValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError("[PregamePanel] Room listener error: " + args.DatabaseError.Message);
            return;
        }

        if (args.Snapshot == null || !args.Snapshot.Exists)
        {
            Debug.LogWarning("[PregamePanel] Room snapshot missing or room deleted.");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[PregamePanel] Room snapshot JSON was empty.");
            return;
        }

        RoomData room = JsonUtility.FromJson<RoomData>(json);

        if (room == null)
        {
            Debug.LogError("[PregamePanel] Failed to parse RoomData from JSON.");
            return;
        }

        Debug.Log("[PregamePanel] Room changed. Code=" + room.code + ", Status=" + room.status);

        bool roomFull = !string.IsNullOrEmpty(room.guestUid);

        if (roomFull)
        {
            Debug.Log("[PregamePanel] Room is full. A game can begin.");

            if (startGameButton != null)
            {
                bool isHost = IsSignedIn() && auth.CurrentUser != null && room.hostUid == auth.CurrentUser.UserId;
                startGameButton.interactable = isHost;
                startGameButton.image.color = isHost ? Color.green : Color.gray;
            }
        }
        else
        {
            Debug.Log("[PregamePanel] Room is waiting for another player.");

            if (startGameButton != null)
            {
                startGameButton.interactable = false;
                startGameButton.image.color = Color.white;
            }
        }

        if (!string.IsNullOrEmpty(room.matchId) && room.status == "in_game")
        {
            Debug.Log("[PregamePanel] Match has started. Match ID: " + room.matchId);
        }
    }

    private void OnDestroy()
    {
        StopWatchingRoom();

        if (auth != null)
        {
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }
    }

    public void OnStartGamePressed()
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

            var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

            SetStatus("Starting game...");

            dbRoot.Child("rooms").Child(roomCode).GetValueAsync().ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    SetStatus("Start game canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    SetStatus("Start game failed: " + task.Exception?.GetBaseException().Message);
                    Debug.LogError("[PregamePanel] Start game read failed: " + task.Exception);
                    return;
                }

                DataSnapshot snapshot = task.Result;

                if (!snapshot.Exists)
                {
                    SetStatus("Room not found.");
                    return;
                }

                string json = snapshot.GetRawJsonValue();
                RoomData room = JsonUtility.FromJson<RoomData>(json);

                if (room == null)
                {
                    SetStatus("Room data invalid.");
                    return;
                }

                if (room.hostUid != auth.CurrentUser.UserId)
                {
                    SetStatus("Only the host can start the game.");
                    return;
                }

                if (string.IsNullOrEmpty(room.guestUid))
                {
                    SetStatus("Need a second player before starting.");
                    return;
                }

                if (!string.IsNullOrEmpty(room.matchId))
                {
                    SetStatus("Game already started.");
                    return;
                }

                string matchId = dbRoot.Child("matches").Push().Key;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                MatchData match = new MatchData
                {
                    matchId = matchId,
                    roomCode = room.code,

                    player1Uid = room.hostUid,
                    player1DisplayName = room.hostDisplayName,
                    player2Uid = room.guestUid,
                    player2DisplayName = room.guestDisplayName,

                    currentTurnUid = room.hostUid,
                    status = "active",
                    turnNumber = 1,

                    player1Score = 0,
                    player2Score = 0,

                    boardStateJson = "",
                    bagStateJson = "",
                    player1RackJson = "",
                    player2RackJson = "",
                    lastMoveJson = "",

                    createdAtUnix = now,
                    updatedAtUnix = now
                };

                string matchJson = JsonUtility.ToJson(match);

                dbRoot.Child("matches").Child(matchId).SetRawJsonValueAsync(matchJson).ContinueWith(matchTask =>
                {
                    if (matchTask.IsCanceled)
                    {
                        SetStatus("Match creation canceled.");
                        return;
                    }

                    if (matchTask.IsFaulted)
                    {
                        SetStatus("Match creation failed: " + matchTask.Exception?.GetBaseException().Message);
                        Debug.LogError("[PregamePanel] Match write failed: " + matchTask.Exception);
                        return;
                    }

                    room.matchId = matchId;
                    room.status = "in_game";

                    string updatedRoomJson = JsonUtility.ToJson(room);

                    dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(updatedRoomJson).ContinueWith(roomTask =>
                    {
                        if (roomTask.IsCanceled)
                        {
                            SetStatus("Room update canceled after match creation.");
                            return;
                        }

                        if (roomTask.IsFaulted)
                        {
                            SetStatus("Room update failed: " + roomTask.Exception?.GetBaseException().Message);
                            Debug.LogError("[PregamePanel] Room update failed: " + roomTask.Exception);
                            return;
                        }

                        Debug.Log("[PregamePanel] Game started successfully. Match ID: " + matchId);
                        SetStatus("Game started successfully. Match ID: " + matchId);

                    }, uiScheduler);

                }, uiScheduler);

            }, uiScheduler);
        
    }
}
