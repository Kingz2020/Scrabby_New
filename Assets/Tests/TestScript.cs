using NUnit.Framework;
using UnityEngine;

public class TestScript {
    
    [Test]
    public void CompareMoves_PlayerValid_AiInvalid_PlayerWins()
    {
        var go = new GameObject();
        var logic = go.AddComponent<GameLogic>();

        var playerMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 10,
            timeUsed = 12f,
            word = "CAT",
            isHuman = true
        };

        var aiMove = new GameLogic.RoundMove
        {
            isValid = false,
            score = 0,
            timeUsed = 70f,
            word = "",
            isHuman = false
        };

        // If CompareMoves stays private, this can't be tested directly.
        // Make it internal/public OR wrap it in a testable helper.
        var winner = logic.TestCompareMoves(playerMove, aiMove);

        Assert.IsNotNull(winner);
        Assert.IsTrue(winner.isHuman);
        Assert.AreEqual("CAT", winner.word);
    }
    
    [Test]
    public void RoundMove_CanBeCreated()
    {
        var move = new GameLogic.RoundMove
        {
            isValid = true,
            score = 12,
            word = "CAT",
            isHuman = true
        };

        Assert.IsTrue(move.isValid);
        Assert.AreEqual(12, move.score);
        Assert.AreEqual("CAT", move.word);
        Assert.IsTrue(move.isHuman);
    }

    [Test]
    public void CompareMoves_BothValid_PlayerHigherScore_PlayerWins()
    {
        var go = new GameObject();
        var logic = go.AddComponent<GameLogic>();

        var playerMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 20,
            timeUsed = 30f,
            word = "HOUSE",
            isHuman = true
        };

        var aiMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 15,
            timeUsed = 25f,
            word = "MOUSE",
            isHuman = false
        };

        var winner = logic.TestCompareMoves(playerMove, aiMove);

        Assert.IsNotNull(winner);
        Assert.IsTrue(winner.isHuman);
        Assert.AreEqual("HOUSE", winner.word);
    }

    [Test]
    public void CompareMoves_BothValid_SameScore_DifferentWords_PlayerWins()
    {
        var go = new GameObject();
        var logic = go.AddComponent<GameLogic>();

        var playerMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 30,
            timeUsed = 40f,
            word = "GARDEN",
            isHuman = true
        };

        var aiMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 30,
            timeUsed = 20f, // AI might be "faster"
            word = "PARDON",
            isHuman = false
        };

        var winner = logic.TestCompareMoves(playerMove, aiMove);

        Assert.IsNotNull(winner);
        Assert.IsTrue(winner.isHuman);      // your logic gives ties to the human
        Assert.AreEqual("GARDEN", winner.word);
    }

    [Test]
    public void CompareMoves_BothValid_SameScore_SameWord_PlayerWins()
    {
        var go = new GameObject();
        var logic = go.AddComponent<GameLogic>();

        var playerMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 25,
            timeUsed = 35f,
            word = "BRIDGE",
            isHuman = true
        };

        var aiMove = new GameLogic.RoundMove
        {
            isValid = true,
            score = 25,
            timeUsed = 10f,
            word = "BRIDGE",   // same word
            isHuman = false
        };

        var winner = logic.TestCompareMoves(playerMove, aiMove);

        Assert.IsNotNull(winner);
        Assert.IsTrue(winner.isHuman);      // human wins exact tie too
        Assert.AreEqual("BRIDGE", winner.word);
    }

    [Test]
    public void CompareMoves_BothInvalid_NoWinner()
    {
        var go = new GameObject();
        var logic = go.AddComponent<GameLogic>();

        var playerMove = new GameLogic.RoundMove
        {
            isValid = false,
            score = 0,
            timeUsed = 50f,
            word = "",
            isHuman = true
        };

        var aiMove = new GameLogic.RoundMove
        {
            isValid = false,
            score = 0,
            timeUsed = 60f,
            word = "",
            isHuman = false
        };

        var winner = logic.TestCompareMoves(playerMove, aiMove);

        Assert.IsNull(winner); // your code returns null when both invalid
    }
}