using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class CrowdRushVisualDirectorV3Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushVisualDirectorV3>() != null) return;
        new GameObject("CrowdRushVisualDirectorV3").AddComponent<CrowdRushVisualDirectorV3>();
    }
}

public sealed class CrowdRushVisualDirectorV3 : MonoBehaviour
{
    private sealed class GateLabelInfo
    {
        public string text;
        public Vector3 worldPosition;
        public Transform root;
        public bool positive;
    }

    private Camera cam;
    private CrowdRushGame game;
    private Transform playerRoot;
    private Transform crowdRoot;
    private float nextScan;

    private readonly List<Transform> eventRoots = new List<Transform>();
    private readonly List<GateLabelInfo> gateLabels = new List<GateLabelInfo>();

    private FieldInfo nearbyChoiceField;

    private GUIStyle gatePositiveStyle;
    private GUIStyle gateNegativeStyle;
    private GUIStyle enemyStyle;
    private GUIStyle finishStyle;

    private void Awake()
    {
        Application.targetFrameRate = 60;

        CrowdRushVisualDirector old = UnityEngine.Object.FindFirstObjectByType<CrowdRushVisualDirector>();
        if (old != null) old.enabled = false;

        nearbyChoiceField = typeof(CrowdRushGame).GetField("nearbyChoice", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void LateUpdate()
    {
        if (game == null) game = UnityEngine.Object.FindFirstObjectByType<CrowdRushGame>();
        if (cam == null) cam = Camera.main;
        if (playerRoot == null)
        {
            GameObject player = GameObject.Find("PlayerRoot");
            if (player != null)
            {
                playerRoot = player.transform;
                crowdRoot = playerRoot.Find("CrowdRoot");
            }
        }

        if (Time.unscaledTime >= nextScan)
        {
            ScanRuntime();
            nextScan = Time.unscaledTime + 0.15f;
        }

        SuppressLegacyChoiceHud();
        SuppressAllWorldText();

        if (cam == null || playerRoot == null) return;

        OverrideCamera();
        CompactPlayerCrowd();
        CompactEnemies();
        CullTrackContent();
    }

    private void SuppressLegacyChoiceHud()
    {
        if (game == null || nearbyChoiceField == null) return;
        nearbyChoiceField.SetValue(game, string.Empty);
    }

    private void ScanRuntime()
    {
        eventRoots.Clear();
        gateLabels.Clear();

        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !t.gameObject.scene.IsValid()) continue;

            string n = t.name;
            bool eventRoot = t.parent == null &&
                (n.StartsWith("GatePair_", StringComparison.Ordinal) ||
                 n.StartsWith("EnemyCrowd_", StringComparison.Ordinal) ||
                 n == "Finish");

            if (eventRoot) eventRoots.Add(t);

            TextMesh tm = t.GetComponent<TextMesh>();
            if (tm != null && n == "Label" && t.parent != null &&
                t.parent.name.StartsWith("GatePair_", StringComparison.Ordinal))
            {
                gateLabels.Add(new GateLabelInfo
                {
                    text = tm.text,
                    worldPosition = t.position,
                    root = t.parent,
                    positive = tm.text.StartsWith("+", StringComparison.Ordinal) ||
                               tm.text.StartsWith("x", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        eventRoots.Sort((a, b) => EventZ(a).CompareTo(EventZ(b)));
    }

    private void SuppressAllWorldText()
    {
        TextMesh[] allText = Resources.FindObjectsOfTypeAll<TextMesh>();
        for (int i = 0; i < allText.Length; i++)
        {
            TextMesh tm = allText[i];
            if (tm == null || !tm.gameObject.scene.IsValid()) continue;
            if (tm.name != "Label") continue;

            Renderer r = tm.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            tm.characterSize = 0.001f;
            tm.fontSize = 1;
        }
    }

    private void OverrideCamera()
    {
        cam.fieldOfView = 43f;
        cam.nearClipPlane = 0.35f;
        cam.farClipPlane = 72f;
        cam.backgroundColor = new Color(0.69f, 0.84f, 0.96f);

        Vector3 p = playerRoot.position;
        Vector3 desired = new Vector3(p.x * 0.04f, 11.8f, p.z - 16.2f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, desired, 14f * Time.unscaledDeltaTime);
        cam.transform.rotation = Quaternion.Euler(34f, 0f, 0f);
    }

    private void CompactPlayerCrowd()
    {
        if (crowdRoot == null) return;

        int active = 0;
        for (int i = 0; i < crowdRoot.childCount; i++)
        {
            Transform c = crowdRoot.GetChild(i);
            if (c.gameObject.activeSelf && c.name == "Runner") active++;
        }

        if (active == 0) return;

        int index = 0;
        for (int i = 0; i < crowdRoot.childCount; i++)
        {
            Transform c = crowdRoot.GetChild(i);
            if (!c.gameObject.activeSelf || c.name != "Runner") continue;

            c.localPosition = Slot(index, active, 0.29f, 12);
            c.localScale = new Vector3(0.24f, 0.36f, 0.24f);
            index++;
        }
    }

    private void CompactEnemies()
    {
        for (int r = 0; r < eventRoots.Count; r++)
        {
            Transform root = eventRoots[r];
            if (root == null || !root.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal)) continue;

            int active = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.gameObject.activeSelf && c.name == "Enemy") active++;
            }

            if (active == 0) continue;
            int index = 0;
            float z = EventZ(root);

            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (!c.gameObject.activeSelf || c.name != "Enemy") continue;

                Vector3 slot = Slot(index, active, 0.31f, 11);
                c.position = new Vector3(slot.x, 0.55f, z + slot.z);
                c.localScale = new Vector3(0.24f, 0.36f, 0.24f);
                index++;
            }
        }
    }

    private void CullTrackContent()
    {
        if (eventRoots.Count == 0) return;

        float pz = playerRoot.position.z;
        Transform nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];
            if (root == null) continue;
            float d = EventZ(root) - pz;
            if (d >= -1.4f && d < nearestDistance)
            {
                nearest = root;
                nearestDistance = d;
            }
        }

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];
            if (root == null) continue;
            float d = EventZ(root) - pz;

            bool currentBattle = root.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal) && d > -3f && d < 3.5f;
            bool show = root == nearest && d > -1.4f && d < 25f;
            show |= currentBattle;

            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        }
    }

    private Vector3 Slot(int index, int count, float spacing, int maxColumns)
    {
        int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))), 1, maxColumns);
        int row = index / columns;
        int col = index % columns;
        int rowCount = Mathf.Min(columns, count - row * columns);
        float width = (rowCount - 1) * spacing;

        return new Vector3(col * spacing - width * 0.5f, 0.55f, -row * spacing);
    }

    private float EventZ(Transform root)
    {
        if (root == null) return float.MaxValue;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == "PostL" || c.name == "PostR" || c.name == "Header" || c.name == "Enemy" ||
                c.name.StartsWith("Finish", StringComparison.Ordinal)) return c.position.z;
        }

        string n = root.name;
        int underscore = n.LastIndexOf('_');
        if (underscore >= 0)
        {
            float parsed;
            if (float.TryParse(n.Substring(underscore + 1), out parsed)) return parsed;
        }
        return root.position.z;
    }

    private void InitStyles()
    {
        if (gatePositiveStyle != null) return;

        int gateFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.052f), 22, 54);
        int infoFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.037f), 18, 42);

        gatePositiveStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = gateFont,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.20f, 1f, 0.40f) }
        };
        gateNegativeStyle = new GUIStyle(gatePositiveStyle);
        gateNegativeStyle.normal.textColor = new Color(1f, 0.30f, 0.34f);

        enemyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = infoFont,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        finishStyle = new GUIStyle(enemyStyle);
        finishStyle.fontSize = gateFont;
    }

    private void OnGUI()
    {
        if (cam == null || playerRoot == null) return;
        InitStyles();

        Transform nearest = GetNearestVisibleEvent();
        if (nearest == null) return;

        if (nearest.name.StartsWith("GatePair_", StringComparison.Ordinal)) DrawGatePair(nearest);
        else if (nearest.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal)) DrawEnemy(nearest);
        else if (nearest.name == "Finish") DrawFinish(nearest);
    }

    private Transform GetNearestVisibleEvent()
    {
        float pz = playerRoot.position.z;
        Transform nearest = null;
        float distance = float.MaxValue;

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];
            if (root == null || !root.gameObject.activeInHierarchy) continue;
            float d = EventZ(root) - pz;
            if (d > 1.2f && d < distance && d < 25f)
            {
                nearest = root;
                distance = d;
            }
        }
        return nearest;
    }

    private void DrawGatePair(Transform root)
    {
        for (int i = 0; i < gateLabels.Count; i++)
        {
            GateLabelInfo info = gateLabels[i];
            if (info.root != root) continue;
            DrawProjected(info.worldPosition, info.text, info.positive ? gatePositiveStyle : gateNegativeStyle, 0.18f);
        }
    }

    private void DrawEnemy(Transform root)
    {
        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == "Enemy" && c.gameObject.activeSelf) count++;
        }
        DrawProjected(new Vector3(0f, 1.65f, EventZ(root)), count.ToString(), enemyStyle, 0.16f);
    }

    private void DrawFinish(Transform root)
    {
        DrawProjected(new Vector3(0f, 3.35f, EventZ(root)), "CHEGADA", finishStyle, 0.28f);
    }

    private void DrawProjected(Vector3 world, string text, GUIStyle style, float widthRatio)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f) return;

        float margin = Screen.width * 0.03f;
        if (screen.x < margin || screen.x > Screen.width - margin || screen.y < 0f || screen.y > Screen.height) return;

        float width = Screen.width * widthRatio;
        float height = Mathf.Clamp(Screen.height * 0.038f, 34f, 70f);
        float x = Mathf.Clamp(screen.x - width * 0.5f, margin, Screen.width - width - margin);
        float y = Mathf.Clamp(Screen.height - screen.y - height * 0.5f, Screen.height * 0.20f, Screen.height * 0.67f);

        GUI.Box(new Rect(x, y, width, height), string.Empty);
        GUI.Label(new Rect(x, y, width, height), text, style);
    }
}
