using System.Collections.Generic;
using UnityEngine;

// Everything the game says out loud.
//
// One call from anywhere - Sound.Play(Sound.TileDown) - and no wiring in the
// scene, which matters here: the scene file can only be edited with Unity
// closed, and a sound belongs next to the moment it marks, not in an
// inspector field three panels away.
//
// Clips live in Assets/Resources/Audio and are named for what they are, so
// adding one is dropping in a WAV and naming it here.
public static class Sound
{
    public const string TileDown = "tile_place";
    public const string WordAccepted = "word_accepted";
    public const string WordRejected = "word_rejected";
    public const string RoundWon = "round_won";
    public const string RoundLost = "round_lost";
    public const string AllSix = "all_six_bonus";

    private const string MutedKey = "Scrabby.Sound.Muted";
    private const string Folder = "Audio/";

    // Enough that two tiles going down close together do not cut each other
    // off, few enough that nothing is kept alive for no reason.
    private const int Voices = 6;

    // How loud each one sits against the rest. A sound heard on every tile
    // cannot be as loud as one heard twice a game, or it is the only thing
    // anybody hears. These are the mix, and they are meant to be argued with.
    private static readonly Dictionary<string, float> Levels =
        new Dictionary<string, float>
    {
        { TileDown, 0.50f },
        { WordAccepted, 0.70f },
        { WordRejected, 0.80f },
        { RoundWon, 0.90f },
        { RoundLost, 0.85f },
        { AllSix, 1.00f }
    };

    private static readonly Dictionary<string, AudioClip> loaded =
        new Dictionary<string, AudioClip>();

    // Said once per missing clip: a silent game is confusing enough without
    // the console repeating itself sixty times a round.
    private static readonly HashSet<string> complained = new HashSet<string>();

    private static AudioSource[] voices;
    private static int nextVoice;

    private static bool mutedLoaded;
    private static bool muted;

    public static bool Muted
    {
        get
        {
            if (!mutedLoaded)
            {
                muted = PlayerPrefs.GetInt(MutedKey, 0) != 0;
                mutedLoaded = true;
            }

            return muted;
        }

        set
        {
            muted = value;
            mutedLoaded = true;
            PlayerPrefs.SetInt(MutedKey, value ? 1 : 0);
            PlayerPrefs.Save();

            // Whatever is ringing now stops with it, rather than finishing
            // after the player has asked for quiet.
            if (value && voices != null)
            {
                foreach (AudioSource source in voices)
                {
                    if (source != null)
                        source.Stop();
                }
            }
        }
    }

    public static void Play(string name)
    {
        if (Muted || !Application.isPlaying || string.IsNullOrEmpty(name))
            return;

        AudioClip clip = Clip(name);

        if (clip == null)
            return;

        AudioSource source = NextVoice();

        if (source == null)
            return;

        float level;

        if (!Levels.TryGetValue(name, out level))
            level = 0.8f;

        source.PlayOneShot(clip, level);
    }

    private static AudioClip Clip(string name)
    {
        AudioClip clip;

        if (loaded.TryGetValue(name, out clip))
            return clip;

        clip = Resources.Load<AudioClip>(Folder + name);

        if (clip == null && complained.Add(name))
        {
            Debug.LogWarning("[SOUND] No clip at Resources/" + Folder + name +
                             " - the game will be quiet where that one goes.");
        }

        loaded[name] = clip;

        return clip;
    }

    private static AudioSource NextVoice()
    {
        if (voices == null)
        {
            GameObject host = new GameObject("Sound");
            host.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(host);

            voices = new AudioSource[Voices];

            for (int i = 0; i < Voices; i++)
            {
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;

                // Flat, and not placed anywhere: these are interface sounds,
                // not things happening at a point in a world.
                source.spatialBlend = 0f;
                voices[i] = source;
            }
        }

        // The oldest voice is the one to reuse, and round-robin is a good
        // enough guess at which that is.
        AudioSource chosen = voices[nextVoice];
        nextVoice = (nextVoice + 1) % Voices;

        return chosen;
    }
}
