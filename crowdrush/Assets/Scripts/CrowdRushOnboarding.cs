using System;
using System.Reflection;
using UnityEngine;

public static class CrowdRushOnboardingBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushOnboarding>() != null) return;
        new GameObject("CrowdRushOnboarding").AddComponent<CrowdRushOnboarding>();
    }
}

public sealed class CrowdRushOnboarding : MonoBehaviour
{
    private CrowdRushGame game;
    private FieldInfo stateField;
    private FieldInfo levelField;
    private object previousState;
    private float playingSince = -1f;
    private int currentLevel = 1;
    private GUIStyle levelStyle;
    private GUIStyle hintStyle;

    private void Awake()
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type t = typeof(CrowdRushGame);
        stateField = t.GetField("state", flags);
        levelField = t.GetField("level", flags);
    }

    private void Update()
    {
        if (game == null) game = UnityEngine.Object.FindFirstObjectByType<CrowdRushGame>();
        if (game == null || stateField == null) return;

        object state = stateField.GetValue(game);
        if (state != null && (previousState == null || !state.Equals(previousState)))
        {
            if (state.ToString() == "Playing")
            {
                playingSince = Time.unscaledTime;
                currentLevel = ReadLevelNumber();
            }
            previousState = state;
        }
    }

    private int ReadLevelNumber()
    {
        if (levelField == null) return 1;
        object level = levelField.GetValue(game);
        if (level == null) return 1;
        FieldInfo number = level.GetType().GetField("number", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return number != null ? Mathf.Max(1, (int)number.GetValue(level)) : 1;
    }

    private void InitStyles()
    {
        if (levelStyle != null) return;
        levelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.075f), 30, 76),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.042f), 18, 44),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private void OnGUI()
    {
        if (game == null || stateField == null || playingSince < 0f) return;
        object state = stateField.GetValue(game);
        if (state == null || (state.ToString() != "Playing" && state.ToString() != "Battle")) return;

        float elapsed = Time.unscaledTime - playingSince;
        if (elapsed > (currentLevel == 1 ? 4.2f : 1.5f)) return;
        InitStyles();

        if (elapsed < 1.35f)
        {
            float w = Screen.width * 0.54f;
            float h = Screen.height * 0.075f;
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.285f, w, h);
            GUI.Box(r, string.Empty);
            GUI.Label(r, "FASE " + currentLevel, levelStyle);
        }

        if (currentLevel == 1 && elapsed > 1.0f && elapsed < 4.2f)
        {
            float w = Screen.width * 0.72f;
            float h = Screen.height * 0.055f;
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.76f, w, h);
            GUI.Box(r, string.Empty);
            GUI.Label(r, "‹  ARRASTE PARA OS LADOS  ›", hintStyle);
        }
    }
}
