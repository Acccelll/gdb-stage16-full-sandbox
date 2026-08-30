using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class CrowdRushPresentationV4Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushPresentationV4>() != null) return;
        new GameObject("CrowdRushPresentationV4").AddComponent<CrowdRushPresentationV4>();
    }
}

public sealed class CrowdRushPresentationV4 : MonoBehaviour
{
    private sealed class GateLabel
    {
        public Transform root;
        public Vector3 world;
        public string text;
        public bool positive;
    }

    private Camera cam;
    private CrowdRushGame game;
    private Transform playerRoot;
    private Transform crowdRoot;
    private readonly List<Transform> eventRoots = new List<Transform>();
    private readonly List<GateLabel> gateLabels = new List<GateLabel>();
    private readonly List<GameObject> roadDecor = new List<GameObject>();

    private FieldInfo stateField;
    private FieldInfo crowdCountField;
    private FieldInfo nearbyChoiceField;
    private FieldInfo battleEnemyStartField;
    private FieldInfo playerZField;

    private object lastState;
    private int lastCrowd = -1;
    private int currentCrowd;
    private int battleEnemyStart;
    private float feedbackUntil;
    private string feedbackText = string.Empty;
    private bool feedbackPositive;
    private float scanAt;
    private float decorCenterZ = float.MinValue;
    private bool finishDecorated;

    private GUIStyle positiveStyle;
    private GUIStyle negativeStyle;
    private GUIStyle neutralStyle;
    private GUIStyle battleStyle;
    private GUIStyle finishStyle;
    private GUIStyle chipStyle;

    private Material dashMaterial;
    private Material railMaterial;
    private Material finishDarkMaterial;

    private void Awake()
    {
        DisableOlderDirectors();
        CacheReflection();
        BuildMaterials();
    }

    private void DisableOlderDirectors()
    {
        CrowdRushVisualDirector v2 = UnityEngine.Object.FindFirstObjectByType<CrowdRushVisualDirector>();
        if (v2 != null) v2.enabled = false;
        CrowdRushVisualDirectorV3 v3 = UnityEngine.Object.FindFirstObjectByType<CrowdRushVisualDirectorV3>();
        if (v3 != null) v3.enabled = false;
    }

