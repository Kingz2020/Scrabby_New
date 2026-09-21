using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The first two minutes, led by the hand.
//
// Scrabby looks like Scrabble and is not, and a card of rules only helps the
// people who read it, which is nobody. So the game plays its own opening move
// in front of the player: the hand taps Solo, picks Easy, presses Play, then
// puts the first tile down and hands over.
//
// The player does the dragging from the second tile on, because dragging is
// the one thing here that has to be felt rather than watched, and they press
// Play themselves so the round is theirs.
//
// Everything is real - the real menu, a real Easy game, the real computer
// answering. When the tutorial ends the player is three rounds into a game
// they are already playing, not back at a menu.
public class TutorialFlow : MonoBehaviour
{
    private const string DoneKey = "Scrabby.Tutorial.Done";

    // Set when the walkthrough finishes and cleared when the game it started
    // ends: the replay rows only exist on the game-over panel, and they are
    // where the one thing the player never saw - the word their opponent
    // played - can finally be watched.
    private const string ReplayHintKey = "Scrabby.Tutorial.ReplayHint";

    // The rack the tutorial deals itself. CAT is spelled from the first three;
    // the rest are there so the rack looks like a rack, and so the computer
    // has something of its own to play.
    private const string Rack = "CATLOM";

    // Where CAT goes: across the middle of the board, so it reads as the
    // opening move it is.
    private static readonly int[,] Word = { { 4, 5 }, { 5, 5 }, { 6, 5 } };

    private TutorialOverlay overlay;
    private OptionPanelController options;
    private bool skipped;

    public static bool AlreadyRun
    {
        get { return PlayerPrefs.GetInt(DoneKey, 0) != 0; }
    }

    public static void Forget()
    {
        PlayerPrefs.DeleteKey(DoneKey);
        PlayerPrefs.Save();
    }

    // Shown once, the first time anyone opens the game.
    //
    // Once means once: it counts as seen the moment it starts, not when it
    // reaches the end. Marked only at the end, anyone who closed the app
    // halfway - or stopped Play in the editor - got it again on every launch.
    // It is still in Settings for whoever wants it again.
    //
    // Never on its own in the editor: there the game is started over and over
    // to test other things, and the walkthrough is in the way every time.
    public static void RunIfNew()
    {
        if (Application.isEditor || AlreadyRun)
            return;

        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();

        Begin();
    }

    public static void Begin()
    {
        if (GameObject.Find("TutorialFlow") != null)
            return;

        GameObject go = new GameObject("TutorialFlow");
        TutorialFlow flow = go.AddComponent<TutorialFlow>();

        flow.StartCoroutine(flow.Run());
    }

    // Called by the game-over panel. Nothing happens unless the game that has
    // just ended was the one the tutorial started.
    public static void ShowReplayHintIfOwed()
    {
        if (PlayerPrefs.GetInt(ReplayHintKey, 0) == 0)
            return;

        // Settled whichever way this goes: owed once, to one game.
        PlayerPrefs.SetInt(ReplayHintKey, 0);
        PlayerPrefs.Save();

        // The saved flag alone is not proof. A tutorial left halfway - the app
        // closed, Play stopped in the editor - leaves it set, and the hand
        // then turned up at the end of some later, ordinary game. Only the
        // tutorial's own game gets it.
        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;

        if (logic == null || !logic.IsTutorialGame)
            return;

        if (GameObject.Find("TutorialReplayHint") != null)
            return;

        GameObject go = new GameObject("TutorialReplayHint");
        TutorialFlow flow = go.AddComponent<TutorialFlow>();

        flow.StartCoroutine(flow.ReplayHint());
    }

