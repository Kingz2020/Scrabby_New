using System;
using UnityEngine;
using Firebase.Database;
using Firebase.Auth;
using Firebase.Extensions;

public class FirebaseMatchService : MonoBehaviour
{
    private DatabaseReference db;

    void Start()
    {
        db = FirebaseDatabase.DefaultInstance.RootReference;
    }

    public void CreateUserProfile(string displayName)
    {
        var user = FirebaseInit.Auth.CurrentUser;
        if (user == null)
        {
            Debug.LogError("No signed-in user.");
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var profile = new UserProfile(user.Email, displayName, now);
        string json = JsonUtility.ToJson(profile);

        db.Child("users").Child(user.UserId).SetRawJsonValueAsync(json)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted) Debug.LogError("CreateUserProfile failed: " + task.Exception);
                else Debug.Log("User profile saved.");
            });
    }

    public void CreateRoom(string roomCode)
    {
        var user = FirebaseInit.Auth.CurrentUser;
        if (user == null)
        {
            Debug.LogError("No signed-in user.");
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var room = new RoomData(roomCode, user.UserId, now);
        string json = JsonUtility.ToJson(room);

        db.Child("rooms").Child(roomCode).SetRawJsonValueAsync(json)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("CreateRoom failed: " + task.Exception);
                    return;
                }

                db.Child("users").Child(user.UserId).Child("currentRoomId").SetValueAsync(roomCode);
                Debug.Log("Room created: " + roomCode);
            });
    }

    public void JoinRoom(string roomCode)
    {
        var user = FirebaseInit.Auth.CurrentUser;
        if (user == null)
        {
            Debug.LogError("No signed-in user.");
            return;
        }

        var roomRef = db.Child("rooms").Child(roomCode);

        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("JoinRoom read failed: " + task.Exception);
                return;
            }

            var snapshot = task.Result;
            if (!snapshot.Exists)
            {
                Debug.LogError("Room does not exist.");
                return;
            }

            string guestUid = snapshot.Child("guestUid").Value?.ToString();
            string hostUid = snapshot.Child("hostUid").Value?.ToString();

            if (!string.IsNullOrEmpty(guestUid))
            {
                Debug.LogError("Room already full.");
                return;
            }

            roomRef.Child("guestUid").SetValueAsync(user.UserId);
            roomRef.Child("status").SetValueAsync("full");
            db.Child("users").Child(user.UserId).Child("currentRoomId").SetValueAsync(roomCode);

            Debug.Log("Joined room: " + roomCode);

            CreateMatch(roomCode, hostUid, user.UserId);
        });
    }

    public void CreateMatch(string roomCode, string hostUid, string guestUid)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string matchId = db.Child("matches").Push().Key;

        var match = new MatchData(roomCode, hostUid, guestUid, now);
        string json = JsonUtility.ToJson(match);

        db.Child("matches").Child(matchId).SetRawJsonValueAsync(json)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("CreateMatch failed: " + task.Exception);
                    return;
                }

                db.Child("rooms").Child(roomCode).Child("matchId").SetValueAsync(matchId);
                db.Child("rooms").Child(roomCode).Child("status").SetValueAsync("starting");

                db.Child("users").Child(hostUid).Child("currentMatchId").SetValueAsync(matchId);
                db.Child("users").Child(guestUid).Child("currentMatchId").SetValueAsync(matchId);

                Debug.Log("Match created: " + matchId);
            });
    }
}