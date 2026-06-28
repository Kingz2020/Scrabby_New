using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class UIManager: MonoBehaviour {

    public GameObject gameBoard;
    public GameObject handTileHolder;
    public GameObject basicTile;
    public WordlistDisplay wordlistDisplay;
    public WorldlistTitleHolder worldlistTitleHolder;
    [SerializeField] private TextMeshProUGUI roundMessageText;
    [SerializeField] private TextMeshProUGUI humanScoreText;
    [SerializeField] private TextMeshProUGUI aiScoreText;
    [SerializeField] private TextMeshProUGUI roundText;

    public void AddTileToHand(LetterInfo tileInfo) {
        /*Debug.Log("UIManager.AddTileToHand called for " + tileInfo.letter);
        GameObject tempTile = Instantiate(basicTile, handTileHolder.transform);
        tempTile.GetComponent<TileScript>().InitTile(tileInfo);*/

        Debug.Log("UIManager.AddTileToHand called for " + tileInfo.letter);
        GameObject tempTile = Instantiate(basicTile);
        tempTile.transform.SetParent(handTileHolder.transform, false);
        tempTile.GetComponent<TileScript>().InitTile(tileInfo);
    
}

    public void ReturnTilesToHand() {
        foreach (PlacedTile tile in Singleton.Instance.DropManager.GetTilesDroppedThisTurn()) {
            tile.GetComponentInChildren<TileScript>().transform.SetParent(handTileHolder.transform);
        }
        Singleton.Instance.DropManager.ResetLocations();
    }

    public void ResetDisplayWordList(List<string> letters) {
        worldlistTitleHolder.ResetAll();
        wordlistDisplay.ResetList(letters);
    }

    public void RemoveAllHandTiles() {
        for (int i = handTileHolder.transform.childCount - 1; i >= 0; i--) {
            Destroy(handTileHolder.transform.GetChild(i).gameObject);
        }
    }

    public void AddWord(string word) {
        wordlistDisplay.AddWord(word);
    }

    public void AddRedWord(string word) {
        wordlistDisplay.AddMissingWord(word);
    }

    public void ShowRoundMessage(string message)
    {
        if (roundMessageText != null)
            roundMessageText.text = message;
    }

    public void ClearRoundMessage()
    {
        if (roundMessageText != null)
            roundMessageText.text = "";
    }

    public void RemoveSingleHandTile(string letter, int points)
    {
        for (int i = 0; i < handTileHolder.transform.childCount; i++)
        {
            Transform child = handTileHolder.transform.GetChild(i);

            PlacedTile placedTile = child.GetComponent<PlacedTile>();
            if (placedTile == null)
                placedTile = child.GetComponentInChildren<PlacedTile>();

            if (placedTile == null || placedTile.letterInfo == null)
                continue;

            if (placedTile.letterInfo.letter == letter && placedTile.letterInfo.points == points)
            {
                Destroy(child.gameObject);
                return;
            }
        }

        Debug.LogWarning("RemoveSingleHandTile could not find tile " + letter + " (" + points + ") in hand UI.");
    }

    public void PlaceAITileOnBoard(LetterInfo tileInfo, LetterPosition letterPosition)
    {
        if (tileInfo == null || letterPosition == null)
        {
            Debug.LogWarning("PlaceAITileOnBoard received null tileInfo or letterPosition.");
            return;
        }

        GhostTile[] allGhostTiles = gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            if (ghostTile.letterPosition != null &&
                ghostTile.letterPosition.RowX == letterPosition.RowX &&
                ghostTile.letterPosition.ColY == letterPosition.ColY)
            {
                GameObject tempTile = Instantiate(basicTile);
                tempTile.transform.SetParent(ghostTile.transform, false);

                TileScript tileScript = tempTile.GetComponent<TileScript>();
                if (tileScript != null)
                {
                    tileScript.InitTile(tileInfo);
                }

                PlacedTile placedTile = tempTile.GetComponent<PlacedTile>();
                if (placedTile != null)
                {
                    placedTile.letterInfo = tileInfo;
                    placedTile.letterPosition = letterPosition;
                }

                return;
            }
        }

        Debug.LogWarning(
            "PlaceAITileOnBoard could not find GhostTile at row " +
            letterPosition.RowX + ", col " + letterPosition.ColY
        );
    }

    public void UpdateTotalScores(int humanScore, int aiScore)
    {
        if (humanScoreText != null)
            humanScoreText.text = "Human: " + humanScore;

        if (aiScoreText != null)
            aiScoreText.text = "AI: " + aiScore;
    }
    public void UpdateRoundText(int currentRound, int maxRounds)
    {
        if (roundText != null)
            roundText.text = "Round: " + currentRound + " / " + maxRounds;
    }

}
