using System;
using System.Reflection;
using UnityEngine;

public static class CrowdRushFeedbackAudioBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushFeedbackAudio>() != null) return;
        new GameObject("CrowdRushFeedbackAudio").AddComponent<CrowdRushFeedbackAudio>();
    }
}

public sealed class CrowdRushFeedbackAudio : MonoBehaviour
{
    private CrowdRushGame game;
    private AudioSource source;
    private FieldInfo stateField;
    private FieldInfo crowdField;
    private FieldInfo resultVictoryField;

    private object lastState;
    private int lastCrowd = -1;
    private float nextAllowedHaptic;

    private AudioClip positive;
    private AudioClip negative;
    private AudioClip battle;
    private AudioClip victory;
    private AudioClip defeat;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = 0.30f;
        source.spatialBlend = 0f;

        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type t = typeof(CrowdRushGame);
        stateField = t.GetField("state", flags);
        crowdField = t.GetField("crowdCount", flags);
        resultVictoryField = t.GetField("resultWasVictory", flags);

        positive = MakeTone("Positive", 760f, 0.095f, 0.20f, 1.12f);
        negative = MakeTone("Negative", 260f, 0.12f, 0.22f, 0.78f);
        battle = MakeTone("Battle", 180f, 0.16f, 0.24f, 1.45f);
        victory = MakeArpeggio("Victory", new float[] { 520f, 660f, 820f }, 0.26f, 0.20f);
        defeat = MakeArpeggio("Defeat", new float[] { 340f, 280f, 210f }, 0.30f, 0.22f);
    }

    private void Update()
    {
        if (game == null) game = UnityEngine.Object.FindFirstObjectByType<CrowdRushGame>();
        if (game == null) return;

        object state = stateField != null ? stateField.GetValue(game) : null;
        int crowd = crowdField != null ? (int)crowdField.GetValue(game) : 0;

        if (lastCrowd >= 0 && crowd != lastCrowd)
        {
            int delta = crowd - lastCrowd;
            if (Mathf.Abs(delta) >= 2)
            {
                if (delta > 0) source.PlayOneShot(positive);
                else source.PlayOneShot(negative);
                TryHaptic(delta < 0);
            }
        }

        if (state != null && lastState != null && !state.Equals(lastState))
        {
            string n = state.ToString();
            if (n == "Battle")
            {
                source.PlayOneShot(battle);
                TryHaptic(true);
            }
            else if (n == "Result")
            {
                bool won = resultVictoryField != null && (bool)resultVictoryField.GetValue(game);
                source.PlayOneShot(won ? victory : defeat);
                TryHaptic(!won);
            }
        }

        lastCrowd = crowd;
        lastState = state;
    }

    private void TryHaptic(bool strong)
    {
        if (Time.unscaledTime < nextAllowedHaptic) return;
        nextAllowedHaptic = Time.unscaledTime + (strong ? 0.32f : 0.18f);
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }

    private AudioClip MakeTone(string name, float startHz, float duration, float amplitude, float endRatio)
    {
        const int rate = 44100;
        int samples = Mathf.Max(32, Mathf.RoundToInt(rate * duration));
        float[] data = new float[samples];
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            float hz = Mathf.Lerp(startHz, startHz * endRatio, t);
            phase += 2f * Mathf.PI * hz / rate;
            float envelope = Mathf.Sin(Mathf.PI * t);
            data[i] = Mathf.Sin(phase) * amplitude * envelope;
        }
        AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip MakeArpeggio(string name, float[] notes, float duration, float amplitude)
    {
        const int rate = 44100;
        int samples = Mathf.Max(64, Mathf.RoundToInt(rate * duration));
        float[] data = new float[samples];
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            int note = Mathf.Clamp(Mathf.FloorToInt(t * notes.Length), 0, notes.Length - 1);
            float hz = notes[note];
            phase += 2f * Mathf.PI * hz / rate;
            float envelope = Mathf.Sin(Mathf.PI * t);
            data[i] = Mathf.Sin(phase) * amplitude * envelope;
        }
        AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
