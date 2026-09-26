using System;
using System.Collections.Generic;

// What a finished game looks like, whoever finished it.
//
// There is one result board. What changes between a solo game, an online
// match and the walkthrough is this sheet - the words, the verdict, and
// which ways out are worth offering - and not the board, which reads the
// sheet and knows nothing about where the game came from.
//
// Before this, two builders wrote their own row text and the card scavenged
// the panel's buttons by name. The two builders had drifted into different
// languages - "R1 KITE 60 v OK 7 YOU" on one card and "Round 1: KITE (60) vs
// OK (7) - You won" on the other, the second wide enough to run under the
// REVIEW button - and what the card offered depended on which questions it
// thought to ask the online controller. Everything either of them decided is
// decided here instead, once, by whoever knows the game.
public class ResultSheet
{
    // The verdict, big, and the numbers under it.
    public string Headline = "";
    public string Detail = "";

    // What the other player is called on this card: "AI" in a solo game, a
    // name or an alias in a match. The rows say it too, so it is given once.
    public string OpponentName = "AI";

    public List<Round> Rounds = new List<Round>();

    // The ways out. Each is a plain statement about this game rather than a
    // question the board has to know to ask.
    public bool OfferAnotherGame;      // solo: deal a new one
    public bool OfferBackToMatches;    // online: the list this came from
    public bool OfferRemoveGame;       // a finished match, which is yours to drop
    public bool OfferProgress = true;  // the chart, for everything but the walkthrough

    // Where the chart should open, if it is offered: the level just played,
    // or the opponent just played against.
    public string ProgressKey;

    // What to do when they ask for another game or to go back. Held here so
    // the board does not have to know who is listening.
    public Action AnotherGame;
    public Action BackToMatches;
    public Action RemoveGame;
    public Action MainMenu;

    // One round, as both players saw it. The board turns this into a line of
    // text - one place, so both modes read alike.
    public class Round
    {
        public int Number;

        public string MyWord = "";
        public int MyScore;
        public bool IPlayed = true;      // false when the round was passed or timed out

        public string TheirWord = "";
        public int TheirScore;
        public bool TheyPlayed = true;

        public Verdict Result = Verdict.Nobody;

        // Watching it again, if there is anything to watch.
        public Action Replay;
    }

    // Every card offers it and every card means the same thing by it, so it
    // is written once rather than passed in from each game.
    public static void BackToTheMenu()
    {
        OptionPanelController menu =
            UnityEngine.Object.FindAnyObjectByType<OptionPanelController>(
                UnityEngine.FindObjectsInactive.Include);

        if (menu != null)
            menu.ReturnToMainMenu();
    }

    public enum Verdict
    {
        Mine,
        Theirs,
        Nobody
    }
}