    private IEnumerator ReplayHint()
    {
        // The rows are built as the panel comes up, so they are not there the
        // instant it is enabled.
        float waited = 0f;
        RoundReplayRow row = FindFirstObjectByType<RoundReplayRow>();

        while (row == null && waited < 4f)
        {
            waited += Time.deltaTime;
            yield return null;
            row = FindFirstObjectByType<RoundReplayRow>();
        }

        if (row == null)
        {
            Destroy(gameObject);
            yield break;
        }

        overlay = TutorialOverlay.Open();

        if (overlay == null)
        {
            Destroy(gameObject);
            yield break;
        }

        overlay.OnSkip = delegate { skipped = true; };

        yield return Wait(0.8f);

        // How the round was settled, said over the result it produced - which
        // is on screen now, so it can be pointed at rather than described.
        overlay.Undim();

        yield return overlay.SayAndWait(
            "The higher score takes the round, and the winning word stays on " +
            "the board. A real game is four rounds.",
            TutorialOverlay.Where.Top);

        if (skipped)
        {
            overlay.Close();
            Destroy(gameObject);
            yield break;
        }

        RectTransform rect = row.transform as RectTransform;

        // The whole row lit, so the round it describes can be read, and the
        // hand on its Play button - the thing to press. Pointing at the middle
        // of the row left the player to guess which part of it did anything.
        Button button = row.ReplayButton != null ? row.ReplayButton : row.GetComponent<Button>();
        RectTransform pointAt = button != null ? button.transform as RectTransform : rect;

        overlay.Spotlight(rect);
        overlay.LetThemTouch(true);
        overlay.Say("Tap <b>Play</b> to watch the round again - including " +
                    "<b>the word your opponent played</b>, which you never get " +
                    "to see while the round is live.",
                    TutorialOverlay.Where.Top);

        yield return overlay.MoveHandTo(pointAt);

        bool tapped = false;
        UnityEngine.Events.UnityAction watcher = delegate { tapped = true; };

        if (button != null)
            button.onClick.AddListener(watcher);

        waited = 0f;
        float sincePulse = 1f;

        // Checked every frame, not between tap animations. The replay this is
        // pointing at plays out on the board behind the dimming, so a second
        // of overlay left over is a second of the thing they asked to see,
        // hidden. The panel stepping aside for the replay counts as the press
        // too, belt and braces.
        //
        // No time limit any more: the rest of the walkthrough waits for the
        // player, and a hint that gave up after fourteen seconds was the one
        // step that did not.
        while (!tapped && !skipped && GameOverShowing())
        {
            waited += Time.deltaTime;
            sincePulse += Time.deltaTime;

            if (sincePulse > 1.1f)
            {
                sincePulse = 0f;

                // Run on the overlay, so destroying it stops the animation
                // rather than leaving a coroutine poking at nothing.
                overlay.StartCoroutine(overlay.Tap());
            }

            yield return null;
        }

        if (button != null)
            button.onClick.RemoveListener(watcher);

        overlay.Close();
        Destroy(gameObject);
    }

    private IEnumerator Run()
    {
        options = FindFirstObjectByType<OptionPanelController>();

        if (options == null)
        {
            Debug.LogWarning("[TUTORIAL] No menu to lead anyone through.");
            Destroy(gameObject);
            yield break;
        }

        overlay = TutorialOverlay.Open();

        if (overlay == null)
        {
            Destroy(gameObject);
            yield break;
        }

        overlay.OnSkip = delegate { skipped = true; };

        // A moment before the first hand appears, or it lands on a screen the
        // player has not looked at yet.
        yield return Wait(0.6f);

        yield return Opening();
        if (skipped) { Finish(); yield break; }

        yield return Menu();
        if (skipped) { Finish(); yield break; }

        yield return Board();
        if (skipped) { Finish(); yield break; }

        yield return FirstTile();
        if (skipped) { Finish(); yield break; }

        yield return TheirTurn();
        if (skipped) { Finish(); yield break; }

        yield return TheAnswer();

        Finish();
    }

    // ------------------------------------------------------------ the steps --