    private void CacheReflection()
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type t = typeof(CrowdRushGame);
        stateField = t.GetField("state", flags);
        crowdCountField = t.GetField("crowdCount", flags);
        nearbyChoiceField = t.GetField("nearbyChoice", flags);
        battleEnemyStartField = t.GetField("battleEnemyStart", flags);
        playerZField = t.GetField("playerZ", flags);
    }

    private void BuildMaterials()
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        dashMaterial = new Material(shader) { color = new Color(0.92f, 0.95f, 1f) };
        railMaterial = new Material(shader) { color = new Color(0.19f, 0.80f, 0.95f) };
        finishDarkMaterial = new Material(shader) { color = new Color(0.12f, 0.14f, 0.18f) };
    }

    private void LateUpdate()
    {
        DisableOlderDirectors();
        AcquireRuntime();
        if (game == null) return;

        SuppressLegacyChoiceHud();
        ReadStateAndFeedback();

        if (Time.unscaledTime >= scanAt)
        {
            ScanScene();
            scanAt = Time.unscaledTime + 0.12f;
        }

        if (cam == null || playerRoot == null) return;

        ConfigureCamera();
        AnimatePlayerCrowd();
        AnimateEnemies();
        CullEvents();
        UpdateRoadDecor();
        DecorateFinish();
    }

    private void AcquireRuntime()
    {
        if (game == null) game = UnityEngine.Object.FindFirstObjectByType<CrowdRushGame>();
        if (cam == null) cam = Camera.main;

        GameObject p = GameObject.Find("PlayerRoot");
        if (p != null)
        {
            playerRoot = p.transform;
            crowdRoot = playerRoot.Find("CrowdRoot");
        }
        else
        {
            playerRoot = null;
            crowdRoot = null;
        }
    }

    private void SuppressLegacyChoiceHud()
    {
        if (nearbyChoiceField != null) nearbyChoiceField.SetValue(game, string.Empty);
    }

    private void ReadStateAndFeedback()
    {
        object state = stateField != null ? stateField.GetValue(game) : null;
        currentCrowd = crowdCountField != null ? (int)crowdCountField.GetValue(game) : 0;
        battleEnemyStart = battleEnemyStartField != null ? (int)battleEnemyStartField.GetValue(game) : 0;

        if (lastCrowd >= 0 && currentCrowd != lastCrowd)
        {
            int delta = currentCrowd - lastCrowd;
            if (Mathf.Abs(delta) >= 2)
            {
                feedbackPositive = delta > 0;
                feedbackText = delta > 0 ? "+" + delta : delta.ToString();
                feedbackUntil = Time.unscaledTime + 0.62f;
            }
        }

        if (state != null && lastState != null && !state.Equals(lastState))
        {
            string name = state.ToString();
            if (name == "Battle")
            {
                feedbackText = "BATALHA";
                feedbackPositive = false;
                feedbackUntil = Time.unscaledTime + 0.7f;
            }
        }

        lastCrowd = currentCrowd;
        lastState = state;
    }

    private void ScanScene()
    {
        eventRoots.Clear();
        gateLabels.Clear();
        TextMesh[] texts = Resources.FindObjectsOfTypeAll<TextMesh>();
        for (int i = 0; i < texts.Length; i++)
        {
            TextMesh tm = texts[i];
            if (tm == null || !tm.gameObject.scene.IsValid()) continue;
            if (tm.name != "Label" || tm.transform.parent == null) continue;
            Transform root = tm.transform.parent;
            if (!root.name.StartsWith("GatePair_", StringComparison.Ordinal)) continue;

            gateLabels.Add(new GateLabel
            {
                root = root,
                world = tm.transform.position,
                text = tm.text,
                positive = tm.text.StartsWith("+", StringComparison.Ordinal) || tm.text.StartsWith("x", StringComparison.OrdinalIgnoreCase)
            });

            Renderer rr = tm.GetComponent<Renderer>();
            if (rr != null) rr.enabled = false;
            tm.characterSize = 0.001f;
        }

        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !t.gameObject.scene.IsValid() || t.parent != null) continue;
            string n = t.name;
            if (n.StartsWith("GatePair_", StringComparison.Ordinal) || n.StartsWith("EnemyCrowd_", StringComparison.Ordinal) || n == "Finish")
                eventRoots.Add(t);
        }
        eventRoots.Sort((a, b) => EventZ(a).CompareTo(EventZ(b)));
    }

    private void ConfigureCamera()
    {
        cam.fieldOfView = 44f;
        cam.nearClipPlane = 0.25f;
        cam.farClipPlane = 78f;
        cam.backgroundColor = new Color(0.72f, 0.86f, 0.97f);

        Vector3 p = playerRoot.position;
        Vector3 desired = new Vector3(p.x * 0.035f, 12.4f, p.z - 17.2f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, desired, 12f * Time.unscaledDeltaTime);
        cam.transform.rotation = Quaternion.Euler(35.5f, 0f, 0f);
    }

    private void AnimatePlayerCrowd()
    {
        if (crowdRoot == null) return;
        int active = CountNamedActive(crowdRoot, "Runner");
        if (active <= 0) return;

        int index = 0;
        float time = Time.time;
        for (int i = 0; i < crowdRoot.childCount; i++)
        {
            Transform c = crowdRoot.GetChild(i);
            if (!c.gameObject.activeSelf || c.name != "Runner") continue;
            Vector3 slot = Slot(index, active, 0.285f, 12);
            float bob = Mathf.Sin(time * 10f + index * 0.72f) * 0.025f;
            c.localPosition = new Vector3(slot.x, 0.54f + bob, slot.z);
            c.localScale = new Vector3(0.235f, 0.35f + Mathf.Abs(bob) * 0.25f, 0.235f);
            index++;
        }
    }

    private void AnimateEnemies()
    {
        float time = Time.time;
        for (int r = 0; r < eventRoots.Count; r++)
        {
            Transform root = eventRoots[r];
            if (root == null || !root.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal)) continue;
            int active = CountNamedActive(root, "Enemy");
            if (active <= 0) continue;
            int index = 0;
            float z = EventZ(root);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (!c.gameObject.activeSelf || c.name != "Enemy") continue;
                Vector3 slot = Slot(index, active, 0.30f, 11);
                float bob = Mathf.Sin(time * 8.5f + index * 0.57f) * 0.02f;
                c.position = new Vector3(slot.x, 0.55f + bob, z + slot.z);
                c.localScale = new Vector3(0.235f, 0.35f, 0.235f);
                index++;
            }
        }
    }

    private int CountNamedActive(Transform root, string name)
    {
        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.gameObject.activeSelf && c.name == name) count++;
        }
        return count;
    }

    private Vector3 Slot(int index, int count, float spacing, int maxColumns)
    {
        int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))), 1, maxColumns);
        int row = index / columns;
        int col = index % columns;
        int rowCount = Mathf.Min(columns, count - row * columns);
        float width = (rowCount - 1) * spacing;
        return new Vector3(col * spacing - width * 0.5f, 0f, -row * spacing);
    }

    private void CullEvents()
    {
        if (eventRoots.Count == 0) return;
        float pz = playerRoot.position.z;
        Transform first = null;
        Transform second = null;
        float d1 = float.MaxValue;
        float d2 = float.MaxValue;

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform r = eventRoots[i];
            if (r == null) continue;
            float d = EventZ(r) - pz;
            if (d < -2.2f) continue;
            if (d < d1)
            {
                second = first; d2 = d1;
                first = r; d1 = d;
            }
            else if (d < d2)
            {
                second = r; d2 = d;
            }
        }

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform r = eventRoots[i];
            if (r == null) continue;
            float d = EventZ(r) - pz;
            bool battle = r.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal) && d > -3.2f && d < 4f;
            bool show = r == first && d < 28f;
            // A second event can be hinted only at long range; this gives depth without overlap.
            show |= r == second && d > 18f && d < 33f;
            show |= battle;
            if (r.gameObject.activeSelf != show) r.gameObject.SetActive(show);
        }
    }

    private void UpdateRoadDecor()
    {
        float pz = playerRoot.position.z;
        float snapped = Mathf.Floor(pz / 4f) * 4f;
        if (Mathf.Abs(snapped - decorCenterZ) < 0.1f && roadDecor.Count > 0) return;
        decorCenterZ = snapped;

        EnsureRoadDecorPool(36);
        int idx = 0;
        for (int i = -5; i <= 12; i++)
        {
            float z = snapped + i * 4f;
            PositionDecor(roadDecor[idx++], new Vector3(0f, 0.015f, z), new Vector3(0.12f, 0.025f, 1.55f), dashMaterial);
            PositionDecor(roadDecor[idx++], new Vector3(-4.72f, 0.03f, z), new Vector3(0.07f, 0.055f, 3.7f), railMaterial);
        }
    }

    private void EnsureRoadDecorPool(int amount)
    {
        while (roadDecor.Count < amount)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PresentationRoadDecor";
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            roadDecor.Add(go);
        }
    }

    private void PositionDecor(GameObject go, Vector3 pos, Vector3 scale, Material material)
    {
        go.transform.position = pos;
        go.transform.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        go.SetActive(true);
    }

    private void DecorateFinish()
    {
        if (finishDecorated) return;
        Transform finish = null;
        for (int i = 0; i < eventRoots.Count; i++) if (eventRoots[i] != null && eventRoots[i].name == "Finish") { finish = eventRoots[i]; break; }
        if (finish == null) return;

        float z = EventZ(finish);
        for (int i = 0; i < 10; i++)
        {
            float x = -3.45f + i * 0.77f;
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "FinishChecker";
            tile.transform.SetParent(finish, true);
            tile.transform.position = new Vector3(x, 3.55f, z - 0.03f);
            tile.transform.localScale = new Vector3(0.37f, 0.18f, 0.08f);
            Renderer r = tile.GetComponent<Renderer>();
            r.sharedMaterial = i % 2 == 0 ? finishDarkMaterial : dashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Collider c = tile.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }
        finishDecorated = true;
    }

    private float EventZ(Transform root)
    {
        if (root == null) return float.MaxValue;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == "PostL" || c.name == "PostR" || c.name == "Header" || c.name == "Enemy" || c.name.StartsWith("Finish", StringComparison.Ordinal)) return c.position.z;
        }
        string n = root.name;
        int u = n.LastIndexOf('_');
        if (u >= 0)
        {
            float parsed;
            if (float.TryParse(n.Substring(u + 1), out parsed)) return parsed;
        }
        return root.position.z;
    }

    private void InitStyles()
    {
        if (positiveStyle != null) return;
        int gateFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.055f), 22, 56);
        int bigFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.072f), 28, 74);
        int chipFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.036f), 16, 40);

        positiveStyle = MakeStyle(gateFont, new Color(0.22f, 1f, 0.42f));
        negativeStyle = MakeStyle(gateFont, new Color(1f, 0.31f, 0.35f));
        neutralStyle = MakeStyle(gateFont, Color.white);
        battleStyle = MakeStyle(bigFont, new Color(1f, 0.78f, 0.18f));
        finishStyle = MakeStyle(gateFont, new Color(1f, 0.84f, 0.22f));
        chipStyle = MakeStyle(chipFont, Color.white);
    }

    private GUIStyle MakeStyle(int size, Color color)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = color }
        };
    }

    private void OnGUI()
    {
        if (cam == null || playerRoot == null || game == null) return;
        InitStyles();

        Transform nearest = GetNearestEvent();
        if (nearest != null)
        {
            if (nearest.name.StartsWith("GatePair_", StringComparison.Ordinal)) DrawGateLabels(nearest);
            else if (nearest.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal)) DrawEnemyInfo(nearest);
            else if (nearest.name == "Finish") DrawFinishInfo(nearest);
        }

        DrawFeedback();
        DrawBattleChip();
    }

    private Transform GetNearestEvent()
    {
        float pz = playerRoot.position.z;
        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform r = eventRoots[i];
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            float d = EventZ(r) - pz;
            if (d > 1.0f && d < best && d < 28f) { best = d; nearest = r; }
        }
        return nearest;
    }

    private void DrawGateLabels(Transform root)
    {
        for (int i = 0; i < gateLabels.Count; i++)
        {
            GateLabel g = gateLabels[i];
            if (g.root != root) continue;
            DrawProjected(g.world, g.text, g.positive ? positiveStyle : negativeStyle, 0.175f, 0.045f);
        }
    }

    private void DrawEnemyInfo(Transform root)
    {
        int count = CountNamedActive(root, "Enemy");
        DrawProjected(new Vector3(0f, 1.75f, EventZ(root)), "VS  " + count, neutralStyle, 0.21f, 0.044f);
    }

    private void DrawFinishInfo(Transform root)
    {
        DrawProjected(new Vector3(0f, 3.35f, EventZ(root)), "CHEGADA", finishStyle, 0.29f, 0.044f);
    }

    private void DrawProjected(Vector3 world, string text, GUIStyle style, float widthRatio, float heightRatio)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f) return;
        float width = Screen.width * widthRatio;
        float height = Mathf.Clamp(Screen.height * heightRatio, 34f, 76f);
        float x = Mathf.Clamp(screen.x - width * 0.5f, Screen.width * 0.025f, Screen.width - width - Screen.width * 0.025f);
        float y = Mathf.Clamp(Screen.height - screen.y - height * 0.5f, Screen.height * 0.19f, Screen.height * 0.64f);
        GUI.Box(new Rect(x, y, width, height), string.Empty);
        GUI.Label(new Rect(x, y, width, height), text, style);
    }

    private void DrawFeedback()
    {
        if (Time.unscaledTime > feedbackUntil || string.IsNullOrEmpty(feedbackText)) return;
        float remaining = Mathf.Clamp01((feedbackUntil - Time.unscaledTime) / 0.62f);
        float scale = 1f + (1f - remaining) * 0.22f;
        float width = Screen.width * 0.34f * scale;
        float height = Screen.height * 0.07f * scale;
        Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.37f, width, height);
        GUI.Label(rect, feedbackText, feedbackText == "BATALHA" ? battleStyle : (feedbackPositive ? positiveStyle : negativeStyle));
    }

    private void DrawBattleChip()
    {
        object state = stateField != null ? stateField.GetValue(game) : null;
        if (state == null || state.ToString() != "Battle") return;
        string text = currentCrowd + "   VS   " + Mathf.Max(0, battleEnemyStart);
        Rect rect = new Rect(Screen.width * 0.25f, Screen.height * 0.215f, Screen.width * 0.50f, Screen.height * 0.045f);
        GUI.Box(rect, string.Empty);
        GUI.Label(rect, text, chipStyle);
    }
}
