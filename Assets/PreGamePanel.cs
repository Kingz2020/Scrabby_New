using System;
using TMPro;
using UnityEngine;
using Firebase.Auth;
using Firebase.Database;
using System.Collections.Generic;
using Firebase.Extensions;
using System.Threading.Tasks;
using UnityEngine.UI;
using System.Collections;

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
    private bool firebaseInitialized = false;

    [SerializeField] private GameLogic gameLogic;
    private bool hasInitializedMatch = false;
    private MatchData currentMatch;

    //public event Action<RoundMove> onlineSubmissionReady;


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
    [System.Serializable]
    private class LetterInfoListWrapper
    {
        public List<LetterInfo> items;
    }


    private void Awake()
    {
        if (pregamePanel != null) pregamePanel.SetActive(true);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;
    }


    private void Start()
    {
        Debug.Log("[PreGamePanel] Start() running on GameObject: " + gameObject.name + " (EntityId: " + gameObject.GetEntityId() + ")");


        if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;

        StartCoroutine(WaitForFirebaseThenInit());
    }

    
    private void OnEnable()
    {
        if (!firebaseInitialized)
        {
            StartCoroutine(WaitForFirebaseThenInit());
        }
    }

    private IEnumerator WaitForFirebaseThenInit()
    {
        yield return new WaitUntil(() => FirebaseInit.IsReady);

        if (firebaseInitialized)
            yield break; // an earlier run already finished this

        auth = FirebaseInit.Auth;
        dbRoot = FirebaseInit.Database.RootReference;
        firebaseInitialized = true;

        Debug.Log("[PreGamePanel] Firebase init complete. dbRoot assigned: " + (dbRoot != null));

        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    private FirebaseUser GetCurrentUser()
    {
        return FirebaseAuth.DefaultInstance?.CurrentUser;
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

        hasInitializedMatch = false;
        currentMatch = null;

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
            //return;
        }

        /*if (args.Snapshot == null || !args.Snapshot.Exists)
        {
            Debug.LogWarning("[PregamePanel] Match snapshot missing or match deleted.");
            return;
        }*/

        //string json = args.Snapshot.GetRawJsonValue();

        //if (string.IsNullOrEmpty(json))
        //{
            Debug.LogWarning("[PregamePanel] Match snapshot JSON was empty.");
            watchedMatchId = "";
            currentMatch = null;
            hasInitializedMatch = false;
        //}
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
            watchedMatchId = "";
            currentMatch = null;
            hasInitializedMatch = false;
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[PregamePanel] Match snapshot JSON was empty.");
            watchedMatchId = "";
            currentMatch = null;
            hasInitializedMatch = false;
            return;
        }

        MatchData match = JsonUtility.FromJson<MatchData>(json);

        if (match == null)
        {
            Debug.LogError("[PregamePanel] Failed to parse MatchData from JSON.");
            return;
        }

        currentMatch = match;

        Debug.Log("[PregamePanel] Match changed. MatchId=" + match.matchId +
                  ", Status=" + match.status +
                  ", Turn=" + match.turnNumber +
                  ", CurrentTurnUid=" + match.currentTurnUid);

        Debug.Log("[PregamePanel] Scores: " +
                  match.player1DisplayName + "=" + match.player1Score + ", " +
                  match.player2DisplayName + "=" + match.player2Score);

        BoardStateData board = null;
        BagStateData bag = null;
        RackStateData sharedRack = null;

        if (!string.IsNullOrEmpty(match.boardStateJson))
            board = JsonUtility.FromJson<BoardStateData>(match.boardStateJson);

        if (!string.IsNullOrEmpty(match.bagStateJson))
            bag = JsonUtility.FromJson<BagStateData>(match.bagStateJson);

        if (!string.IsNullOrEmpty(match.sharedrackjson))
            sharedRack = JsonUtility.FromJson<RackStateData>(match.sharedrackjson);

        Debug.Log("[PregamePanel] Board cells: " + (board != null && board.cells != null ? board.cells.Count : 0));
        Debug.Log("[PregamePanel] Bag remaining: " + (bag != null && bag.tiles != null ? bag.tiles.Count : 0));
        Debug.Log("[PregamePanel] Shared rack: " + GetRackDebugString(sharedRack));

        if (match.status == "active")
        {
            Debug.Log("[PregamePanel] Match is active.");
        }
        else if (match.status == "finished")
        {
            Debug.Log("[PregamePanel] Match is finished.");
        }

        if (gameLogic == null)
        {
            Debug.LogError("[PregamePanel] gameLogic is null.");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            Debug.LogError("[PregamePanel] Auth or CurrentUser is null.");
            return;
        }

        if (!hasInitializedMatch)
        {
            hasInitializedMatch = true;
            gameLogic.StartOnlineMatch(match, auth.CurrentUser.UserId);
        }
        else
        {
            gameLogic.ApplyMatchUpdate(match);
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
        var auth = FirebaseAuth.DefaultInstance;
        var user = auth?.CurrentUser;

        if (user != null)
        {
            SetStatus("Already logged in.");
            return;
        }

        if (auth == null)
        {
            SetStatus("Firebase Auth not ready.");
            return;
        }

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
        Debug.Log("[PreGamePanel] OnCreateRoomPressed() running on GameObject: " + gameObject.name + " (EntityId: " + gameObject.GetEntityId() + ")");

        var auth = FirebaseAuth.DefaultInstance;
        var user = auth?.CurrentUser;

        if (user == null)
        {
            SetStatus("You must be logged in first.");
            return;
        }

        string roomCode = GenerateRoomCode();
        string hostName = GetBestDisplayName();

        RoomData room = new RoomData
        {
            code = roomCode,
            hostUid = user.UserId,
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
            //EnterGameplayMode();
            return;

        }
    }

    private void EnterGameplayMode()
    {
        Debug.Log("[PREGAME] EnterGameplayMode CALLED");

        if (gameLogic == null)
        {
            Debug.LogError("[PREGAME] gameLogic is NULL");
            return;
        }

        if (currentMatch == null)
        {
            Debug.LogError("[PREGAME] currentMatch is NULL");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            Debug.LogError("[PREGAME] auth/current user is NULL");
            return;
        }

        string uid = auth.CurrentUser.UserId;
        bool isPlayer1 = currentMatch.player1Uid == uid;

        List<LetterInfo> localRack = ParseRackJson(currentMatch.sharedrackjson);
        int localScore = isPlayer1 ? currentMatch.player1Score : currentMatch.player2Score;
        int opponentScore = isPlayer1 ? currentMatch.player2Score : currentMatch.player1Score;

        Debug.Log("[PREGAME] Local player is " + (isPlayer1 ? "P1" : "P2"));
        Debug.Log("[PREGAME] Local rack count = " + (localRack == null ? -1 : localRack.Count));

        gameLogic.BeginOnlineMatchFromRack(
            7,
            15,
            15,
            localRack,
            localScore,
            opponentScore,
            currentMatch.turnNumber
        );
    }

    private List<LetterInfo> CloneLetterList(List<LetterInfo> source)
    {
        List<LetterInfo> clone = new List<LetterInfo>();

        if (source == null)
            return clone;

        foreach (LetterInfo tile in source)
        {
            if (tile == null)
                continue;

            clone.Add(new LetterInfo(tile));
        }

        return clone;
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

        if (gameLogic != null)
            gameLogic.onlineSubmissionReady -= OnOnlineSubmissionReady;
    }

    public void OnStartGamePressed()
    {
        Debug.Log("[PregamePanel] OnStartGamePressed CALLED");

        if (auth == null || auth.CurrentUser == null)
        {
            SetStatus("You must be signed in.");
            return;
        }

        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(roomCode))
        {
            SetStatus("Enter a room code first.");
            return;
        }

        DatabaseReference roomRef = dbRoot.Child("rooms").Child(roomCode);

        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to load room: " + task.Exception);
                SetStatus("Failed to load room.");
                return;
            }

            if (!task.IsCompleted || task.Result == null || !task.Result.Exists)
            {
                SetStatus("Room not found.");
                return;
            }

            string roomJson = task.Result.GetRawJsonValue();
            RoomData room = JsonUtility.FromJson<RoomData>(roomJson);

            if (room == null)
            {
                SetStatus("Could not parse room data.");
                return;
            }

            if (string.IsNullOrEmpty(room.guestUid))
            {
                SetStatus("Cannot start yet. Waiting for guest.");
                return;
            }

            if (!string.IsNullOrEmpty(room.matchId) && room.status == "in_game")
            {
                Debug.Log("[PregamePanel] Match already exists for room. matchId=" + room.matchId);
                WatchMatch(room.matchId);
                EnterGameplayMode();
                return;
            }

            TryCreateInitialMatchFromRoom(roomCode, room);
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

    private BoardStateData CreateInitialBoard(int width = 9, int height = 9)
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
    
    private void EndTurnOnlineSubmit(RoundMove move)
    {
        if (currentMatch == null || auth == null || auth.CurrentUser == null)
        {
            SetStatus("Match not ready.");
            return;
        }

        string uid = auth.CurrentUser.UserId;

        if (currentMatch.status != "active")
        {
            SetStatus("Match is not active.");
            return;
        }

        if (currentMatch.currentTurnUid != uid)
        {
            SetStatus("Not your turn.");
            return;
        }

        if (move == null || !move.isValid)
        {
            SetStatus("Invalid move.");
            return;
        }

        DatabaseReference matchRef = dbRoot.Child("matches").Child(currentMatch.matchId);
        string moveJson = JsonUtility.ToJson(move);

        matchRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null)
                return TransactionResult.Abort();

            var matchDict = mutableData.Value as Dictionary<string, object>;
            if (matchDict == null)
                return TransactionResult.Abort();

            string status = matchDict.ContainsKey("status") && matchDict["status"] != null
                ? matchDict["status"].ToString()
                : "";

            string currentTurnUid = matchDict.ContainsKey("currentTurnUid") && matchDict["currentTurnUid"] != null
                ? matchDict["currentTurnUid"].ToString()
                : "";

            string pendingMoveJsonExisting = matchDict.ContainsKey("pendingMoveJson") && matchDict["pendingMoveJson"] != null
                ? matchDict["pendingMoveJson"].ToString()
                : "";

            int turnNumber = matchDict.ContainsKey("turnNumber") && matchDict["turnNumber"] != null
                ? Convert.ToInt32(matchDict["turnNumber"])
                : 0;

            if (status != "active")
                return TransactionResult.Abort();

            if (currentTurnUid != uid)
                return TransactionResult.Abort();

            if (!string.IsNullOrEmpty(pendingMoveJsonExisting))
                return TransactionResult.Abort();

            matchDict["pendingMoveJson"] = moveJson;
            matchDict["pendingMoveByUid"] = uid;
            matchDict["pendingMoveTurnNumber"] = turnNumber;
            matchDict["turnResolutionStatus"] = "idle";
            matchDict["turnResolutionByUid"] = "";
            matchDict["lastActionUnix"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            mutableData.Value = matchDict;
            return TransactionResult.Success(mutableData);
        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to submit move: " + task.Exception);
                SetStatus("Failed to submit move.");
                return;
            }

            Debug.Log("[PregamePanel] Move submitted for turn " + currentMatch.turnNumber);
            SetStatus("Move submitted.");

            TryResolvePendingMove(currentMatch.matchId);
        });
    }

    private void OnOnlineSubmissionReady(RoundMove move)
    {
        if (currentMatch == null || auth == null || auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        if (currentMatch.currentTurnUid != uid)
        {
            SetStatus("Not your turn.");
            return;
        }

        EndTurnOnlineSubmit(move);
    }

    private BoardCellData FindCell(BoardStateData board, int x, int y)
    {
        foreach (var cell in board.cells)
        {
            if (cell.x == x && cell.y == y)
                return cell;
        }

        return null;
    }

    private void TryCreateInitialMatchFromRoom(string roomCode, RoomData roomSnapshot)
    {
        if (auth == null || auth.CurrentUser == null)
            return;

        string myUid = auth.CurrentUser.UserId;
        DatabaseReference roomRef = dbRoot.Child("rooms").Child(roomCode);

        SetStatus("Attempting to start game...");

        roomRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null)
                return TransactionResult.Abort();

            var roomDict = mutableData.Value as Dictionary<string, object>;
            if (roomDict == null)
                return TransactionResult.Abort();

            string existingMatchId = roomDict.ContainsKey("matchId") && roomDict["matchId"] != null
                ? roomDict["matchId"].ToString()
                : "";

            string status = roomDict.ContainsKey("status") && roomDict["status"] != null
                ? roomDict["status"].ToString()
                : "";

            string guestUid = roomDict.ContainsKey("guestUid") && roomDict["guestUid"] != null
                ? roomDict["guestUid"].ToString()
                : "";

            if (string.IsNullOrEmpty(guestUid))
                return TransactionResult.Abort();

            if (!string.IsNullOrEmpty(existingMatchId) || status == "in_game")
                return TransactionResult.Abort();

            string newMatchId = dbRoot.Child("matches").Push().Key;

            roomDict["matchId"] = newMatchId;
            roomDict["status"] = "starting";
            roomDict["startedByUid"] = myUid;
            roomDict["startedAtUnix"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            mutableData.Value = roomDict;
            return TransactionResult.Success(mutableData);
        }).ContinueWithOnMainThread(txTask =>
        {
            if (txTask.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Room transaction failed: " + txTask.Exception);
                SetStatus("Failed to claim game start.");
                return;
            }

            roomRef.GetValueAsync().ContinueWithOnMainThread(readBackTask =>
            {
                if (readBackTask.IsFaulted || readBackTask.Result == null || !readBackTask.Result.Exists)
                {
                    SetStatus("Failed to verify room state.");
                    return;
                }

                string updatedRoomJson = readBackTask.Result.GetRawJsonValue();
                RoomData updatedRoom = JsonUtility.FromJson<RoomData>(updatedRoomJson);

                if (updatedRoom == null || string.IsNullOrEmpty(updatedRoom.matchId))
                {
                    SetStatus("Failed to verify match creation.");
                    return;
                }

                if (updatedRoom.status == "in_game")
                {
                    Debug.Log("[PregamePanel] Someone already created the match. matchId=" + updatedRoom.matchId);
                    WatchMatch(updatedRoom.matchId);
                    EnterGameplayMode();
                    return;
                }

                bool iClaimedStart = updatedRoom.status == "starting" &&
                                     updatedRoom.matchId != null &&
                                     readBackTask.Result.Child("startedByUid").Value != null &&
                                     readBackTask.Result.Child("startedByUid").Value.ToString() == myUid;

                if (!iClaimedStart)
                {
                    Debug.Log("[PregamePanel] Another client claimed start. Waiting for final match...");
                    if (!string.IsNullOrEmpty(updatedRoom.matchId))
                    {
                        WatchMatch(updatedRoom.matchId);
                        EnterGameplayMode();
                    }
                    return;
                }

                string matchId = updatedRoom.matchId;

                BagStateData bag = CreateInitialBag();
                RackStateData sharedrackjson = DrawTiles(bag, 7);
                //RackStateData player2Rack = DrawTiles(bag, 7);
                BoardStateData board = CreateInitialBoard();

                MatchData match = new MatchData
                {
                    matchId = matchId,
                    roomCode = roomCode,

                    hostUid = updatedRoom.hostUid,
                    guestUid = updatedRoom.guestUid,

                    player1Uid = updatedRoom.hostUid,
                    player2Uid = updatedRoom.guestUid,
                    player1DisplayName = updatedRoom.hostDisplayName,
                    player2DisplayName = updatedRoom.guestDisplayName,

                    player1Score = 0,
                    player2Score = 0,

                    turnNumber = 1,
                    currentTurnUid = updatedRoom.hostUid,
                    status = "active",

                    boardStateJson = JsonUtility.ToJson(board),
                    bagStateJson = JsonUtility.ToJson(bag),
                    sharedrackjson = JsonUtility.ToJson(sharedrackjson),
                    //player2RackJson = JsonUtility.ToJson(sharedrackjson),

                    createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    setupStatus = "done",
                    setupByUid = myUid,
                    setupAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    pendingMoveJson = "",
                    pendingMoveByUid = "",
                    pendingMoveTurnNumber = 0,
                    turnResolutionStatus = "idle",
                    turnResolutionByUid = "",
                    turnDeadlineUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30000,

                    stateVersion = 1
                };

                string matchJson = JsonUtility.ToJson(match);

                dbRoot.Child("matches").Child(matchId).SetRawJsonValueAsync(matchJson)
                    .ContinueWithOnMainThread(writeMatchTask =>
                    {
                        if (writeMatchTask.IsFaulted)
                        {
                            Debug.LogError("[PregamePanel] Failed writing match: " + writeMatchTask.Exception);
                            SetStatus("Failed to write match.");
                            return;
                        }

                        updatedRoom.status = "in_game";
                        string finalRoomJson = JsonUtility.ToJson(updatedRoom);

                        roomRef.SetRawJsonValueAsync(finalRoomJson).ContinueWithOnMainThread(writeRoomTask =>
                        {
                            if (writeRoomTask.IsFaulted)
                            {
                                Debug.LogError("[PregamePanel] Failed writing room final state: " + writeRoomTask.Exception);
                                SetStatus("Match created, but room update failed.");
                                return;
                            }

                            Debug.Log("[PregamePanel] Match created: " + matchId);
                            Debug.Log("[PregamePanel] Player1 rack: " + GetRackDebugString(sharedrackjson));
                            Debug.Log("[PregamePanel] Player2 rack: " + GetRackDebugString(sharedrackjson));
                            Debug.Log("[PregamePanel] Bag tiles remaining: " + bag.tiles.Count);

                            SetStatus("Game started.");
                            WatchMatch(matchId);
                            EnterGameplayMode();
                        });
                    });
            });
        });
    }
    private void TryResolvePendingMove(string matchId)
    {
        if (string.IsNullOrEmpty(matchId) || auth == null || auth.CurrentUser == null)
            return;

        string myUid = auth.CurrentUser.UserId;
        DatabaseReference matchRef = dbRoot.Child("matches").Child(matchId);

        matchRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null)
                return TransactionResult.Abort();

            var matchDict = mutableData.Value as Dictionary<string, object>;
            if (matchDict == null)
                return TransactionResult.Abort();

            string status = matchDict.ContainsKey("status") && matchDict["status"] != null
                ? matchDict["status"].ToString()
                : "";

            string pendingMoveJson = matchDict.ContainsKey("pendingMoveJson") && matchDict["pendingMoveJson"] != null
                ? matchDict["pendingMoveJson"].ToString()
                : "";

            string turnResolutionStatus = matchDict.ContainsKey("turnResolutionStatus") && matchDict["turnResolutionStatus"] != null
                ? matchDict["turnResolutionStatus"].ToString()
                : "idle";

            if (status != "active")
                return TransactionResult.Abort();

            if (string.IsNullOrEmpty(pendingMoveJson))
                return TransactionResult.Abort();

            if (turnResolutionStatus == "resolving")
                return TransactionResult.Abort();

            string boardStateJson = matchDict.ContainsKey("boardStateJson") && matchDict["boardStateJson"] != null
                ? matchDict["boardStateJson"].ToString()
                : "";

            string bagStateJson = matchDict.ContainsKey("bagStateJson") && matchDict["bagStateJson"] != null
                ? matchDict["bagStateJson"].ToString()
                : "";

            string player1RackJson = matchDict.ContainsKey("player1RackJson") && matchDict["player1RackJson"] != null
                ? matchDict["player1RackJson"].ToString()
                : "";

            string player2RackJson = matchDict.ContainsKey("player2RackJson") && matchDict["player2RackJson"] != null
                ? matchDict["player2RackJson"].ToString()
                : "";

            string player1Uid = matchDict.ContainsKey("player1Uid") && matchDict["player1Uid"] != null
                ? matchDict["player1Uid"].ToString()
                : "";

            string player2Uid = matchDict.ContainsKey("player2Uid") && matchDict["player2Uid"] != null
                ? matchDict["player2Uid"].ToString()
                : "";

            string pendingMoveByUid = matchDict.ContainsKey("pendingMoveByUid") && matchDict["pendingMoveByUid"] != null
                ? matchDict["pendingMoveByUid"].ToString()
                : "";

            int turnNumber = matchDict.ContainsKey("turnNumber") && matchDict["turnNumber"] != null
                ? Convert.ToInt32(matchDict["turnNumber"])
                : 0;

            int pendingMoveTurnNumber = matchDict.ContainsKey("pendingMoveTurnNumber") && matchDict["pendingMoveTurnNumber"] != null
                ? Convert.ToInt32(matchDict["pendingMoveTurnNumber"])
                : 0;

            if (pendingMoveTurnNumber != turnNumber)
                return TransactionResult.Abort();

            BoardStateData board = JsonUtility.FromJson<BoardStateData>(boardStateJson);
            BagStateData bag = JsonUtility.FromJson<BagStateData>(bagStateJson);
            RackStateData player1Rack = JsonUtility.FromJson<RackStateData>(player1RackJson);
            RackStateData player2Rack = JsonUtility.FromJson<RackStateData>(player2RackJson);
            RoundMove move = JsonUtility.FromJson<RoundMove>(pendingMoveJson);

            if (board == null || bag == null || player1Rack == null || player2Rack == null || move == null || !move.isValid)
                return TransactionResult.Abort();

            bool isPlayer1Move = pendingMoveByUid == player1Uid;
            RackStateData actingRack = isPlayer1Move ? player1Rack : player2Rack;

            foreach (var simTile in move.simulatedTiles)
            {
                int x = simTile.letterPosition.ColY - 1;
                int y = simTile.letterPosition.RowX - 1;

                BoardCellData cell = FindCell(board, x, y);
                if (cell == null || cell.occupied)
                    return TransactionResult.Abort();

                int rackIndex = actingRack.tiles.FindIndex(t => t.letter == simTile.letterInfo.letter);
                if (rackIndex < 0)
                    return TransactionResult.Abort();

                TileData rackTile = actingRack.tiles[rackIndex];
                actingRack.tiles.RemoveAt(rackIndex);

                cell.occupied = true;
                cell.tile = rackTile;
            }

            while (actingRack.tiles.Count < 7 && bag.tiles.Count > 0)
            {
                actingRack.tiles.Add(bag.tiles[0]);
                bag.tiles.RemoveAt(0);
            }

            int player1Score = matchDict.ContainsKey("player1Score") && matchDict["player1Score"] != null
                ? Convert.ToInt32(matchDict["player1Score"])
                : 0;

            int player2Score = matchDict.ContainsKey("player2Score") && matchDict["player2Score"] != null
                ? Convert.ToInt32(matchDict["player2Score"])
                : 0;

            if (isPlayer1Move)
                player1Score += move.score;
            else
                player2Score += move.score;

            string nextTurnUid = isPlayer1Move ? player2Uid : player1Uid;

            int stateVersion = matchDict.ContainsKey("stateVersion") && matchDict["stateVersion"] != null
                ? Convert.ToInt32(matchDict["stateVersion"])
                : 0;

            matchDict["boardStateJson"] = JsonUtility.ToJson(board);
            matchDict["bagStateJson"] = JsonUtility.ToJson(bag);
            matchDict["player1RackJson"] = JsonUtility.ToJson(player1Rack);
            matchDict["player2RackJson"] = JsonUtility.ToJson(player2Rack);

            matchDict["player1Score"] = player1Score;
            matchDict["player2Score"] = player2Score;

            matchDict["currentTurnUid"] = nextTurnUid;
            matchDict["turnNumber"] = turnNumber + 1;

            matchDict["pendingMoveJson"] = "";
            matchDict["pendingMoveByUid"] = "";
            matchDict["pendingMoveTurnNumber"] = 0;

            matchDict["turnResolutionStatus"] = "done";
            matchDict["turnResolutionByUid"] = myUid;

            matchDict["turnDeadlineUnix"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30000;
            matchDict["lastActionUnix"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            matchDict["stateVersion"] = stateVersion + 1;

            mutableData.Value = matchDict;
            return TransactionResult.Success(mutableData);
        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[PregamePanel] Failed to resolve pending move: " + task.Exception);
                return;
            }

            Debug.Log("[PregamePanel] Resolve attempt finished.");
        });
    }


    private List<LetterInfo> ParseRackJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<LetterInfo>();

        try
        {
            string wrapped = "{\"items\":" + json + "}";
            LetterInfoListWrapper wrapper = JsonUtility.FromJson<LetterInfoListWrapper>(wrapped);
            return wrapper != null && wrapper.items != null ? wrapper.items : new List<LetterInfo>();
        }
        catch (Exception ex)
        {
            Debug.LogError("[PREGAME] Failed to parse rack json: " + ex.Message);
            return new List<LetterInfo>();
        }
    }
    /*private List<LetterInfo> ParseRackJson(string rackJson)
    {
        if (string.IsNullOrWhiteSpace(rackJson))
            return new List<LetterInfo>();

        try
        {
            string wrappedJson = "{\"items\":" + rackJson + "}";
            LetterInfoListWrapper wrapper = JsonUtility.FromJson<LetterInfoListWrapper>(wrappedJson);

            if (wrapper != null && wrapper.items != null)
                return wrapper.items;

            return new List<LetterInfo>();
        }
        catch (Exception ex)
        {
            Debug.LogError("[PREGAME] Failed to parse rack json: " + ex.Message);
            return new List<LetterInfo>();
        }
    }*/
}
