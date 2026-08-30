using System;
using System.Collections.Generic;
using UnityEngine;

public static class CrowdRushVisualPatchBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushVisualDirector>() != null) return;
        new GameObject("CrowdRushVisualDirector").AddComponent<CrowdRushVisualDirector>();
    }
}

public sealed class CrowdRushVisualDirector : MonoBehaviour
{
    private sealed class WorldLabel
    {
        public TextMesh textMesh;
        public Transform root;
        public bool positive;
    }

    private Camera cam;
    private Transform playerRoot;
    private Transform crowdRoot;
    private readonly List<WorldLabel> gateLabels = new List<WorldLabel>();
    private readonly List<Transform> eventRoots = new List<Transform>();
    private readonly List<Transform> enemyRoots = new List<Transform>();

    private float nextRescan;
    private GUIStyle positiveStyle;
    private GUIStyle negativeStyle;
    private GUIStyle enemyStyle;
    private GUIStyle finishStyle;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        nextRescan = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextRescan)
        {
            RescanScene();
            nextRescan = Time.unscaledTime + 0.35f;
        }

        if (playerRoot == null)
        {
            playerRoot = GameObject.Find("PlayerRoot")?.transform;
            if (playerRoot != null) crowdRoot = playerRoot.Find("CrowdRoot");
        }

        if (cam == null) cam = Camera.main;
        if (cam == null || playerRoot == null) return;

        OverrideCamera();
        CompactPlayerCrowd();
        CompactEnemyCrowds();
        LimitVisibleContent();
    }

    private void RescanScene()
    {
        cam = Camera.main;
        playerRoot = GameObject.Find("PlayerRoot")?.transform;
        crowdRoot = playerRoot != null ? playerRoot.Find("CrowdRoot") : null;

        gateLabels.Clear();
        eventRoots.Clear();
        enemyRoots.Clear();

        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !t.gameObject.scene.IsValid()) continue;

            string n = t.name;

            if (n.StartsWith("GatePair_", StringComparison.Ordinal) ||
                n.StartsWith("EnemyCrowd_", StringComparison.Ordinal) ||
                n == "Finish")
            {
                if (t.parent == null) eventRoots.Add(t);
                if (n.StartsWith("EnemyCrowd_", StringComparison.Ordinal)) enemyRoots.Add(t);
            }

            TextMesh tm = t.GetComponent<TextMesh>();
            if (tm != null && n == "Label")
            {
                Transform root = t.parent;
                if (root != null && root.name.StartsWith("GatePair_", StringComparison.Ordinal))
                {
                    bool positive = tm.text.StartsWith("+", StringComparison.Ordinal) ||
                                    tm.text.StartsWith("x", StringComparison.OrdinalIgnoreCase);

                    gateLabels.Add(new WorldLabel
                    {
                        textMesh = tm,
                        root = root,
                        positive = positive
                    });

                    Renderer renderer = t.GetComponent<Renderer>();
                    if (renderer != null) renderer.enabled = false;
                }
            }
        }

        eventRoots.Sort((a, b) => EventZ(a).CompareTo(EventZ(b)));
    }

    private void OverrideCamera()
    {
        cam.fieldOfView = 46f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 85f;

        Vector3 p = playerRoot.position;
        Vector3 desired = new Vector3(p.x * 0.08f, 10.4f, p.z - 13.8f);

        cam.transform.position = Vector3.Lerp(
            cam.transform.position,
            desired,
            10f * Time.unscaledDeltaTime
        );

        cam.transform.rotation = Quaternion.Euler(31.5f, 0f, 0f);
        cam.backgroundColor = new Color(0.67f, 0.83f, 0.95f);
    }

    private void CompactPlayerCrowd()
    {
        if (crowdRoot == null) return;

        int activeCount = 0;

        for (int i = 0; i < crowdRoot.childCount; i++)
        {
            Transform child = crowdRoot.GetChild(i);
            if (child.gameObject.activeSelf && child.name == "Runner") activeCount++;
        }

        if (activeCount <= 0) return;

        int index = 0;

        for (int i = 0; i < crowdRoot.childCount; i++)
        {
            Transform child = crowdRoot.GetChild(i);
            if (!child.gameObject.activeSelf || child.name != "Runner") continue;

            child.localPosition = Slot(index, activeCount, 0.38f, 15);
            child.localScale = new Vector3(0.30f, 0.42f, 0.30f);
            index++;
        }
    }

    private void CompactEnemyCrowds()
    {
        for (int r = 0; r < enemyRoots.Count; r++)
        {
            Transform root = enemyRoots[r];
            if (root == null || !root.gameObject.activeInHierarchy) continue;

            int activeCount = 0;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.gameObject.activeSelf && child.name == "Enemy") activeCount++;
            }

            if (activeCount <= 0) continue;

            int index = 0;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.gameObject.activeSelf || child.name != "Enemy") continue;

                Vector3 local = Slot(index, activeCount, 0.40f, 12);
                float baseZ = EventZ(root);
                child.position = new Vector3(local.x, 0.58f, baseZ + local.z);
                child.localScale = new Vector3(0.30f, 0.42f, 0.30f);
                index++;
            }
        }
    }

    private Vector3 Slot(int index, int count, float spacing, int maxColumns)
    {
        int columns = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count)) * 1.1f),
            1,
            maxColumns
        );

        int row = index / columns;
        int col = index % columns;
        int rowCount = Mathf.Min(columns, count - row * columns);
        float width = (rowCount - 1) * spacing;

        return new Vector3(
            col * spacing - width * 0.5f,
            0.58f,
            -row * spacing
        );
    }

    private void LimitVisibleContent()
    {
        if (eventRoots.Count == 0) return;

        float playerZ = playerRoot.position.z;
        Transform nearestFuture = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];
            if (root == null) continue;

            float d = EventZ(root) - playerZ;

            if (d >= -1.8f && d < nearestDistance)
            {
                nearestDistance = d;
                nearestFuture = root;
            }
        }

        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];
            if (root == null) continue;

            float d = EventZ(root) - playerZ;
            bool nearBattle = root.name.StartsWith("EnemyCrowd_", StringComparison.Ordinal) &&
                              d > -2.2f && d < 2.2f;

            bool show = (root == nearestFuture && d > -1.8f && d < 30f) || nearBattle;

            if (root.gameObject.activeSelf != show)
                root.gameObject.SetActive(show);
        }
    }

    private float EventZ(Transform root)
    {
        if (root == null) return float.MaxValue;

        if (root.childCount > 0)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name == "PostL" || c.name == "PostR" || c.name == "Header" ||
                    c.name == "Enemy" || c.name.StartsWith("Finish", StringComparison.Ordinal))
                    return c.position.z;
            }
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
        if (positiveStyle != null) return;

        int gateFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.067f), 24, 72);
        int infoFont = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.043f), 18, 52);

        positiveStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = gateFont,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.30f, 1f, 0.48f) }
        };

        negativeStyle = new GUIStyle(positiveStyle);
        negativeStyle.normal.textColor = new Color(1f, 0.38f, 0.42f);

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
        if (playerRoot == null || cam == null) return;

        InitStyles();
        DrawGateLabels();
        DrawEnemyLabel();
        DrawFinishLabel();
    }

    private void DrawGateLabels()
    {
        for (int i = 0; i < gateLabels.Count; i++)
        {
            WorldLabel item = gateLabels[i];

            if (item.textMesh == null || item.root == null || !item.root.gameObject.activeInHierarchy)
                continue;

            float d = EventZ(item.root) - playerRoot.position.z;
            if (d <= 1.5f || d > 28f) continue;

            DrawProjected(
                item.textMesh.transform.position,
                item.textMesh.text,
                item.positive ? positiveStyle : negativeStyle,
                0.24f
            );
        }
    }

    private void DrawEnemyLabel()
    {
        for (int i = 0; i < enemyRoots.Count; i++)
        {
            Transform root = enemyRoots[i];

            if (root == null || !root.gameObject.activeInHierarchy) continue;

            float d = EventZ(root) - playerRoot.position.z;
            if (d < -1.8f || d > 28f) continue;

            int count = 0;

            for (int c = 0; c < root.childCount; c++)
            {
                Transform child = root.GetChild(c);
                if (child.name == "Enemy" && child.gameObject.activeSelf) count++;
            }

            DrawProjected(
                new Vector3(0f, 2.0f, EventZ(root)),
                "INIMIGOS " + count,
                enemyStyle,
                0.36f
            );

            break;
        }
    }

    private void DrawFinishLabel()
    {
        for (int i = 0; i < eventRoots.Count; i++)
        {
            Transform root = eventRoots[i];

            if (root == null || root.name != "Finish" || !root.gameObject.activeInHierarchy)
                continue;

            float z = EventZ(root);
            float d = z - playerRoot.position.z;
            if (d <= 1.5f || d > 28f) continue;

            DrawProjected(new Vector3(0f, 3.4f, z), "CHEGADA", finishStyle, 0.34f);
            break;
        }
    }

    private void DrawProjected(Vector3 world, string text, GUIStyle style, float widthRatio)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f) return;
        if (screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height) return;

        float width = Screen.width * widthRatio;
        float height = Mathf.Clamp(Screen.height * 0.05f, 38f, 86f);
        float x = screen.x - width * 0.5f;
        float y = Screen.height - screen.y - height * 0.5f;

        GUI.Box(new Rect(x, y, width, height), string.Empty);
        GUI.Label(new Rect(x, y, width, height), text, style);
    }
}
