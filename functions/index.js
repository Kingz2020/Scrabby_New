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
const { onSchedule } = require("firebase-functions/v2/scheduler");
const { initializeApp } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");
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

// Quick game's stand-ins. Not accounts: nobody signs in as one, and nothing
// is ever sent to one.
function isBot(uid) {
  return typeof uid === "string" && uid.startsWith("bot-");
}

async function read(path) {
  const snap = await db().ref(path).get();
  return snap.exists() ? snap.val() : null;
}

// Sends one notification to one player, if their phone has told us where it
// is. A notification for the same match replaces the last one (the tag), so a
// long game does not stack up a pile of them.
async function notify(uid, body, matchOrRoomId) {
  if (!uid || isBot(uid)) return;

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

    // Somebody real came after all: the bot planned for this seat stands
    // down, words and all.
    if (!isBot(after))
      await db().ref(`botQueue/${matchId}`).remove();
  });

// An invitation arrived.
exports.onInvite = onValueCreated(
  on("/users/{uid}/invites/{roomCode}"),
  async (event) => {
    const invite = event.data.val() || {};
    const { uid, roomCode } = event.params;

    await notify(uid, `${shortName(invite.fromDisplayName)} invited you to a game.`, roomCode);
  });

// ---------------------------------------------------------------------------
// The two jobs that used to force the database wide open.
//
// A phone may now touch only its own profile. But inviting somebody means
// finding them by their email address and putting something in THEIR profile,
// and starting a match means adding it to BOTH players' lists. Allowing a
// phone to do either means allowing it to read and write every player's data.
//
// So the server does these two things instead. It has the whole database and
// the account list, and it is the only thing that does.
// ---------------------------------------------------------------------------

// A player's list of games (or rooms), read, changed, written back in one
// go, so two matches starting at once cannot overwrite each other's entry.
// Unity writes these as JSON arrays, so they come back as arrays.
async function editList(uid, list, change) {
  const ref = db().ref(`users/${uid}/${list}`);

  await ref.transaction((current) => {
    const values = Array.isArray(current)
      ? current.filter((v) => typeof v === "string")
      : current && typeof current === "object"
        ? Object.values(current).filter((v) => typeof v === "string")
        : [];

    const next = change(values);

    // undefined aborts the transaction: nothing to change.
    return next === null ? undefined : next;
  });
}

const addTo = (id) => (values) => values.includes(id) ? null : values.concat([id]);
const removeFrom = (id) => (values) =>
  values.includes(id) ? values.filter((v) => v !== id) : null;

// One player has been put in a match: it belongs on their list of games, and
// the room that arranged it no longer belongs on their list of rooms.
async function matchBelongsTo(uid, matchId) {
  // A bot has no list: nobody ever opens the game as one.
  if (!uid || isBot(uid)) return;

  await editList(uid, "activeMatchIds", addTo(matchId));

  const roomCode = await read(`matches/${matchId}/roomCode`);

  if (roomCode)
    await editList(uid, "activeRoomIds", removeFrom(roomCode));

  logger.info(`[LIST] match ${matchId} is on ${uid}'s list.`);
}

exports.onPlayer1Set = onValueWritten(
  on("/matches/{matchId}/player1Uid"),
  async (event) => {
    const uid = event.data.after.val();

    if (uid && uid !== event.data.before.val())
      await matchBelongsTo(uid, event.params.matchId);
  });

exports.onPlayer2Set = onValueWritten(
  on("/matches/{matchId}/player2Uid"),
  async (event) => {
    const uid = event.data.after.val();

    if (uid && uid !== event.data.before.val())
      await matchBelongsTo(uid, event.params.matchId);
  });

// An invitation, asked for by the sender and delivered by us.
//
// The game writes what it wants to inviteRequests (it may only write its own
// name on it, by the rules), and we turn the email address into a player and
// put the invitation in their profile. The answer goes back on the request,
// so the sender can be told "sent to Ada" or "nobody has that address".
exports.onInviteRequest = onValueCreated(
  on("/inviteRequests/{requestId}"),
  async (event) => {
    const request = event.data.val() || {};
    const { requestId } = event.params;
    const answer = db().ref(`inviteRequests/${requestId}/result`);

    const { fromUid, fromDisplayName, roomCode } = request;
    const toEmail = (request.toEmail || "").trim();

    if (!fromUid || !roomCode || (!toEmail && !request.toUid)) {
      await answer.set({ status: "error", message: "Incomplete request." });
      return;
    }

    // A rematch names the other player directly; an invitation by email has
    // to be looked up.
    let toUid = request.toUid || "";

    if (!toUid) {
      try {
        toUid = (await getAuth().getUserByEmail(toEmail)).uid;
      } catch (err) {
        logger.info(`[INVITE] no account for ${toEmail}: ${err.code}`);
        await answer.set({ status: "no-user" });
        return;
      }
    }

    if (toUid === fromUid) {
      await answer.set({ status: "self" });
      return;
    }

    // Their alias if they have one, otherwise the address, so the sender's own
    // list can say who it is waiting for.
    const profileName = await read(`users/${toUid}/displayName`);
    const toName = profileName || shortName(toEmail);

    await db().ref(`users/${toUid}/invites/${roomCode}`).set({
      roomCode,
      fromUid,
      fromDisplayName: fromDisplayName || "",
      createdAtUnix: Date.now(),
    });

    await db().ref(`rooms/${roomCode}`).update({
      invitedUid: toUid,
      invitedDisplayName: toName,
    });

    logger.info(`[INVITE] ${fromUid} -> ${toUid} for room ${roomCode}.`);
    await answer.set({ status: "sent", toUid, toName });
  });

