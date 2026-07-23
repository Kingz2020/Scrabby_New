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
    [SerializeField] private GameObject pregamePanelRoot;
    [SerializeField] private GameObject gameplayRoot;

    [SerializeField] private GameObject pregamePanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject gameOverPanel;

    //private bool hasEnteredGameplay = false;

    private DatabaseReference dbRoot;

    private FirebaseAuth auth;
    private FirebaseUser user;
    public Button startGameButton;

    private DatabaseReference currentMatchRef;
    private DatabaseReference currentRoomRef;
    private string watchedRoomCode = "";
    private string watchedMatchId = "";

    [Serializable]
    public class TileData
    {
        public string letter;
        public int value;
        public string id;
    }

    [Serializable]
    public class BagStateData
    {
        public List<TileData> tiles = new List<TileData>();
    }

    [Serializable]
    public class RackStateData
    {
        public List<TileData> tiles = new List<TileData>();
    }

    [Serializable]
    public class BoardCellData
    {
        public int x;
        public int y;
        public bool occupied;
        public TileData tile;
    }

    [Serializable]
    public class BoardStateData
    {
        public int width;
        public int height;
        public List<BoardCellData> cells = new List<BoardCellData>();
    }

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
        public string hostUid;
        public string guestUid;
        public string player1Uid;
        public string player2Uid;
        public string player1DisplayName;
        public string player2DisplayName;
        public int player1Score;
        public int player2Score;
        public int turnNumber;
        public string currentTurnUid;
        public string status;

        public string boardStateJson;
        public string bagStateJson;
        public string player1RackJson;
        public string player2RackJson;

        public long createdAt;
    }

    private void Awake()
    {
        if (pregamePanel != null) pregamePanel.SetActive(true);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
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

    public void WatchMatch(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            Debug.LogWarning("[PregamePanel] WatchMatch called with empty matchId.");
            return;
        }

        matchId = matchId.Trim();

        StopWatchingMatch();

        watchedMatchId = matchId;
        currentMatchRef = dbRoot.Child("matches").Child(matchId);
        currentMatchRef.ValueChanged += OnMatchValueChanged;

        Debug.Log("[PregamePanel] Now watching match: " + matchId);
    }
    public void StopWatchingMatch()
    {
        if (currentMatchRef != null)
        {
            currentMatchRef.ValueChanged -= OnMatchValueChanged;
            Debug.Log("[PregamePanel] Stopped watching match: " + watchedMatchId);
            currentMatchRef = null;
        }

        watchedMatchId = "";
    }

    private void OnMatchValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError("[PregamePanel] Match listener error: " + args.DatabaseError.Message);
            return;
        }

        if (args.Snapshot == null || !args.Snapshot.Exists)
        {
            Debug.LogWarning("[PregamePanel] Match snapshot missing or match deleted.");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[PregamePanel] Match snapshot JSON was empty.");
            return;
        }

        MatchData match = JsonUtility.FromJson<MatchData>(json);

        if (match == null)
        {
            Debug.LogError("[PregamePanel] Failed to parse MatchData from JSON.");
            return;
        }

        Debug.Log("[PregamePanel] Match changed. MatchId=" + match.matchId +
                  ", Status=" + match.status +
                  ", Turn=" + match.turnNumber +
                  ", CurrentTurnUid=" + match.currentTurnUid);

        Debug.Log("[PregamePanel] Scores: " +
                  match.player1DisplayName + "=" + match.player1Score + ", " +
                  match.player2DisplayName + "=" + match.player2Score);

        // PUT THE DEBUG PARSING HERE
        BoardStateData board = null;
        BagStateData bag = null;
        RackStateData p1Rack = null;
        RackStateData p2Rack = null;

        if (!string.IsNullOrEmpty(match.boardStateJson))
            board = JsonUtility.FromJson<BoardStateData>(match.boardStateJson);

        if (!string.IsNullOrEmpty(match.bagStateJson))
            bag = JsonUtility.FromJson<BagStateData>(match.bagStateJson);

        if (!string.IsNullOrEmpty(match.player1RackJson))
            p1Rack = JsonUtility.FromJson<RackStateData>(match.player1RackJson);

        if (!string.IsNullOrEmpty(match.player2RackJson))
            p2Rack = JsonUtility.FromJson<RackStateData>(match.player2RackJson);

        Debug.Log("[PregamePanel] Board cells: " + (board != null && board.cells != null ? board.cells.Count : 0));
        Debug.Log("[PregamePanel] Bag remaining: " + (bag != null && bag.tiles != null ? bag.tiles.Count : 0));
        Debug.Log("[PregamePanel] P1 rack: " + GetRackDebugString(p1Rack));
        Debug.Log("[PregamePanel] P2 rack: " + GetRackDebugString(p2Rack));

        if (match.status == "active")
        {
            Debug.Log("[PregamePanel] Match is active.");
        }
        else if (match.status == "finished")
        {
            Debug.Log("[PregamePanel] Match is finished.");
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
            WatchRoom(roomCode);
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
                WatchRoom(roomCode);

            }, uiScheduler);

        }, uiScheduler);
    }

    public void OnLogoutPressed()
    {
        if (auth == null)
            return;

        StopWatchingRoom();
        StopWatchingMatch();

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

       /* if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }*/
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

        bool roomFull = !string.IsNullOrEmpty(room.guestUid) && room.status == "full";
        bool isHost = IsSignedIn() && auth != null && auth.CurrentUser != null && room.hostUid == auth.CurrentUser.UserId;

        Debug.Log("[PregamePanel] roomFull=" + roomFull +
                  ", isHost=" + isHost +
                  ", hostUid=" + room.hostUid +
                  ", currentUid=" + (auth != null && auth.CurrentUser != null ? auth.CurrentUser.UserId : "null"));

        if (roomFull)
        {
            Debug.Log("[PregamePanel] Room is full. A game can begin.");
        }
        else
        {
            Debug.Log("[PregamePanel] Room is waiting for another player.");
        }

        if (startGameButton != null)
        {
            bool canStart = roomFull;
            Debug.Log("[PregamePanel] Setting startGameButton.interactable = " + canStart);
            startGameButton.interactable = canStart;
            startGameButton.image.color = canStart ? Color.green : Color.gray;
            Debug.Log("[PregamePanel] AFTER SET: interactable=" + startGameButton.interactable);
        }
        else
        {
            Debug.LogWarning("[PregamePanel] startGameButton is NULL in OnRoomValueChanged!");
        }

        Debug.Log("[PregamePanel] AFTER RunOnMainThread call queued");

        if (!string.IsNullOrEmpty(room.matchId) && room.status == "in_game")
        {
            Debug.Log("[PregamePanel] Match started. Match ID: " + room.matchId);
            WatchMatch(room.matchId);
            EnterGameplayMode();
            return;
            /*RunOnMainThread(() =>
            {
                EnterGameplayMode();
            });*/
        }
    }

    private void EnterGameplayMode()
    {
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        StopWatchingRoom();
        StopWatchingMatch();

        if (auth != null)
        {
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }
    }

    public void OnStartGamePressed()
    {
        Debug.Log("[PregamePanel] OnStartGamePressed CALLED");

        if (auth == null || auth.CurrentUser == null)
        {
            SetStatus("You must be signed in.");
            return;
        }

        string uid = auth.CurrentUser.UserId;
        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";

        Debug.Log("[PregamePanel] Start pressed by uid=" + uid + ", roomCode=" + roomCode);

        if (string.IsNullOrEmpty(roomCode))
        {
            Debug.LogWarning("[PregamePanel] Start blocked: roomCode is empty.");
            SetStatus("Enter a room code first.");
            return;
        }

        DatabaseReference roomRef = dbRoot.Child("rooms").Child(roomCode);

        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            Debug.Log("[PregamePanel] Start callback entered");

            if (task.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to load room: " + task.Exception);
                SetStatus("Failed to load room.");
                return;
            }

            if (!task.IsCompleted || task.Result == null || !task.Result.Exists)
            {
                Debug.LogWarning("[PregamePanel] Start callback: room missing.");
                SetStatus("Room not found.");
                return;
            }

            string json = task.Result.GetRawJsonValue();
            Debug.Log("[PregamePanel] Start callback raw room json: " + json);

            RoomData room = JsonUtility.FromJson<RoomData>(json);

            if (room == null)
            {
                Debug.LogError("[PregamePanel] Could not parse room data.");
                SetStatus("Could not parse room data.");
                return;
            }

            Debug.Log("[PregamePanel] Start callback room parsed. hostUid=" + room.hostUid +
                      ", guestUid=" + room.guestUid +
                      ", status=" + room.status);

            if (string.IsNullOrEmpty(room.guestUid))
            {
                Debug.LogWarning("[PregamePanel] Start blocked: guestUid empty.");
                SetStatus("Cannot start yet. Waiting for guest.");
                return;
            }

            string matchId = dbRoot.Child("matches").Push().Key;
            Debug.Log("[PregamePanel] Generated matchId=" + matchId);

            BagStateData bag = CreateInitialBag();
            RackStateData player1Rack = DrawTiles(bag, 7);
            RackStateData player2Rack = DrawTiles(bag, 7);
            BoardStateData board = CreateInitialBoard();

            MatchData match = new MatchData
            {
                matchId = matchId,
                roomCode = roomCode,
                hostUid = room.hostUid,
                guestUid = room.guestUid,
                player1Uid = room.hostUid,
                player2Uid = room.guestUid,
                player1DisplayName = room.hostDisplayName,
                player2DisplayName = room.guestDisplayName,
                player1Score = 0,
                player2Score = 0,
                turnNumber = 1,
                currentTurnUid = room.hostUid,
                status = "active",
                boardStateJson = ToJson(board),
                bagStateJson = ToJson(bag),
                player1RackJson = ToJson(player1Rack),
                player2RackJson = ToJson(player2Rack),
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            room.matchId = matchId;
            room.status = "in_game";

            string matchJson = JsonUtility.ToJson(match);
            string roomJson = JsonUtility.ToJson(room);

            Debug.Log("[PregamePanel] Writing match JSON: " + matchJson);
            Debug.Log("[PregamePanel] Writing room JSON: " + roomJson);

            dbRoot.Child("matches").Child(matchId).SetRawJsonValueAsync(matchJson)
                .ContinueWithOnMainThread(matchWriteTask =>
                {
                    if (matchWriteTask.IsFaulted)
                    {
                        Debug.LogError("[PregamePanel] Failed writing match: " + matchWriteTask.Exception);
                        SetStatus("Failed to write match.");
                        return;
                    }

                    Debug.Log("[PregamePanel] Match node written successfully.");

                    dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(roomJson)
                        .ContinueWithOnMainThread(roomWriteTask =>
                        {
                            if (roomWriteTask.IsFaulted)
                            {
                                Debug.LogError("[PregamePanel] Failed writing room: " + roomWriteTask.Exception);
                                SetStatus("Failed to update room.");
                                return;
                            }

                            Debug.Log("[PregamePanel] Room node written successfully.");

                            SetStatus("Game started.");
                            Debug.Log("[PregamePanel] Match created: " + matchId);
                            Debug.Log("[PregamePanel] Player1 rack: " + GetRackDebugString(player1Rack));
                            Debug.Log("[PregamePanel] Player2 rack: " + GetRackDebugString(player2Rack));
                            Debug.Log("[PregamePanel] Bag tiles remaining: " + bag.tiles.Count);
                        });
                });
        });
    }

    private string NewTileId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private void AddTiles(BagStateData bag, string letter, int value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            bag.tiles.Add(new TileData
            {
                letter = letter,
                value = value,
                id = NewTileId()
            });
        }
    }

    private BagStateData CreateInitialBag()
    {
        BagStateData bag = new BagStateData();

        AddTiles(bag, "A", 1, 9);
        AddTiles(bag, "B", 3, 2);
        AddTiles(bag, "C", 3, 2);
        AddTiles(bag, "D", 2, 4);
        AddTiles(bag, "E", 1, 12);
        AddTiles(bag, "F", 4, 2);
        AddTiles(bag, "G", 2, 3);
        AddTiles(bag, "H", 4, 2);
        AddTiles(bag, "I", 1, 9);
        AddTiles(bag, "J", 8, 1);
        AddTiles(bag, "K", 5, 1);
        AddTiles(bag, "L", 1, 4);
        AddTiles(bag, "M", 3, 2);
        AddTiles(bag, "N", 1, 6);
        AddTiles(bag, "O", 1, 8);
        AddTiles(bag, "P", 3, 2);
        AddTiles(bag, "Q", 10, 1);
        AddTiles(bag, "R", 1, 6);
        AddTiles(bag, "S", 1, 4);
        AddTiles(bag, "T", 1, 6);
        AddTiles(bag, "U", 1, 4);
        AddTiles(bag, "V", 4, 2);
        AddTiles(bag, "W", 4, 2);
        AddTiles(bag, "X", 8, 1);
        AddTiles(bag, "Y", 4, 2);
        AddTiles(bag, "Z", 10, 1);

        ShuffleTiles(bag.tiles);
        return bag;
    }

    private void ShuffleTiles(List<TileData> tiles)
    {
        for (int i = tiles.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            TileData temp = tiles[i];
            tiles[i] = tiles[j];
            tiles[j] = temp;
        }
    }

    private RackStateData DrawTiles(BagStateData bag, int count)
    {
        RackStateData rack = new RackStateData();

        int drawCount = Mathf.Min(count, bag.tiles.Count);

        for (int i = 0; i < drawCount; i++)
        {
            rack.tiles.Add(bag.tiles[0]);
            bag.tiles.RemoveAt(0);
        }

        return rack;
    }

    private BoardStateData CreateInitialBoard(int width = 15, int height = 15)
    {
        BoardStateData board = new BoardStateData
        {
            width = width,
            height = height
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                board.cells.Add(new BoardCellData
                {
                    x = x,
                    y = y,
                    occupied = false,
                    tile = null
                });
            }
        }

        return board;
    }

    private string ToJson<T>(T obj)
    {
        return JsonUtility.ToJson(obj);
    }

    private string GetRackDebugString(RackStateData rack)
    {
        if (rack == null || rack.tiles == null || rack.tiles.Count == 0)
            return "(empty)";

        List<string> parts = new List<string>();

        for (int i = 0; i < rack.tiles.Count; i++)
        {
            TileData tile = rack.tiles[i];
            parts.Add(tile.letter + tile.value);
        }

        return string.Join(", ", parts);
    }
}