    private IEnumerator Opening()
    {
        overlay.NoSpotlight();
        overlay.LetThemTouch(false);

        yield return overlay.SayAndWait(
            "Scrabby is a word duel.\nLet's play one round together.",
            TutorialOverlay.Where.Auto);
    }

    // Every press is the player's own, at their own pace. The hand shows where;
    // it never presses. Watching a hand do it all went by too fast to follow,
    // and a caption only gets read when nothing moves on until it has been.
    private IEnumerator Menu()
    {
        // Somewhere else to start from, so that pressing Solo and Easy visibly
        // changes something. Pressing the tab that is already chosen teaches
        // nothing about what the tabs are.
        options.ShowMultiplayerTab();
        options.ChooseDifficulty(GameLogic.SoloDifficulty.Hard);

        yield return PlayerPresses(options.SoloTabRect,
            "Tap <b>Solo</b> to play against the computer.",
            TutorialOverlay.Where.Auto);

        if (skipped)
            yield break;

        // The four levels shown together first, so the choice is seen to be a
        // choice, then only Easy lit - the other three cannot be pressed by
        // accident while the player is being asked for this one.
        overlay.Spotlight(options.DifficultyRowRect);
        overlay.LetThemTouch(false);

        yield return overlay.SayAndWait(
            "Four levels, from Easy to Expert. You can change it any time.",
            TutorialOverlay.Where.Auto);

        if (skipped)
            yield break;

        yield return PlayerPresses(options.EasyChipRect,
            "Tap <b>Easy</b> to start gently.",
            TutorialOverlay.Where.Auto);

        if (skipped)
            yield break;

        yield return PlayerPresses(options.PlayRect,
            "Now tap <b>Play</b>.",
            TutorialOverlay.Where.Auto);
    }

