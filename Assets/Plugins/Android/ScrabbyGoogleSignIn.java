package com.kingz.scrabby;

import android.app.Activity;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.util.Log;

import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executor;
import java.util.concurrent.Executors;

/**
 * Sign in with the Google account already on the phone.
 *
 * Android's Credential Manager does the work: it shows the account sheet,
 * talks to Google, and hands back an ID token. That token is all Firebase
 * needs to sign the player in - the game never sees a password, and the
 * player never types one.
 *
 * Called from C# (GoogleSignInBridge.cs). Everything here runs on Android's
 * side of the fence and answers on Unity's main thread via UnitySendMessage,
 * because Unity objects may only be touched there.
 *
 * The older GoogleSignInClient this replaces was deprecated in 2025; the
 * androidx.credentials API is what Google supports now, and it is also what
 * offers passkeys and saved passwords later, should they ever be wanted.
 */
public final class ScrabbyGoogleSignIn {

    private static final String TAG = "ScrabbyGoogleSignIn";

    // The GameObject that hears the answer, and the two methods on it.
    private static final String LISTENER = "GoogleSignInBridge";
    private static final String ON_TOKEN = "OnGoogleToken";
    private static final String ON_ERROR = "OnGoogleError";

    private ScrabbyGoogleSignIn() {
    }

    /**
     * @param activity       Unity's activity, for the account sheet to sit on.
     * @param serverClientId the project's WEB client id from Firebase, not the
     *                       Android one. Google checks the token was minted for
     *                       this project; the Android id is the wrong one and
     *                       fails in a way that reads as "no accounts found".
     */
    public static void signIn(final Activity activity, final String serverClientId) {
        ask(activity, serverClientId, true);
    }

    /**
     * @param sheetFirst true asks with the "sign in with Google" sheet, which
     *                   offers every account on the phone. Some accounts come
     *                   back from that with "[16] Account reauth failed" -
     *                   Play services declining to mint a token and reporting
     *                   it as a cancellation - and the same account often
     *                   works through the plain ID request, so that is tried
     *                   second rather than giving up.
     */
    private static void ask(final Activity activity, final String serverClientId,
                            final boolean sheetFirst) {
        try {
            GetCredentialRequest.Builder request = new GetCredentialRequest.Builder();

            if (sheetFirst) {
                request.addCredentialOption(
                    new GetSignInWithGoogleOption.Builder(serverClientId).build());
            } else {
                request.addCredentialOption(
                    new GetGoogleIdOption.Builder()
                        .setServerClientId(serverClientId)
                        .setFilterByAuthorizedAccounts(false)
                        .setAutoSelectEnabled(false)
                        .build());
            }

            CredentialManager manager = CredentialManager.create(activity);
            Executor executor = Executors.newSingleThreadExecutor();

            manager.getCredentialAsync(
                activity,
                request.build(),
                new CancellationSignal(),
                executor,
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {

                    @Override
                    public void onResult(GetCredentialResponse response) {
                        handle(response);
                    }

                    @Override
                    public void onError(GetCredentialException error) {
                        String message = error.getType() + ": " + error.getMessage();
                        Log.i(TAG, "sign-in did not finish - " + message);

                        // Worth one second try the other way round, unless the
                        // player themselves backed out of the sheet.
                        boolean reauth = message != null && message.contains("reauth");

                        if (sheetFirst && reauth) {
                            Log.i(TAG, "trying again without the account sheet");
                            ask(activity, serverClientId, false);
                            return;
                        }

                        send(ON_ERROR, message);
                    }
                });
        } catch (Throwable t) {
            Log.e(TAG, "sign-in could not start", t);
            send(ON_ERROR, "could not start: " + t.getMessage());
        }
    }

    private static void handle(GetCredentialResponse response) {
        try {
            Bundle data = response.getCredential().getData();
            String type = response.getCredential().getType();

            if (!GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(type)) {
                send(ON_ERROR, "unexpected credential type: " + type);
                return;
            }

            GoogleIdTokenCredential credential =
                GoogleIdTokenCredential.createFrom(data);

            String token = credential.getIdToken();

            if (token == null || token.isEmpty()) {
                send(ON_ERROR, "no token in the credential");
                return;
            }

            send(ON_TOKEN, token);
        } catch (Throwable t) {
            Log.e(TAG, "could not read the credential", t);
            send(ON_ERROR, "could not read the credential: " + t.getMessage());
        }
    }

    private static void send(String method, String argument) {
        try {
            com.unity3d.player.UnityPlayer.UnitySendMessage(LISTENER, method, argument);
        } catch (Throwable t) {
            Log.e(TAG, "could not reach Unity", t);
        }
    }
}
