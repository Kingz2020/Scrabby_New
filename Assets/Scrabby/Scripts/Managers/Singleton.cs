using System.Collections.Generic;
using UnityEngine;

public class Singleton : MonoBehaviour
{
    public static Singleton Instance { get; private set; }

    public DropManager DropManager { get; private set; }
    public UIManager UIManager { get; private set; }
    public DebugManager DebugManager { get; private set; }
    public GameLogic GameLogic { get; private set; }
    public WordLookupLogic WordLookupLogic { get; private set; }
    public OnlineMatchController OnlineMatchController { get; private set; }

    private void Awake()
    {
        Debug.Log("[SINGLETON] Awake ran on " + gameObject.name + " frame " + Time.frameCount);
        Debug.Log("[SINGLETON] Awake on " + gameObject.name);

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SINGLETON] Duplicate singleton destroyed on " + gameObject.name);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        DropManager = GetComponentInChildren<DropManager>(true);
        UIManager = GetComponentInChildren<UIManager>(true);
        DebugManager = GetComponentInChildren<DebugManager>(true);
        GameLogic = GetComponentInChildren<GameLogic>(true);
        WordLookupLogic = GetComponentInChildren<WordLookupLogic>(true);
        OnlineMatchController = GetComponentInChildren<OnlineMatchController>(true);

        Debug.Log("[SINGLETON] Instance assigned. UIManager null? " + (UIManager == null));
        Debug.Log("[SINGLETON] GameLogic null? " + (GameLogic == null));
        Debug.Log("[SINGLETON] OnlineMatchController null? " + (OnlineMatchController == null));

        if (OnlineMatchController == null)
            Debug.LogError("[SINGLETON] OnlineMatchController was not found under Manager.");
    }
}