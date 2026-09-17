// Scrabby push notifications.
//
// The game never sends a notification itself: a phone cannot be trusted with
// the right to message other phones. It writes its move to the database as it
// always has, and these functions, running on Firebase's servers, notice the
// write and tell the other player.
//
// Where each player's phone can be reached is kept at pushTokens/{uid}/token,
// written by the game (PushNotifications.cs).

const { onValueCreated, onValueWritten } = require("firebase-functions/v2/database");
const { initializeApp } = require("firebase-admin/app");
const { getDatabase } = require("firebase-admin/database");
const { getMessaging } = require("firebase-admin/messaging");
const logger = require("firebase-functions/logger");

initializeApp({
  databaseURL: "https://partyscrabby-default-rtdb.europe-west1.firebasedatabase.app",
});

// The database lives in europe-west1; its triggers have to run there too.
const on = (ref) => ({ ref, instance: "partyscrabby-default-rtdb", region: "europe-west1" });

const db = () => getDatabase();

// A player without an alias is known by their email; the part before the @
// reads as a name, the whole address does not.
function shortName(name) {
  if (!name) return "Your opponent";
  const at = name.indexOf("@");
  return at > 0 ? name.substring(0, at) : name;
}

async function read(path) {
  const snap = await db().ref(path).get();
  return snap.exists() ? snap.val() : null;
}

// Sends one notification to one player, if their phone has told us where it
// is. A notification for the same match replaces the last one (the tag), so a
// long game does not stack up a pile of them.
async function notify(uid, body, matchOrRoomId) {
  if (!uid) return;

  const token = await read(`pushTokens/${uid}/token`);
  if (!token) {
    logger.info(`[PUSH] ${uid} has no token; nothing sent.`);
    return;
  }

  try {
    await getMessaging().send({
      token,
      notification: { title: "Scrabby", body },
      data: { id: matchOrRoomId || "" },
      android: {
        priority: "high",
        notification: { tag: matchOrRoomId || "scrabby" },
      },
    });
    logger.info(`[PUSH] sent to ${uid}: ${body}`);
  } catch (err) {
    // The app was uninstalled or the token replaced: forget it, so it is not
    // tried again every turn.
    if (err.code === "messaging/registration-token-not-registered" ||
        err.code === "messaging/invalid-registration-token") {
      await db().ref(`pushTokens/${uid}`).remove();
      logger.info(`[PUSH] token for ${uid} was stale; removed.`);
    } else {
      logger.error(`[PUSH] send to ${uid} failed`, err);
    }
  }
}

// Someone played their word. Tell the other player - either that it is their
// move, or, if they had already played, that the round (or the game) is
// decided.
exports.onWordPlayed = onValueCreated(
  on("/matches/{matchId}/rounds/{round}/submissions/{uid}"),
  async (event) => {
    const { matchId, round, uid } = event.params;
    const base = `matches/${matchId}`;

    const [p1, p2, p1Name, p2Name, totalRounds] = await Promise.all([
      read(`${base}/player1Uid`),
      read(`${base}/player2Uid`),
      read(`${base}/player1DisplayName`),
      read(`${base}/player2DisplayName`),
      read(`${base}/totalRounds`),
    ]);

    const other = uid === p1 ? p2 : p1;

    // A quick game nobody has joined yet: there is no one to tell.
    if (!other) return;

    const playerName = shortName(uid === p1 ? p1Name : p2Name);
    const otherPlayed = await read(`${base}/rounds/${round}/submissions/${other}`);

    let body;
    if (!otherPlayed) {
      body = `${playerName} played their word in round ${round}. Your move!`;
    } else if (Number(round) >= Number(totalRounds || 0)) {
      body = `Your game with ${playerName} is over - see who won.`;
    } else {
      body = `Round ${round} with ${playerName} is decided - see whose word won.`;
    }

    await notify(other, body, matchId);
  });

// Someone took the empty seat of a quick game. The player who opened it has
// been waiting, possibly for hours.
exports.onQuickGameJoined = onValueWritten(
  on("/matches/{matchId}/player2Uid"),
  async (event) => {
    const before = event.data.before.val();
    const after = event.data.after.val();

    // Only the empty seat being filled. A match made from an invitation is
    // created with both players already in it, and must not trigger this.
    if (before !== "" || !after) return;

    const { matchId } = event.params;
    const [p1, p2Name] = await Promise.all([
      read(`matches/${matchId}/player1Uid`),
      read(`matches/${matchId}/player2DisplayName`),
    ]);

    await notify(p1, `${shortName(p2Name)} joined your quick game.`, matchId);
  });

// An invitation arrived.
exports.onInvite = onValueCreated(
  on("/users/{uid}/invites/{roomCode}"),
  async (event) => {
    const invite = event.data.val() || {};
    const { uid, roomCode } = event.params;

    await notify(uid, `${shortName(invite.fromDisplayName)} invited you to a game.`, roomCode);
  });
