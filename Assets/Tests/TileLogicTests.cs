using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

public class TileLogicTests
{
    private GameLogic CreateLogic()
    {
        var go = new GameObject();
        return go.AddComponent<GameLogic>();
    }

    private LetterInfo MakeTile(string letter, int points = 1)
    {
        return new LetterInfo(letter, points);
    }

    [Test]
    public void CanBuildWordFromTiles_EnoughLetters_ReturnsTrue()
    {
        var logic = CreateLogic();

        var tiles = new List<LetterInfo>
        {
            MakeTile("C"),
            MakeTile("A"),
            MakeTile("T")
        };

        bool result = logic.CanBuildWordFromTiles_TestHook("CAT", tiles);

        Assert.IsTrue(result);
    }

    [Test]
    public void CanBuildWordFromTiles_MissingRepeatedLetter_ReturnsFalse()
    {
        var logic = CreateLogic();

        var tiles = new List<LetterInfo>
        {
            MakeTile("B"),
            MakeTile("A"),
            MakeTile("L")
        };

        bool result = logic.CanBuildWordFromTiles_TestHook("BALL", tiles);

        Assert.IsFalse(result);
    }

    [Test]
    public void CanBuildWordFromTiles_RepeatedLetterAvailable_ReturnsTrue()
    {
        var logic = CreateLogic();

        var tiles = new List<LetterInfo>
        {
            MakeTile("B"),
            MakeTile("A"),
            MakeTile("L"),
            MakeTile("L")
        };

        bool result = logic.CanBuildWordFromTiles_TestHook("BALL", tiles);

        Assert.IsTrue(result);
    }

    [Test]
    public void CanBuildWordFromTiles_WordUsesLetterNotInRack_ReturnsFalse()
    {
        var logic = CreateLogic();

        var tiles = new List<LetterInfo>
        {
            MakeTile("C"),
            MakeTile("A"),
            MakeTile("T")
        };

        bool result = logic.CanBuildWordFromTiles_TestHook("CAR", tiles);

        Assert.IsFalse(result);
    }
}