using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Autostart : MonoBehaviour
{
    public Timer timer;
    //public GameObject startButton;
    public GameObject resetButton;
    public GameObject revealButton;

    void Start()
    {
        if (Singleton.Instance != null &&
            Singleton.Instance.GameLogic != null &&
            Singleton.Instance.GameLogic.IsOnlineMatch)
        {
            return; // online match already hydrated by PreGamePanel — don't overwrite it
        }

        Singleton.Instance.DebugManager.LoadFromJson();
        Singleton.Instance.DebugManager.StartNewGame();
    }

    public void StartClick()
    {
        Singleton.Instance.DebugManager.RefillHand();
        Singleton.Instance.DebugManager.ResetDisplayWords();
        //startButton.SetActive(false);
        resetButton.SetActive(true);
        revealButton.SetActive(true);
        timer.StartTimer();
    }
}