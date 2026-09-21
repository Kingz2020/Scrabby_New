# Signing Scrabby for Google Play

Google Play only accepts an Android App Bundle (`.aab`) signed with your
**upload key**. This is a one-time setup, and it is yours to do: the key and its
passwords should never pass through anyone else's hands, and never go into this
repository.

With Play App Signing — the default for every new app — Google holds the key
that actually signs what players download. Yours only proves an upload came from
you. So losing it is recoverable: Google support can register a new upload key.
It is still a slow, form-filling process, so back it up properly.

## 1. Make the key

Run this in a terminal. It asks for a password, then for your name and
organisation (these are stored inside the certificate; "Kingz Co." is fine),
then for the key password — press Enter to use the same one.

```bat
mkdir "%USERPROFILE%\.scrabby"

"C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" -genkeypair -v -keystore "%USERPROFILE%\.scrabby\scrabby-upload.jks" -alias scrabby-upload -keyalg RSA -keysize 2048 -validity 10000
```

`%USERPROFILE%\.scrabby` is deliberately **outside** OneDrive and outside the
project, so it is never synced or committed by accident.

## 2. Tell the build where it is

Create `%USERPROFILE%\.scrabby\signing.json` in a text editor:

```json
{
  "keystorePath": "C:/Users/mysta/.scrabby/scrabby-upload.jks",
  "keystorePass": "YOUR KEYSTORE PASSWORD",
  "keyAlias": "scrabby-upload",
  "keyPass": "YOUR KEY PASSWORD"
}
```

Forward slashes in the path. If you pressed Enter for the key password in step
1, both passwords are the same.

## 3. Back it up

Both of these, somewhere that is not this laptop:

- the file `scrabby-upload.jks`
- the passwords (a password manager is the right place)

## 4. Build the bundle

From Unity: **Scrabby → Build → Android App Bundle (for Google Play)**.

Or with Unity closed:

```bat
"C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe" -quit -batchmode -nographics -projectPath "C:\Users\mysta\OneDrive\Dokumente\GitHub\Scrabby_New" -executeMethod BuildApk.BuildBundleFromCommandLine -logFile build.log
```

The bundle lands in `Builds/` named with its version code, e.g.
`Scrabby_1.0_code380842_20260921_1030.aab`.

## What the build does for you

- **Version code** is set from the clock (minutes since 2026), so every upload is
  higher than the last without anyone counting. The *version name* — the "1.0"
  players see — is yours to change in Player Settings when you want it to move.
- **Target SDK** is pinned to 36, which Play requires for new apps from August
  2026.
- **Nothing secret is left behind.** The keystore settings are cleared from the
  project as soon as the build finishes, so they are never written into
  `ProjectSettings` or committed.

Test builds by cable (**Scrabby → Build → Android APK**) are still debug-signed
and need none of this. One consequence: a copy installed from Play and one
installed by cable are signed differently, so switching between them on the
same phone means uninstalling first.
