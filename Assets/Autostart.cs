using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Autostart: MonoBehaviour {

    public Timer timer;

    //public GameObject wordList;
    public GameObject startButton;
    public GameObject resetButton;
    public GameObject revealButton;
    
    void Start() {
        Singleton.Instance.DebugManager.LoadFromJson();
        Singleton.Instance.DebugManager.StartNewGame();
    }

    public void StartClick() {
        Singleton.Instance.DebugManager.RefillHand();
        Singleton.Instance.DebugManager.ResetDisplayWords();
        //wordList.SetActive(true);
        startButton.SetActive(false);
        resetButton.SetActive(true);
        revealButton.SetActive(true);
        timer.StartTimer();
    }
 
}