// ---------------------------------------------------------------------------
// The bots' referee.
//
// A quick game nobody joins gets a bot (see BotOpponent.cs in the game). The
// bot's word is worked out on the player's phone, when they play their own -
// the only moment the phone certainly has that match's board - and parked in
// botQueue/{matchId} with the time it is due. Playing it is left to this,
// because by then the phone is in a pocket with the app asleep:
//
//   - seat the planned bot once its time comes and nobody real has sat down;
//   - post each parked word once it is due, which sets off onWordPlayed and
//     so the "your move" notification, exactly as a person's word would;
//   - clear the plan away when the game ends or a person took the seat.
//
// And one safety net: a bot game where the player's word has waited half a
// day for an answer that was never worked out (the app was closed mid-search,
// or the game dates from before this existed) gets a pass from the bot, so
// the player wins the round and the game moves on instead of hanging.
// ---------------------------------------------------------------------------

const GIVE_UP_AFTER_MS = 12 * 60 * 60 * 1000;

exports.botReferee = onSchedule(
  { schedule: "every 1 minutes", region: "europe-west1", timeoutSeconds: 120 },
  async () => {
    const now = Date.now();
    const plans = (await db().ref("botQueue").get()).val() || {};

    for (const [matchId, plan] of Object.entries(plans)) {
      try {
        await refereeOne(matchId, plan || {}, now);
      } catch (err) {
        logger.error(`[BOT] ${matchId}: referee failed`, err);
      }
    }

    try {
      await safetyNet(plans, now);
    } catch (err) {
      logger.error("[BOT] safety net failed", err);
    }
  });

async function refereeOne(matchId, plan, now) {
  const match = await read(`matches/${matchId}`);

  // Gone or over: nothing left to play.
  if (!match || match.status === "completed") {
    await db().ref(`botQueue/${matchId}`).remove();
    return;
  }

  const botUid = plan.botUid;
  if (!isBot(botUid)) return;

  let seat = match.player2Uid || "";

  // Nobody has come. Sit down, if it is time - through a transaction, so a
  // person arriving in the same instant keeps the seat.
  if (!seat) {
    if (!plan.seatAtUnix || now < plan.seatAtUnix) return;

    const claim = await db().ref(`matches/${matchId}/player2Uid`).transaction((current) =>
      current === "" ? botUid : undefined);

    if (!claim.committed) return;

    seat = botUid;
    await db().ref(`matches/${matchId}/player2DisplayName`).set(plan.botName || "");

    // The offer comes down with it, if it is still this match's offer.
    const offer = await read("quickQueue/waiting");
    if (offer && offer.matchId === matchId)
      await db().ref("quickQueue/waiting").remove();

    logger.info(`[BOT] ${plan.botName} (${botUid}) sat down in ${matchId}.`);
  }

  // A person took the seat: the bot is not needed.
  if (seat !== botUid) {
    await db().ref(`botQueue/${matchId}`).remove();
    return;
  }

  const moves = plan.moves || {};

  for (const [round, move] of Object.entries(moves)) {
    if (!move || !move.submission || !move.dueAtUnix || now < move.dueAtUnix) continue;

    const path = `matches/${matchId}/rounds/${round}/submissions/${botUid}`;

    // Written once. If it is somehow there already, the parked copy just goes.
    if (!(await db().ref(path).get()).exists()) {
      await db().ref(path).set({ ...move.submission, uid: botUid });
      logger.info(`[BOT] ${plan.botName} played '${move.submission.word}' in round ${round} of ${matchId}.`);
    }

    await db().ref(`botQueue/${matchId}/moves/${round}`).remove();
  }
}

async function safetyNet(plans, now) {
  const snap = await db().ref("matches")
    .orderByChild("player2Uid").startAt("bot-").endAt("bot-").get();

  const matches = snap.val() || {};

  for (const [matchId, match] of Object.entries(matches)) {
    if (!match || match.status === "completed" || !isBot(match.player2Uid)) continue;

    const round = match.currentRoundNumber || 1;
    const rounds = match.rounds || {};
    const subs = (rounds[round] && rounds[round].submissions) || {};
    const human = subs[match.player1Uid];
    const bot = match.player2Uid;

    // Only a word that is waiting on the bot.
    if (!human || subs[bot]) continue;

    // Parked and due later: the referee has it.
    const plan = plans[matchId];
    if (plan && plan.moves && plan.moves[round]) continue;

    const waited = now - (Number(human.submittedAtUnix) || now);
    if (waited < GIVE_UP_AFTER_MS) continue;

    await db().ref(`matches/${matchId}/rounds/${round}/submissions/${bot}`).set({
      uid: bot,
      word: "",
      score: 0,
      isValid: false,
      simulatedTilesJson: "",
      secondsRemaining: 0,
      submittedAtUnix: now,
    });

    logger.info(`[BOT] ${bot} passed round ${round} of ${matchId}: no word after ${Math.round(waited / 3600000)}h.`);
  }
}
