using UnityEngine;

public class FirebaseDebugButtons : MonoBehaviour
{
    [SerializeField] private FirebaseMatchService matchService;

    public void TestCreateProfile()
    {
        matchService.CreateUserProfile("Kingz");
    }

    public void TestCreateRoom()
    {
        matchService.CreateRoom("ABCD12");
    }

    public void TestJoinRoom()
    {
        matchService.JoinRoom("ABCD12");
    }
}