    private IEnumerator Board()
    {
        overlay.HideHand();
        overlay.NoSpotlight();
        overlay.Hush();

        // The board cells are built when the screen first appears.
        float waited = 0f;

        while (CheckWordButton() == null && waited < 8f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        Button deal = CheckWordButton();

        if (deal == null)
        {
            Debug.LogWarning("[TUTORIAL] The board never appeared; stopping.");
            skipped = true;
            yield break;
        }

        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;

        // Asked for before the round begins, so the six letters and the four
        // bonus squares are dealt as they are meant to be rather than dealt
        // at random and corrected afterwards.
        if (logic != null)
            logic.SetUpTutorialRound(Rack);

        // Owed from here: the game is one round long now, so its game-over
        // panel can arrive while this coroutine is still speaking.
        PlayerPrefs.SetInt(ReplayHintKey, 1);
        PlayerPrefs.Save();

        // Nothing has been dealt yet. This button does two jobs: the first
        // press starts the round - letters out, bonus squares scattered - and
        // every press after it plays the word. The player presses it, so they
        // see the letters and the bonus squares arrive because they asked.
        yield return PlayerPresses(deal.transform as RectTransform,
            "This button deals your letters and scatters the bonus squares. Tap it.",
            TutorialOverlay.Where.Top);

        if (skipped)
            yield break;

        waited = 0f;

        while (RackTiles().Count < 6 && waited < 10f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        yield return Wait(0.4f);

        // Seventy seconds is the round, and the tutorial is about to spend
        // some of them talking. The clock waits.
        if (logic != null)
            logic.HoldTheClock(true);

        yield return null;

        // The board scatters its bonus squares one at a time at the start of a
        // round, and nothing may be said or placed until it has finished. Talk
        // over it and the hand points at a board still filling itself in; put
        // a tile down during it and that square's bonus is skipped entirely,
        // because the board does not draw a bonus under a letter.
        yield return BonusSquaresSettled();

        RectTransform rack = RackRect();

        overlay.HideHand();
        overlay.Spotlight(rack);
        overlay.LetThemTouch(false);

        yield return overlay.SayAndWait(
            "You and the computer are dealt <b>the same six letters</b>. " +
            "You both play them, on the same board.",
            TutorialOverlay.Where.Top);

        if (skipped)
            yield break;

        // The triple word, which is where CAT is about to land - so the
        // scoring can be explained with the square it happens on rather than
        // in the abstract.
        RectTransform triple = CellAt(5, 5) != null
            ? CellAt(5, 5).transform as RectTransform
            : null;

        if (triple != null)
        {
            overlay.Spotlight(triple, 8f);

            yield return overlay.SayAndWait(
                "Four bonus squares are scattered on the board. This one " +
                "<b>triples the whole word</b> - and they are scattered " +
                "again after every round.",
                TutorialOverlay.Where.Top);
        }
    }

    // The hand points at something real and waits, pulsing, for the player to
    // press it themselves. Only that one thing is lit, so it is the only thing
    // on screen that can be pressed.
    private IEnumerator PlayerPresses(RectTransform target, string words,
                                      TutorialOverlay.Where where)
    {
        if (target == null || skipped)
            yield break;

        Button button = target.GetComponent<Button>();

        if (button == null)
        {
            Debug.LogWarning("[TUTORIAL] " + target.name + " is not a button; skipping that step.");
            yield break;
        }

        overlay.Spotlight(target);
        overlay.LetThemTouch(true);
        overlay.Say(words, where);

        yield return overlay.WaitForPress(button, () => skipped);

        overlay.LetThemTouch(false);

        // A moment for whatever the press opened to arrive before the next
        // thing is lit, or the spotlight is cut around a screen mid-change.
        yield return Wait(0.45f);
    }

    // Waits for the scatter to finish, with a ceiling: a tutorial that hangs
    // because a view never said it had stopped is worse than one that starts
    // talking a moment early.
    private IEnumerator BonusSquaresSettled()
    {
        BonusBoardView view = FindFirstObjectByType<BonusBoardView>();

        if (view == null)
            yield break;

        float waited = 0f;

        while (view.IsRevealing && waited < 8f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // A breath after the last one lands, so the board is still before
        // anything is said about it.
        yield return Wait(0.5f);

        // And a check, because a tutorial that explains bonus squares to
        // someone looking at a board without any is worse than one that never
        // mentions them.
        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;
        BonusTile[,] board = logic != null ? logic.GetBoardBonusTiles() : null;

        int onTheBoard = 0;

        if (board != null)
        {
            foreach (BonusTile bonus in board)
            {
                if (bonus != null)
                    onTheBoard++;
            }
        }

        int drawn = FindObjectsByType<BonusTileView>(FindObjectsSortMode.None).Length;

        ScrabbyLog.Trace("[TUTORIAL] Bonus squares: " + onTheBoard + " scattered, " +
                         drawn + " drawn.");

        // Scattered but not drawn means the board was not ready when the
        // reveal ran. Ask for them again rather than explaining bonus squares
        // to someone looking at a board without any.
        if (onTheBoard > 0 && drawn == 0)
        {
            Debug.LogWarning("[TUTORIAL] Bonus squares were placed but not drawn; redrawing.");

            view.DrawBonusTilesImmediately();
            yield return null;
        }
    }

    // The hand puts the first tile down, so dragging has been seen once.
    private IEnumerator FirstTile()
    {
        TileScript tile = RackTile("C");
        GhostTile cell = CellAt(Word[0, 0], Word[0, 1]);

        if (tile == null || cell == null)
        {
            Debug.LogWarning("[TUTORIAL] The board is not laid out as expected; stopping.");
            skipped = true;
            yield break;
        }

        overlay.SpotlightAll(new RectTransform[] { RackRect(), BoardRect() }, 24f);
        overlay.LetThemTouch(false);

        yield return overlay.SayAndWait(
            "Letters go on the board by dragging them there. " +
            "Watch this one - then the next two are yours.",
            TutorialOverlay.Where.Top);

        if (skipped)
            yield break;

        overlay.Say("", TutorialOverlay.Where.Top);
        overlay.Hush();

        yield return Drag(tile, cell);
        yield return Wait(0.5f);
    }

    // And now they do it.
    private IEnumerator TheirTurn()
    {
        GhostTile a = CellAt(Word[1, 0], Word[1, 1]);
        GhostTile t = CellAt(Word[2, 0], Word[2, 1]);

        if (a == null || t == null)
        {
            skipped = true;
            yield break;
        }

        // Whatever the pointer crossed during the demonstration is forgotten, so
        // the player's first drag lands where they let go of it.
        if (Singleton.Instance != null && Singleton.Instance.DropManager != null)
            Singleton.Instance.DropManager.ForgetWhereThePointerWas();

        overlay.HideHand();
        overlay.SpotlightAll(new RectTransform[] { RackRect(), BoardRect() }, 24f);
        overlay.LetThemTouch(true);
        overlay.Mark(a.transform as RectTransform);
        overlay.Mark(t.transform as RectTransform);
        overlay.Say("Your turn. Drag <b>A</b> and <b>T</b> onto the two marked squares.",
                    TutorialOverlay.Where.Top);

        float waited = 0f;

        while (!(Taken(a) && Taken(t)))
        {
            if (skipped)
                yield break;

            waited += Time.deltaTime;

            // A nudge for anyone who has stopped, without nagging anyone who
            // is simply thinking.
            if (waited > 12f)
            {
                overlay.Say("Drag the <b>A</b> and the <b>T</b> from your rack " +
                            "onto the marked squares to spell CAT.",
                            TutorialOverlay.Where.Top);
                waited = 0f;
            }

            yield return null;
        }

        overlay.ClearMarks();
    }

    private IEnumerator TheAnswer()
    {
        Button check = CheckWordButton();

        if (check == null)
        {
            skipped = true;
            yield break;
        }

        yield return PlayerPresses(check.transform as RectTransform,
            "<b>CAT</b> - that's a word. Tap the same button again to play it.",
            TutorialOverlay.Where.Top);

        GameLogic playing = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;

        if (playing != null)
            playing.HoldTheClock(false);

        if (skipped)
            yield break;

        // Out of the way: what happens next is the lesson, and it cannot be
        // taught from behind a grey sheet.
        overlay.HideHand();
        overlay.Undim();

        // The sum, on the square it happened on. Told once, with real numbers,
        // at the moment the player can see where they came from.
        yield return overlay.SayAndWait(
            "<b>That's it!</b> C is 3, A is 1, T is 1 - and the triple word " +
            "square makes that <b>15</b>.",
            TutorialOverlay.Where.Top);

        if (skipped)
            yield break;

        overlay.Say("Now the computer plays the same six letters.",
                    TutorialOverlay.Where.Top);

        // A tutorial game is one round, so what comes next is the result,
        // not another rack. Waiting on the round number would wait for ever.
        float waited = 0f;

        while (!GameOverShowing() && waited < 20f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // And this is where the walkthrough hands over. The game-over panel
        // brings up its own last word - how the round was decided, and the
        // replay rows - on an overlay of its own, which replaces this one.
        // Waiting here for a tap on an overlay that is about to be taken away
        // would wait for ever, and never mark the tutorial as done.
    }

    private void Finish()
    {
        // Someone who walked out does not want the hand back at the end.
        if (skipped)
        {
            PlayerPrefs.SetInt(ReplayHintKey, 0);
            PlayerPrefs.Save();
        }

        GameLogic logic = Singleton.Instance != null ? Singleton.Instance.GameLogic : null;

        // However this ended - finished or skipped - the game goes back to
        // running on its own clock.
        if (logic != null)
            logic.HoldTheClock(false);

        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();

        if (overlay != null)
            overlay.Close();

        Destroy(gameObject);
    }

    // ----------------------------------------------------------- the pieces --

    // The hand carrying a tile from the rack to a square - the same path a
    // finger takes, through the same code a finger uses.
    private IEnumerator Drag(TileScript tile, GhostTile cell)
    {
        RectTransform from = tile.transform as RectTransform;
        RectTransform to = cell.transform as RectTransform;

        yield return overlay.MoveHandTo(from);
        yield return overlay.Tap();

        tile.BeginDemoDrag();

        Vector2 start = overlay.PointOf(from);
        Vector2 end = overlay.PointOf(to);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / 0.85f;

            float e = t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
            Vector2 at = Vector2.Lerp(start, end, Mathf.Clamp01(e));

            overlay.PutHandAt(at);
            tile.transform.position = overlay.WorldPointOf(at);

            yield return null;
        }

        tile.EndDemoDrag(cell);
    }

    private IEnumerator Wait(float seconds)
    {
        float waited = 0f;

        while (waited < seconds && !skipped)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    // ------------------------------------------------------- finding things --

    private static List<TileScript> RackTiles()
    {
        List<TileScript> tiles = new List<TileScript>();

        GameObject rack = Singleton.Instance != null && Singleton.Instance.UIManager != null
            ? Singleton.Instance.UIManager.handTileHolder
            : null;

        if (rack == null)
            return tiles;

        foreach (TileScript tile in rack.GetComponentsInChildren<TileScript>(true))
            tiles.Add(tile);

        return tiles;
    }

    private static TileScript RackTile(string letter)
    {
        foreach (TileScript tile in RackTiles())
        {
            if (tile.LetterInfo != null &&
                string.Equals(tile.LetterInfo.letter, letter,
                              System.StringComparison.OrdinalIgnoreCase))
            {
                return tile;
            }
        }

        return null;
    }

    private static RectTransform RackRect()
    {
        GameObject rack = Singleton.Instance != null && Singleton.Instance.UIManager != null
            ? Singleton.Instance.UIManager.handTileHolder
            : null;

        return rack != null ? rack.transform as RectTransform : null;
    }

    private static RectTransform BoardRect()
    {
        GameObject board = Singleton.Instance != null && Singleton.Instance.UIManager != null
            ? Singleton.Instance.UIManager.gameBoard
            : null;

        return board != null ? board.transform as RectTransform : null;
    }

    private static GhostTile CellAt(int x, int y)
    {
        GameObject board = Singleton.Instance != null && Singleton.Instance.UIManager != null
            ? Singleton.Instance.UIManager.gameBoard
            : null;

        if (board == null)
            return null;

        foreach (GhostTile cell in board.GetComponentsInChildren<GhostTile>(true))
        {
            if (cell.letterPosition != null &&
                cell.letterPosition.RowX == x && cell.letterPosition.ColY == y)
            {
                return cell;
            }
        }

        return null;
    }

    private static bool Taken(GhostTile cell)
    {
        return cell != null && cell.GetComponentInChildren<TileScript>(true) != null;
    }

    // Any bonus square that is on the board right now, to point at while
    // explaining them.
    private static RectTransform ABonusSquare()
    {
        BonusTileView[] bonuses = FindObjectsByType<BonusTileView>(FindObjectsSortMode.None);

        foreach (BonusTileView bonus in bonuses)
        {
            if (bonus != null && bonus.gameObject.activeInHierarchy)
                return bonus.transform as RectTransform;
        }

        return null;
    }

    private static bool GameOverShowing()
    {
        UIManager ui = Singleton.Instance != null ? Singleton.Instance.UIManager : null;

        return ui != null && ui.gameOverPanel != null &&
               ui.gameOverPanel.activeInHierarchy;
    }

    private static Button CheckWordButton()
    {
        GameObject go = GameObject.Find("Check Word");

        return go != null ? go.GetComponent<Button>() : null;
    }
}
