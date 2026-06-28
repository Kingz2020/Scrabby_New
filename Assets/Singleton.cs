using System.Collections.Generic;
using UnityEngine;

public class Singleton: MonoBehaviour {
    public static Singleton Instance { get; private set; }
    public DropManager DropManager { get; private set; }
    public UIManager UIManager { get; private set; }
    public DebugManager DebugManager { get; private set; }
    public GameLogic GameLogic { get; private set; }
    public WordLookupLogic WordLookupLogic { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
            return;
        }
        Instance = this;
        DropManager = GetComponentInChildren<DropManager>();
        UIManager = GetComponentInChildren<UIManager>();
        DebugManager = GetComponentInChildren<DebugManager>();
        GameLogic = GetComponentInChildren<GameLogic>();
        WordLookupLogic = GetComponentInChildren<WordLookupLogic>();
    }
}
