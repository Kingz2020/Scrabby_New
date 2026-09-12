# PROTOTYPE — round replay animation

Throwaway. Not shipped code, not imported by Unity (lives outside `Assets/`).

**Question:** what should the round replay look like, in solo and in multiplayer?

**Answer: B, "Cascade & settle"** — chosen 2026-09-12. It keeps the existing
beat order (player 1, player 2, winner) so nothing about the round's structure
had to change, and fixes the motion. Folded into `UIManager.PlayMovePreview`,
`TileScript`, `GameLogic.ReplaySoloRound` and
`OnlineMatchController.ApplyReplayTilesAfterBoardBuild`.

One deliberate difference from the prototype: B's score chip flies up off the
finished word. The shipped version pins the score to the word using the
`HighlightPlayedWord` box and badge that already existed, in the player's
colour, rather than adding a second floating-score mechanism.

**Run it:** double-click `index.html`. Space plays, ← / → switch variants, or put
`?variant=A|B|C|D` in the URL. The Solo / Multiplayer toggle switches which of the two
real code paths is being imitated. The State panel logs each beat with the Unity method
it stands for.

## Variants

| | | |
|---|---|---|
| **A** | As shipped today | `UIManager.PlayMovePreview` + `TileScript.PlayWinningReplayDrop`, reproduced warts and all. The control. |
| **B** | Cascade & settle | Same beats, rebuilt motion: overlapping gravity drops, squash landing, colour held for the whole word, word-level punch, score chip, fade-out exit. |
| **C** | Spotlight sweep | No falling. Board dims, a light sweep reveals the word in reading order, underline grows, score counts up. |
| **D** | Head to head | Both moves land at once, scores pop side by side, loser drops away, winner slams with a shockwave. Three beats become one. |

## What the control exposed

Reproducing A faithfully surfaced five things in the shipped animation:

1. **Pop-in.** `CreateReplayPreviewTile` instantiates every tile fully visible before the
   loop hides and drops them, so the whole word flashes on screen first.
   ([UIManager.cs:724](../../Assets/Scrabby/Scripts/Managers/UIManager.cs#L724))
2. **`totalDuration` is not the duration.** It is divided per tile, then each tile adds a
   hard-coded `WaitForSecondsRealtime(0.25f)`. A 4-tile word asking for 1.5s takes 2.5s;
   the round takes ~10.6s end to end.
   ([TileScript.cs:168](../../Assets/Scrabby/Scripts/Gameplay/Tiles/TileScript.cs#L168))
3. **The player colour never reads.** `PlayWinningReplayDrop` restores the original letter
   colour at the end of each tile, so by the time the word is complete the colour coding
   that distinguishes player 1 / player 2 / winner is gone.
4. **No exit.** `RemoveReplayPreviewTiles` destroys the word outright — it vanishes between
   beats.
5. **Multiplayer is silent.** `GameLogic.ReplaySoloRound` narrates every beat through
   `ShowRoundMessage`; `GameLogic.ReplayOnlineRound` plays the identical tiles with no
   narration, so online viewers see words appear and disappear with no idea who played them.

Measured round length at 1×: A ≈ 10.6s, B ≈ 7.4s, C ≈ 8.2s, D ≈ 4.6s.

## When a variant wins

Fold the winner into `PlayMovePreview` / `PlayWinningReplayDrop` (rewritten properly — this
code was written under prototype rules: no tests, no error handling), and keep whichever
variant lost on a throwaway branch rather than in `main`. Item 5 is worth fixing regardless
of which variant wins: both replay paths should narrate.
