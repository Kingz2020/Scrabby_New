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


    [SerializeField] private GameObject pregamePanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject optionPanel;

    [SerializeField] private MatchStatusPanel matchStatusPanel;

    private DatabaseReference dbRoot;

    private FirebaseAuth auth;
    private FirebaseUser user;
    public Button startGameButton;

    private DatabaseReference currentMatchRef;
    private DatabaseReference currentRoomRef;
    //private string watchedRoomCode = "";
    private string watchedMatchId = "";
    private bool firebaseInitialized = false;

    [SerializeField] private GameLogic gameLogic;
    private bool hasInitializedMatch = false;
    private MatchData currentMatch;

    private int matchTraceSeq = 0;

    private DatabaseReference currentSubmissionsRef;
    private int watchedRoundNumber = -1;
    private int lastProcessedRound = 0;

    private const string TestUserA_Email = "sexy@bikini.com";
    private const string TestUserB_Email = "zia@far.com";
    private const string TestUserPassword = "Scrabby1234";
    private DatabaseReference watchedRoomRef;

    private EventHandler<ValueChangedEventArgs> roomWatcher;

    private bool pendingEnterGameplay = false;

    private string pendingResolutionMatchId = null;

    [SerializeField] private OnlineMatchController onlineMatchController;

    [Serializable]
    public class RoomPlayerData
    {
        public string uid;
        public string displayName;
    }

    [Serializable]
    public class StringListWrapper
    {
        public List<string> items = new List<string>();
    }

    [System.Serializable]
    public class UserData
    {
        public string email;
        public string displayName;

        public string avatarId;

        public long createdAt;
        public long lastSeenAt;

        public string presenceState;

        public List<string> activeRoomIds = new List<string>();
        public List<string> activeMatchIds = new List<string>();
    }

    [Serializable]
    public class RoomInviteData
    {
        public string roomCode;
        public string fromUid;
        public string fromDisplayName;
        public long createdAtUnix;
    }

    private void Awake()
    {
        if (onlineMatchController == null)
            onlineMatchController = OnlineMatchController.Instance;

        if (pregamePanel != null) pregamePanel.SetActive(true);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        //if (gameLogic != null)
        //    gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;
    }


    private void Start()
    {
        Debug.Log("[PreGamePanel] Start() running on GameObject: " + gameObject.name + " (EntityId: " + gameObject.GetEntityId() + ")");
        Debug.Log("[PreGamePanel] Start running");
        //Debug.Log("[PreGamePanel] START instance = " + GetInstanceID());

        if (startGameButton != null)
        {
            startGameButton.interactable = false;
            startGameButton.image.color = Color.white;
        }

        //if (gameLogic != null)
        //    gameLogic.onlineSubmissionReady += OnOnlineSubmissionReady;

        StartCoroutine(WaitForFirebaseThenInit());
    }

    
    private void OnEnable()
    {
        if (!firebaseInitialized)
        {
            Debug.Log("[PreGamePanel] ENABLED");
            StartCoroutine(WaitForFirebaseThenInit());
        }
    }

    private IEnumerator WaitForFirebaseThenInit()
    {
        Debug.Log("[PreGamePanel] WaitForFirebaseThenInit started");

        yield return new WaitUntil(() => FirebaseInit.IsReady);

        Debug.Log("[PreGamePanel] Firebase became ready");

        if (firebaseInitialized)
            yield break; // an earlier run already finished this

        auth = FirebaseInit.Auth;

        Debug.Log("FirebaseInit.Database = " + FirebaseInit.Database);
        Debug.Log("FirebaseInit.Auth = " + FirebaseInit.Auth);

        dbRoot = FirebaseInit.Database.RootReference;

        Debug.Log("[PreGamePanel] dbRoot assigned = " + dbRoot);

        firebaseInitialized = true;

        

        Debug.Log("[PreGamePanel] Firebase init complete. dbRoot assigned: " + (dbRoot != null));

        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    public void OnRefreshPressed()
    {
        Debug.Log("[MATCH STATUS] Refresh requested");
        SetStatus("Refresh not implemented yet");
    }
    private void TraceMatch(string label)
    {
        matchTraceSeq++;

        string currentMatchId = currentMatch == null ? "NULL" : currentMatch.matchId;
        string currentStatus = currentMatch == null ? "NULL" : currentMatch.status;
        string currentTurn = currentMatch == null ? "NULL" : currentMatch.currentRoundNumber.ToString();

        Debug.Log(
            $"[MATCHTRACE #{matchTraceSeq}] {label} | " +
            $"watchedMatchId={watchedMatchId} | " +
            $"currentMatchId={currentMatchId} | " +
            $"currentStatus={currentStatus} | " +
            $"currentTurn={currentTurn} | " +
            $"hasInitializedMatch={hasInitializedMatch} | " +
            $"frame={Time.frameCount}"
        );
    }
    private FirebaseUser GetCurrentUser()
    {
        return FirebaseAuth.DefaultInstance?.CurrentUser;
    }

    private void OnDisable()
    {
        Debug.Log("[PreGamePanel] DISABLED");
        StopWatchingRoom();
    }

    // on PreGamePanel
    public void EnterMultiplayerFlow()
    {
        if (optionPanel != null) optionPanel.SetActive(false);

        if (IsSignedIn())
        {
            // Already authenticated — skip the login screen entirely.
            gameObject.SetActive(false);
            if (matchStatusPanel != null) matchStatusPanel.gameObject.SetActive(true);
        }
        else
        {
            // Show the login/register screen; OnLoginPressed's success path
            // already hands off to MatchStatusPanel once auth completes.
            if (pregamePanel != null) pregamePanel.SetActive(true);
        }
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
                RepairCurrentUserProfileIfMissing();

                string shownName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
                SetStatus("Signed in.");
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                // Auto-login (persisted session) reached this point too — hand off
                // to MatchStatusPanel the same way OnLoginPressed's success path does,
                // but only if PreGamePanel is the one currently visible (avoid stealing
                // focus if the user is mid-gameplay or elsewhere when a session refreshes).
                if (matchStatusPanel != null && pregamePanel != null && pregamePanel.activeInHierarchy)
                {
                    matchStatusPanel.gameObject.SetActive(true);
                    gameObject.SetActive(false);
                }
            }
            else
            {
                if (signedInAsText != null)
                    signedInAsText.text = "Not signed in";
            }

            RefreshUI();
        }
    }

    private void ShowOnlineRoundResult(RoundResultData result)
    {
        if (Singleton.Instance == null ||
            Singleton.Instance.UIManager == null)
            return;

        string uid = auth.CurrentUser.UserId;
        bool isPlayer1 = currentMatch.player1Uid == uid;

        int localScore =
            isPlayer1
            ? currentMatch.player1Score
            : currentMatch.player2Score;

        int opponentScore =
            isPlayer1
            ? currentMatch.player2Score
            : currentMatch.player1Score;

        Singleton.Instance.UIManager.UpdateTotalScores(
            localScore,
            opponentScore);

        if (result.anyValidMove)
        {
            Singleton.Instance.UIManager.ShowRoundMessage(
                result.winnerDisplayName +
                " wins with " +
                result.winnerWord +
                " (" +
                result.winnerScore +
                " pts)");
        }
        else
        {
            Singleton.Instance.UIManager.ShowRoundMessage(
                "No valid move this round.");
        }
    }

    /*private void WatchSubmissionsForRound(int roundNumber)
    {
        if (currentSubmissionsRef != null)
        {
            currentSubmissionsRef.ValueChanged -= OnSubmissionsValueChanged;
            currentSubmissionsRef = null;
        }

        watchedRoundNumber = roundNumber;

        currentSubmissionsRef = dbRoot.Child("matches").Child(currentMatch.matchId)
            .Child("rounds").Child(roundNumber.ToString()).Child("submissions");

        currentSubmissionsRef.ValueChanged += OnSubmissionsValueChanged;
    }
    */
    /*private void OnSubmissionsValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || currentMatch == null)
            return;

        int submittedCount = (int)args.Snapshot.ChildrenCount;
        int expectedCount = 2; // host + guest — extend when room supports more players

        Debug.Log("[PregamePanel] Round " + watchedRoundNumber + " submissions: " + submittedCount + "/" + expectedCount);

        if (submittedCount >= expectedCount)
        {
            OnlineMatchController.Instance.TryResolveRound(currentMatch.matchId, watchedRoundNumber);
        }
    }*/

    /*private bool IsBetterSubmission(RoundSubmissionData candidate, RoundSubmissionData currentBest)
    {
        if (candidate.score != currentBest.score)
            return candidate.score > currentBest.score;

        int candLen = string.IsNullOrEmpty(candidate.word) ? 0 : candidate.word.Length;
        int bestLen = string.IsNullOrEmpty(currentBest.word) ? 0 : currentBest.word.Length;

        if (candLen != bestLen)
            return candLen > bestLen;

        return candidate.submittedAtUnix < currentBest.submittedAtUnix; // earlier submission wins ties
    }*/

    public void OnRegisterPressed()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        string displayName = displayNameInput.text.Trim();

        Debug.Log("[PregamePanel] Register button pressed.");

        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
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
                    //currentRoomId = "",
                    activeRoomIds = new List<string>(),
                    activeMatchIds = new List<string>(),
                    //currentMatchId = "",
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

                //RepairCurrentUserProfileIfMissing();

                RefreshUI();

                // Return to MatchStatusPanel now that login succeeded.
                if (matchStatusPanel != null)
                {
                    matchStatusPanel.gameObject.SetActive(true);
                    gameObject.SetActive(false);
                }
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

        // Self-heal dbRoot if this panel's Firebase-init coroutine never completed
        // (e.g. it was interrupted by the panel being disabled before FirebaseInit.IsReady).
        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
                this.auth = FirebaseInit.Auth;
                firebaseInitialized = true;
                Debug.Log("[PreGamePanel] dbRoot was null — recovered from FirebaseInit.");
            }
            else
            {
                SetStatus("Firebase not ready yet. Try again in a moment.");
                Debug.LogWarning("[PreGamePanel] OnCreateRoomPressed aborted: dbRoot null and FirebaseInit not ready.");
                return;
            }
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

        Debug.Log("user = " + user);
        Debug.Log("dbRoot = " + dbRoot);
        Debug.Log("roomCodeInput = " + roomCodeInput);

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

            //SetStatus("Room created: " + roomCode);
            //WatchRoom(roomCode);

            AddRoomToUser(roomCode);

            roomCodeInput.text = roomCode;
            roomCodeInput.SetTextWithoutNotify(roomCode);
            roomCodeInput.ForceLabelUpdate();

            SetStatus("Room created: " + roomCode);
            WatchRoom(roomCode);

            //user.activeRoomIds.Add(roomCode);
            //presenceState = "waiting";

        });
    }

    private void AddMatchToUser(string uid, string matchId, Action onComplete = null)
    {
        Debug.Log("[AddMatchToUser] ENTER uid=" + uid + " matchId=" + matchId);

        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(matchId))
        {
            Debug.LogWarning("[AddMatchToUser] ABORT early: uid or matchId empty.");
            onComplete?.Invoke();
            return;
        }

        Debug.Log("[PreGamePanel] AddMatchToUser uid=" + uid + " matchId=" + matchId);

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
            {
                Debug.LogWarning("[PreGamePanel] AddMatchToUser read failed or user missing for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            string json = task.Result.GetRawJsonValue();
            UserData userData = JsonUtility.FromJson<UserData>(json);
            if (userData == null)
            {
                Debug.LogWarning("[PreGamePanel] AddMatchToUser could not parse UserData for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            if (userData.activeMatchIds == null)
                userData.activeMatchIds = new List<string>();

            if (!userData.activeMatchIds.Contains(matchId))
                userData.activeMatchIds.Add(matchId);

            var updates = new Dictionary<string, object>
        {
            { "activeMatchIds", userData.activeMatchIds }
        };

            dbRoot.Child("users").Child(uid)
                  .UpdateChildrenAsync(updates)
                  .ContinueWithOnMainThread(writeTask =>
                  {
                      if (writeTask.IsFaulted)
                      {
                          Debug.LogError("[PreGamePanel] AddMatchToUser write failed for uid=" + uid + ": " + writeTask.Exception);
                      }
                      else
                      {
                          Debug.Log("[PreGamePanel] AddMatchToUser updated activeMatchIds for uid=" + uid +
                                    " -> [" + string.Join(",", userData.activeMatchIds) + "]");
                      }

                      onComplete?.Invoke();
                  });
        });
    }
    public void RepairCurrentUserProfileIfMissing()
    {
        if (!EnsureFirebaseReady() || auth == null || auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (!task.IsFaulted && task.Result != null && task.Result.Exists)
            {
                // Profile already exists — don't overwrite a good one.
                return;
            }

            FirebaseUser u = auth.CurrentUser;
            string displayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName;

            UserData userData = new UserData
            {
                email = u.Email,
                displayName = displayName,
                avatarId = "",
                createdAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                lastSeenAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                presenceState = "online",
                activeRoomIds = new List<string>(),
                activeMatchIds = new List<string>()
            };

            string json = JsonUtility.ToJson(userData);

            dbRoot.Child("users").Child(uid).SetRawJsonValueAsync(json).ContinueWithOnMainThread(dbTask =>
            {
                if (dbTask.IsFaulted)
                {
                    Debug.LogError("[PreGamePanel] Repair profile write failed: " + dbTask.Exception);
                    return;
                }

                Debug.Log("[PreGamePanel] Repaired user profile for uid=" + uid);
            });
        });
    }

    private void AddRoomToUser(string roomCode, Action onComplete = null)
    {
        if (auth == null || auth.CurrentUser == null || string.IsNullOrEmpty(roomCode))
        {
            onComplete?.Invoke();
            return;
        }

        string uid = auth.CurrentUser.UserId;
        Debug.Log("[PreGamePanel] AddRoomToUser uid=" + uid + " roomCode=" + roomCode);

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
            {
                Debug.LogWarning("[PreGamePanel] AddRoomToUser read failed or user missing for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            string json = task.Result.GetRawJsonValue();
            UserData userData = JsonUtility.FromJson<UserData>(json);
            if (userData == null)
            {
                Debug.LogWarning("[PreGamePanel] AddRoomToUser could not parse UserData for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            if (userData.activeRoomIds == null)
                userData.activeRoomIds = new List<string>();

            if (!userData.activeRoomIds.Contains(roomCode))
                userData.activeRoomIds.Add(roomCode);

            var updates = new Dictionary<string, object>
        {
            { "activeRoomIds", userData.activeRoomIds }
        };

            dbRoot.Child("users").Child(uid)
                  .UpdateChildrenAsync(updates)
                  .ContinueWithOnMainThread(writeTask =>
                  {
                      if (writeTask.IsFaulted)
                      {
                          Debug.LogError("[PreGamePanel] AddRoomToUser write failed for uid=" + uid + ": " + writeTask.Exception);
                      }
                      else
                      {
                          Debug.Log("[PreGamePanel] AddRoomToUser updated activeRoomIds for uid=" + uid +
                                    " -> [" + string.Join(",", userData.activeRoomIds) + "]");
                      }

                      onComplete?.Invoke();
                  });
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

        JoinRoomByCode(roomCode);
    }


    public void JoinRoomByCode(string roomCode)
    {
        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
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

    public void SendRoomInvite(string toUid, string roomCode)
    {
        if (string.IsNullOrEmpty(toUid) || auth == null || auth.CurrentUser == null)
            return;

        RoomInviteData invite = new RoomInviteData
        {
            roomCode = roomCode,
            fromUid = auth.CurrentUser.UserId,
            fromDisplayName = GetBestDisplayName(),
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        dbRoot.Child("users").Child(toUid).Child("invites").Child(roomCode)
            .SetRawJsonValueAsync(JsonUtility.ToJson(invite));
    }
    public void AcceptRoomInvite(string roomCode)
    {
        // remove the invite record, then join normally
        if (auth != null && auth.CurrentUser != null)
        {
            dbRoot.Child("users").Child(auth.CurrentUser.UserId).Child("invites").Child(roomCode).RemoveValueAsync();
        }

        JoinRoomByCode(roomCode);
    }

    public void DeclineRoomInvite(string roomCode)
    {
        if (auth != null && auth.CurrentUser != null)
        {
            dbRoot.Child("users").Child(auth.CurrentUser.UserId).Child("invites").Child(roomCode).RemoveValueAsync();
        }

        if (matchStatusPanel != null)
            matchStatusPanel.OnRefreshPressed();
    }

    public void OnRematchPressed()
    {
        if (currentMatch == null || auth == null || auth.CurrentUser == null)
            return;

        string myUid = auth.CurrentUser.UserId;
        string opponentUid = currentMatch.player1Uid == myUid ? currentMatch.player2Uid : currentMatch.player1Uid;

        // reuse your existing room-creation logic, then invite the opponent
        string roomCode = GenerateRoomCode();
        RoomData room = new RoomData
        {
            code = roomCode,
            hostUid = myUid,
            hostDisplayName = GetBestDisplayName(),
            guestUid = "",
            guestDisplayName = "",
            status = "waiting",
            createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        dbRoot.Child("rooms").Child(roomCode).SetRawJsonValueAsync(JsonUtility.ToJson(room))
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted) { SetStatus("Rematch failed."); return; }

                AddRoomToUser(roomCode);
                SendRoomInvite(opponentUid, roomCode);
                SetStatus("Rematch invite sent!");
                ShowPregamePanel(); // or wherever makes sense post-gameover
            });
    }

    public void OnLogoutPressed()
    {
        if (auth == null)
            return;

        StopWatchingRoom();
        OnlineMatchController.Instance?.StopWatchingMatch();

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

    private void HandleRoomState(string roomCode, RoomData room)
    {
        bool roomIsFull =
            !string.IsNullOrEmpty(room.hostUid) &&
            !string.IsNullOrEmpty(room.guestUid);

        Debug.Log("[HandleRoomState] roomCode=" + roomCode +
              " hostUid=" + room.hostUid +
              " guestUid=" + room.guestUid +
              " matchId=" + room.matchId +
              " status=" + room.status);

        if (!roomIsFull)
            return;

        // Room is full but no match yet.
        if (string.IsNullOrEmpty(room.matchId))
        {
            TryCreateInitialMatchFromRoom(roomCode, room);
            return;
        }
        Debug.Log("[HandleRoomState] Match exists for current user. Adding to activeMatchIds...");
        // Match already exists.
        AddMatchToUser(auth.CurrentUser.UserId, room.matchId, () =>
        {
            Debug.Log("[HandleRoomState] AddMatchToUser completed for uid=" + auth.CurrentUser.UserId);
            if (matchStatusPanel != null)
                matchStatusPanel.ForceRefresh();
        });

        OnlineMatchController.Instance.WatchMatch(room.matchId, false);
    }

    public void WatchRoom(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode))
            return;

        // Self-heal dbRoot if this panel's Firebase-init coroutine never completed.
        if (dbRoot == null)
        {
            if (FirebaseInit.IsReady && FirebaseInit.Database != null)
            {
                dbRoot = FirebaseInit.Database.RootReference;
                auth = FirebaseInit.Auth;
                firebaseInitialized = true;
                Debug.Log("[PreGamePanel] dbRoot was null in WatchRoom — recovered from FirebaseInit.");
            }
            else
            {
                Debug.LogWarning("[PreGamePanel] WatchRoom aborted: dbRoot null and FirebaseInit not ready.");
                return;
            }
        }

        StopWatchingRoom();

        watchedRoomRef =
            dbRoot.Child("rooms").Child(roomCode);

        roomWatcher = (sender, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError(
                    "[ROOM WATCH] " +
                    args.DatabaseError.Message);

                return;
            }

            if (args.Snapshot == null ||
                !args.Snapshot.Exists)
            {
                return;
            }

            string json =
                args.Snapshot.GetRawJsonValue();

            RoomData room =
                JsonUtility.FromJson<RoomData>(json);

            if (room == null)
                return;

            HandleRoomState(roomCode, room);
        };

        watchedRoomRef.ValueChanged += roomWatcher;

        Debug.Log("[ROOM WATCH] Watching room " + roomCode);
    }

    private void StopWatchingRoom()
    {
        if (watchedRoomRef != null &&
            roomWatcher != null)
        {
            watchedRoomRef.ValueChanged -= roomWatcher;
        }

        watchedRoomRef = null;
        roomWatcher = null;
    }

    /*private void OnRoomValueChanged(object sender, ValueChangedEventArgs args)
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
        bool matchExists = !string.IsNullOrEmpty(room.matchId);
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
            bool canStart = roomFull || matchExists;
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
            Debug.Log("[PregamePanel] Match exists. Waiting for player to press Start.");

            if (watchedMatchId != room.matchId || currentMatch == null)
            {
                OnlineMatchController.Instance.WatchMatch(room.matchId, false);
            }

            return;
        }
    }
    */
    public void EnterGameplayMode()
    {
        Debug.Log("[ENTER GAMEPLAY] PANEL SWITCH ONLY");

        // Switch panels FIRST
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (matchStatusPanel != null) matchStatusPanel.gameObject.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        // Do NOT touch currentMatch or call BeginOnlineMatchFromRack here anymore.
        // That is now handled by OnlineMatchController.StartGameplayForCurrentMatch().
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
        OnlineMatchController.Instance?.StopWatchingMatch();

        if (auth != null)
        {
            auth.StateChanged -= AuthStateChanged;
            auth = null;
        }

        //if (gameLogic != null)
         //   gameLogic.onlineSubmissionReady -= OnOnlineSubmissionReady;
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

        if (!EnsureFirebaseReady())
        {
            SetStatus("Firebase not ready yet. Try again in a moment.");
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
                Debug.Log("[PregamePanel] Loading existing match: " + room.matchId);

                Debug.Log($"[STARTFLOW] Existing match detected matchId={room.matchId}");
                AddMatchToUser(auth.CurrentUser.UserId, room.matchId);
                OnlineMatchController.Instance.ResumeMatch(room.matchId);
                Debug.Log("[STARTFLOW] WatchMatch called for existing match");

                return;
            }

            Debug.Log($"[STARTFLOW] About to call TryCreateInitialMatchFromRoom roomCode={roomCode} matchId={(room.matchId ?? "null")}");
            TryCreateInitialMatchFromRoom(roomCode, room);
            Debug.Log("[STARTFLOW] Returned from TryCreateInitialMatchFromRoom call");
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

    /*private void OnOnlineSubmissionReady(RoundMove move)
    {
        if (currentMatch == null || auth == null || auth.CurrentUser == null)
            return;

        OnlineMatchController.Instance.SubmitRoundMove(move);
    }*/

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
        Debug.Log($"[STARTFLOW] TryCreateInitialMatchFromRoom ENTER roomCode={roomCode} matchId={(roomSnapshot != null ? roomSnapshot.matchId : "null")}");

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
                    OnlineMatchController.Instance.WatchMatch(updatedRoom.matchId,false);
                    //EnterGameplayMode();
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
                        OnlineMatchController.Instance.WatchMatch(updatedRoom.matchId,false);
                        //EnterGameplayMode();
                    }
                    return;
                }

                string matchId = updatedRoom.matchId;

                BagStateData bag = CreateInitialBag();
                RackStateData sharedrackjson = DrawTiles(bag, 7);
                BoardStateData board = CreateInitialBoard();

                Debug.Log("[BONUS] gameLogic reference null? " + (gameLogic == null));
                string bonusBoardJson = "";

                if (gameLogic != null)
                {
                    // This snapshot uses a 9x9 board via CreateInitialBoard(9, 9) [13].
                    gameLogic.SetBoardSize(9, 9);

                    bonusBoardJson = gameLogic.GenerateBonusBoardJsonForOnlineMatch();
                    Debug.Log("[BONUS] bonusBoardJson length=" + (bonusBoardJson == null ? -1 : bonusBoardJson.Length));
                }
                else
                {
                    Debug.LogWarning("[BONUS] gameLogic was NULL in TryCreateInitialMatchFromRoom. Skipping bonus board JSON.");
                }

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

                    status = "active",
                    currentRoundNumber = 1,

                    boardStateJson = JsonUtility.ToJson(board),
                    bagStateJson = JsonUtility.ToJson(bag),
                    sharedrackjson = JsonUtility.ToJson(sharedrackjson),
                    
                    bonusBoardJson = bonusBoardJson,

                    lastRoundResultJson = "",
                    roundResolutionStatus = "idle",
                    roundResolutionByUid = "",

                    totalRounds = matchStatusPanel != null ? matchStatusPanel.GetRoundCount() : 5,

                    createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                    setupStatus = "done",
                    setupByUid = myUid,
                    setupAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

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

                            //AddMatchToUser(matchId); // for the local (host) player
                            //AddMatchToUser(updatedRoom.hostUid, matchId);
                            //AddMatchToUser(updatedRoom.guestUid, matchId);
                            //RemoveRoomFromUser(updatedRoom.hostUid, roomCode);
                            //RemoveRoomFromUser(updatedRoom.guestUid, roomCode);

                            AddMatchToUser(updatedRoom.hostUid, matchId, () =>
                            {
                                RemoveRoomFromUser(updatedRoom.hostUid, roomCode);
                            });

                            AddMatchToUser(updatedRoom.guestUid, matchId, () =>
                            {
                                RemoveRoomFromUser(updatedRoom.guestUid, roomCode);
                            });

                            

                            SetStatus("Game ready. Tap Resume to play.");
                            OnlineMatchController.Instance.WatchMatch(matchId,false);
                        });
                    });
            });
        });
    }
    public void SetRoomCodeInput(string code)
    {
        if (roomCodeInput == null)
            return;

        roomCodeInput.text = code;
        roomCodeInput.SetTextWithoutNotify(code);
        roomCodeInput.ForceLabelUpdate();
    }
    private List<LetterInfo> ParseRackJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<LetterInfo>();

        RackStateData rack = JsonUtility.FromJson<RackStateData>(json);
        List<LetterInfo> result = new List<LetterInfo>();

        if (rack != null && rack.tiles != null)
        {
            foreach (var tile in rack.tiles)
            {
                if (tile == null) continue;
                result.Add(new LetterInfo(tile.letter, tile.value));
            }
        }

        return result;
    }
    /*private void SubmitRoundMove(RoundMove move)
    {
        if (!EnsureFirebaseReady() || currentMatch == null || auth.CurrentUser == null)
        {
            SetStatus("Match not ready.");
            return;
        }

        if (currentMatch.status != "active")
        {
            SetStatus("Match is not active.");
            return;
        }

        string uid = auth.CurrentUser.UserId;
        int roundNumber = currentMatch.currentRoundNumber;

        RoundSubmissionData submission = new RoundSubmissionData
        {
            uid = uid,
            word = move != null ? move.word : "",
            score = move != null ? move.score : 0,
            isValid = move != null && move.isValid,
            simulatedTilesJson = SerializeSimulatedTiles(move),
            submittedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        string json = JsonUtility.ToJson(submission);

        dbRoot.Child("matches").Child(currentMatch.matchId)
            .Child("rounds").Child(roundNumber.ToString())
            .Child("submissions").Child(uid)
            .SetRawJsonValueAsync(json)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("[PregamePanel] Failed to submit round move: " + task.Exception);
                    SetStatus("Failed to submit move.");
                    return;
                }

                Debug.Log("[PregamePanel] Round " + roundNumber + " submission written.");
                SetStatus("Move submitted. Waiting for other players...");

                pendingResolutionMatchId = currentMatch.matchId; // remember: I'm actively waiting on this match

                // Start the waiting sequence on THIS PreGamePanel, not on GameLogic
                StartCoroutine(ShowSubmittedWaitingSequence());
            });
    }
    */
    private IEnumerator ShowSubmittedWaitingSequence()
    {
        if (gameLogic != null)
            gameLogic.SetInputLocked(true);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Move submitted!");

        yield return new WaitForSeconds(1.5f);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Waiting for other players...");

        yield return new WaitForSeconds(1.5f);

        if (pendingResolutionMatchId == null)
            yield break; // already redirected to game-over panel; don't switch to MatchStatusPanel

        if (gameplayPanel != null)
            gameplayPanel.SetActive(false);
        if (pregamePanel != null)
            pregamePanel.SetActive(false);
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
        if (matchStatusPanel != null)
        {
            matchStatusPanel.gameObject.SetActive(true);
            matchStatusPanel.OnRefreshPressed();
        }
    }

    private string SerializeSimulatedTiles(RoundMove move)
    {
        List<SimPlacedTileData> list = new List<SimPlacedTileData>();

        if (move != null && move.isValid && move.simulatedTiles != null)
        {
            foreach (var sim in move.simulatedTiles)
            {
                if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                    continue;

                list.Add(new SimPlacedTileData
                {
                    letter = sim.letterInfo.letter,
                    points = sim.letterInfo.points,
                    row = sim.letterPosition.RowX,
                    col = sim.letterPosition.ColY
                });
            }
        }

        return JsonUtility.ToJson(new SimTileListWrapper { tiles = list });
    }

    public void TryResumeActiveMatch()
    {
        if (!EnsureFirebaseReady())
            return;

        if (auth.CurrentUser == null)
            return;

        string uid = auth.CurrentUser.UserId;

        dbRoot.Child("matches")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.Result == null)
                {
                    Debug.LogError("[RESUME] Failed to load matches.");
                    ShowPregamePanel();
                    return;
                }

                foreach (var child in task.Result.Children)
                {
                    string raw = child.GetRawJsonValue();

                    if (string.IsNullOrEmpty(raw))
                        continue;

                    MatchData match =
                        JsonUtility.FromJson<MatchData>(raw);

                    if (match == null)
                        continue;

                    if (match.status != "active")
                        continue;

                    bool belongsToUser =
                        match.player1Uid == uid ||
                        match.player2Uid == uid;

                    if (!belongsToUser)
                        continue;

                    Debug.Log(
                        "[RESUME] Found active match: " +
                        match.matchId);

                    currentMatch = match;

                    OnlineMatchController.Instance.ResumeMatch(match.matchId);

                    return;
                }

                Debug.Log("[RESUME] No active match found.");

                ShowPregamePanel();
            });
    }
    public void ShowPregamePanel()
    {
        if (optionPanel != null)
            optionPanel.SetActive(false);

        if (pregamePanel != null)
            pregamePanel.SetActive(true);

        if (gameplayPanel != null)
            gameplayPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        Debug.Log("[RESUME] Showing pregame panel");
    }

    public void OnSwitchTestUserPressed()
    {
        if (auth == null)
        {
            Debug.LogWarning("[PregamePanel] Cannot switch test user, auth is null.");
            return;
        }

        string currentEmail = auth.CurrentUser != null ? auth.CurrentUser.Email : null;

        string targetEmail = (currentEmail == TestUserA_Email)
            ? TestUserB_Email
            : TestUserA_Email;

        Debug.Log("[PregamePanel] Switching test user: " + (currentEmail ?? "none") + " -> " + targetEmail);

        if (auth.CurrentUser != null)
            auth.SignOut();

        SetStatus("Switching to " + targetEmail + "...");

        auth.SignInWithEmailAndPasswordAsync(targetEmail, TestUserPassword).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                RunOnMainThread(() => SetStatus("Switch canceled."));
                return;
            }

            if (task.IsFaulted)
            {
                string err = GetFirebaseErrorMessage(task.Exception);
                Debug.LogError("[PregamePanel] Switch test user failed: " + err);
                RunOnMainThread(() => SetStatus("Switch failed: " + err));
                return;
            }

            FirebaseUser signedInUser = task.Result.User;

            RunOnMainThread(() =>
            {
                Debug.Log("[PregamePanel] RunOnMainThread action START for switch to " + targetEmail);

                string shownName = string.IsNullOrWhiteSpace(signedInUser.DisplayName)
                    ? signedInUser.Email
                    : signedInUser.DisplayName;

                SetStatus("Switched to: " + shownName);
                if (signedInAsText != null)
                    signedInAsText.text = "Signed in as: " + shownName;

                RefreshUI();

                if (matchStatusPanel != null)
                {
                    Debug.Log("[PregamePanel] Calling RefreshMatchStateForUser with uid=" + signedInUser.UserId);
                    matchStatusPanel.gameObject.SetActive(true);
                    matchStatusPanel.UpdateLoginNameDisplay();
                    matchStatusPanel.RefreshMatchStateForUser(signedInUser.UserId);
                }
                else
                {
                    Debug.LogWarning("[PregamePanel] matchStatusPanel is NULL in switch-user callback!");

                }
                Debug.Log("[PregamePanel] RunOnMainThread action END");
            });
        });
    }

    private bool EnsureFirebaseReady()
    {
        if (dbRoot != null && auth != null)
            return true;

        if (FirebaseInit.IsReady && FirebaseInit.Database != null)
        {
            dbRoot = FirebaseInit.Database.RootReference;
            auth = FirebaseInit.Auth;
            firebaseInitialized = true;
            Debug.Log("[PreGamePanel] Firebase self-healed via EnsureFirebaseReady.");
            return true;
        }

        Debug.LogWarning("[PreGamePanel] EnsureFirebaseReady failed: Firebase not ready yet.");
        return false;
    }
    private void RemoveRoomFromUser(string uid, string roomCode, Action onComplete = null)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(roomCode))
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log("[PreGamePanel] RemoveRoomFromUser uid=" + uid + " roomCode=" + roomCode);

        dbRoot.Child("users").Child(uid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.Result == null || !task.Result.Exists)
            {
                Debug.LogWarning("[PreGamePanel] RemoveRoomFromUser read failed or user missing for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            string json = task.Result.GetRawJsonValue();
            UserData userData = JsonUtility.FromJson<UserData>(json);
            if (userData == null)
            {
                Debug.LogWarning("[PreGamePanel] RemoveRoomFromUser could not parse UserData for uid=" + uid);
                onComplete?.Invoke();
                return;
            }

            if (userData.activeRoomIds != null)
                userData.activeRoomIds.Remove(roomCode);

            var updates = new Dictionary<string, object>
        {
            { "activeRoomIds", userData.activeRoomIds }
        };

            dbRoot.Child("users").Child(uid)
                  .UpdateChildrenAsync(updates)
                  .ContinueWithOnMainThread(writeTask =>
                  {
                      if (writeTask.IsFaulted)
                      {
                          Debug.LogError("[PreGamePanel] RemoveRoomFromUser write failed for uid=" + uid + ": " + writeTask.Exception);
                      }
                      else
                      {
                          Debug.Log("[PreGamePanel] RemoveRoomFromUser updated activeRoomIds for uid=" + uid +
                                    " -> [" + (userData.activeRoomIds == null
                                                ? ""
                                                : string.Join(",", userData.activeRoomIds)) + "]");
                      }

                      onComplete?.Invoke();
                  });
        });
    }
}
