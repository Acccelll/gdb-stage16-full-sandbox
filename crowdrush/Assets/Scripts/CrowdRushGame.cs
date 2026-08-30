using System;
using System.Collections.Generic;
using UnityEngine;

public enum CrowdRushState { MainMenu, Playing, Battle, Result, Paused }
public enum GateOperation { Add, Subtract, Multiply, Divide }
public enum RunEventType { GatePair, Enemy, Finish }

[Serializable]
public struct GateRule
{
    public GateOperation operation;
    public int value;

    public int Apply(int current)
    {
        switch (operation)
        {
            case GateOperation.Add: return Mathf.Max(0, current + value);
            case GateOperation.Subtract: return Mathf.Max(0, current - value);
            case GateOperation.Multiply: return Mathf.Max(0, current * Mathf.Max(1, value));
            case GateOperation.Divide: return Mathf.Max(1, current / Mathf.Max(1, value));
            default: return current;
        }
    }

    public string Label
    {
        get
        {
            switch (operation)
            {
                case GateOperation.Add: return "+" + value;
                case GateOperation.Subtract: return "-" + value;
                case GateOperation.Multiply: return "x" + value;
                case GateOperation.Divide: return "/" + value;
                default: return "?";
            }
        }
    }
}

public sealed class CrowdRushBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindFirstObjectByType<CrowdRushGame>() != null) return;
        GameObject root = new GameObject("CrowdRushGame");
        root.AddComponent<CrowdRushGame>();
    }
}

public sealed class CrowdRushGame : MonoBehaviour
{
    private sealed class LevelData
    {
        public int number;
        public int startCrowd;
        public int reward;
        public float speed;
        public float length;
        public int difficulty;
    }

    private sealed class RunEvent
    {
        public RunEventType type;
        public float z;
        public bool processed;
        public GateRule left;
        public GateRule right;
        public int enemyCount;
        public GameObject visualRoot;
        public readonly List<GameObject> enemyVisuals = new List<GameObject>();
    }

    private const int MaxLevels = 10;
    private const int VisibleCrowdLimit = 300;
    private const float LaneX = 2.35f;
    private const float PlayerXLimit = 4.6f;

    private CrowdRushState state = CrowdRushState.MainMenu;
    private readonly List<RunEvent> events = new List<RunEvent>();
    private readonly List<GameObject> crowdUnits = new List<GameObject>();
    private readonly List<GameObject> worldObjects = new List<GameObject>();

    private Camera mainCamera;
    private Transform playerRoot;
    private Transform crowdRoot;
    private Material roadMat;
    private Material playerMat;
    private Material enemyMat;
    private Material positiveMat;
    private Material negativeMat;
    private Material finishMat;

    private LevelData level;
    private int crowdCount;
    private int coins;
    private int highestLevel;
    private int startingCrowdUpgrade;
    private bool resultWasVictory;
    private int resultReward;
    private float playerZ;
    private float playerX;
    private float dragLastX;
    private bool dragging;
    private float battleTimer;
    private int battlePlayerStart;
    private int battleEnemyStart;
    private int battlePlayerEnd;
    private int battleEnemyEnd;
    private RunEvent battleEvent;
    private float resultDelay;
    private string nearbyChoice = string.Empty;

    private GUIStyle titleStyle;
    private GUIStyle textStyle;
    private GUIStyle bigStyle;
    private GUIStyle buttonStyle;
    private GUIStyle smallStyle;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        Screen.orientation = ScreenOrientation.Portrait;
        QualitySettings.vSyncCount = 0;
        LoadSave();
        CreateMaterials();
        CreateCameraAndLight();
        BuildMenuBackdrop();
    }

    private void Update()
    {
        if (state == CrowdRushState.Playing) UpdatePlaying();
        else if (state == CrowdRushState.Battle) UpdateBattle();
        else if (state == CrowdRushState.Result && resultDelay > 0f) resultDelay -= Time.deltaTime;
    }

    private void UpdatePlaying()
    {
        HandleHorizontalInput();
        playerZ += level.speed * Time.deltaTime;
        playerRoot.position = new Vector3(playerX, 0f, playerZ);
        UpdateCamera();
        UpdateNearbyGateHint();

        for (int i = 0; i < events.Count; i++)
        {
            RunEvent e = events[i];
            if (e.processed || playerZ < e.z) continue;
            e.processed = true;
            if (e.type == RunEventType.GatePair) ResolveGate(e);
            else if (e.type == RunEventType.Enemy) StartBattle(e);
            else if (e.type == RunEventType.Finish) CompleteLevel();
            break;
        }
    }

    private void HandleHorizontalInput()
    {
        float delta = 0f;
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began) { dragging = true; dragLastX = t.position.x; }
            else if (dragging && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
            {
                delta = t.position.x - dragLastX;
                dragLastX = t.position.x;
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) dragging = false;
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) { dragging = true; dragLastX = Input.mousePosition.x; }
            if (dragging && Input.GetMouseButton(0))
            {
                delta = Input.mousePosition.x - dragLastX;
                dragLastX = Input.mousePosition.x;
            }
            if (Input.GetMouseButtonUp(0)) dragging = false;
        }

        if (Mathf.Abs(delta) > 0.001f)
        {
            playerX += delta / Mathf.Max(1f, Screen.width) * 10.5f;
            playerX = Mathf.Clamp(playerX, -PlayerXLimit, PlayerXLimit);
        }
    }

    private void ResolveGate(RunEvent e)
    {
        GateRule selected = playerX < 0f ? e.left : e.right;
        crowdCount = selected.Apply(crowdCount);
        SyncCrowdVisuals();
        if (crowdCount <= 0) LoseLevel();
        if (e.visualRoot != null) e.visualRoot.SetActive(false);
    }

    private void StartBattle(RunEvent e)
    {
        state = CrowdRushState.Battle;
        nearbyChoice = string.Empty;
        battleEvent = e;
        battleTimer = 0f;
        battlePlayerStart = crowdCount;
        battleEnemyStart = e.enemyCount;

        if (battlePlayerStart > battleEnemyStart)
        {
            battlePlayerEnd = battlePlayerStart - battleEnemyStart;
            battleEnemyEnd = 0;
        }
        else if (battlePlayerStart < battleEnemyStart)
        {
            battlePlayerEnd = 0;
            battleEnemyEnd = battleEnemyStart - battlePlayerStart;
        }
        else
        {
            battlePlayerEnd = 0;
            battleEnemyEnd = 0;
        }
    }

    private void UpdateBattle()
    {
        const float duration = 1.25f;
        battleTimer += Time.deltaTime;
        float t = Mathf.Clamp01(battleTimer / duration);
        crowdCount = Mathf.RoundToInt(Mathf.Lerp(battlePlayerStart, battlePlayerEnd, t));
        SetEnemyVisibleCount(battleEvent, Mathf.RoundToInt(Mathf.Lerp(battleEnemyStart, battleEnemyEnd, t)));
        SyncCrowdVisuals();
        UpdateCamera();

        if (t < 1f) return;

        crowdCount = battlePlayerEnd;
        if (battleEvent.visualRoot != null) battleEvent.visualRoot.SetActive(false);
        if (battlePlayerEnd > 0)
        {
            state = CrowdRushState.Playing;
            SyncCrowdVisuals();
        }
        else
        {
            LoseLevel();
        }
    }

    private void CompleteLevel()
    {
        resultWasVictory = true;
        resultReward = level.reward + Mathf.Min(500, crowdCount * 2);
        coins += resultReward;
        if (level.number >= highestLevel && highestLevel < MaxLevels) highestLevel = level.number + 1;
        SaveProgress();
        state = CrowdRushState.Result;
        resultDelay = 0.45f;
        nearbyChoice = string.Empty;
    }

    private void LoseLevel()
    {
        crowdCount = 0;
        SyncCrowdVisuals();
        resultWasVictory = false;
        resultReward = 0;
        state = CrowdRushState.Result;
        resultDelay = 0.45f;
        nearbyChoice = string.Empty;
    }

    private void StartLevel(int number)
    {
        ClearWorld();
        level = MakeLevel(Mathf.Clamp(number, 1, MaxLevels));
        crowdCount = level.startCrowd + startingCrowdUpgrade * 2;
        playerZ = 2f;
        playerX = 0f;
        dragging = false;
        nearbyChoice = string.Empty;

        BuildTrack(level);
        BuildPlayer();
        GenerateEvents(level);
        SyncCrowdVisuals();
        state = CrowdRushState.Playing;
    }

    private LevelData MakeLevel(int n)
    {
        return new LevelData
        {
            number = n,
            startCrowd = 10 + (n - 1) * 2,
            reward = 100 + n * 35,
            speed = 5.2f + Mathf.Min(1.8f, (n - 1) * 0.15f),
            length = 78f + n * 8f,
            difficulty = n
        };
    }

    private void GenerateEvents(LevelData l)
    {
        System.Random rng = new System.Random(2000 + l.number * 97);
        float z = 18f;
        int contentIndex = 0;
        while (z < l.length - 12f)
        {
            bool enemy = contentIndex >= 2 && contentIndex % 3 == 2;
            if (enemy)
            {
                int baseline = 8 + l.number * 5 + contentIndex * 3;
                int enemyCount = Mathf.Max(6, baseline + rng.Next(-4, 8));
                AddEnemyEvent(z, enemyCount);
            }
            else
            {
                AddGateEvent(z, MakePositiveGate(l.number, rng), MakeRiskGate(l.number, rng));
            }
            z += 13f + (float)rng.NextDouble() * 4f;
            contentIndex++;
        }
        AddFinishEvent(l.length);
    }

    private GateRule MakePositiveGate(int difficulty, System.Random rng)
    {
        if (rng.NextDouble() < 0.42)
            return new GateRule { operation = GateOperation.Multiply, value = difficulty >= 7 && rng.NextDouble() < 0.25 ? 3 : 2 };
        return new GateRule { operation = GateOperation.Add, value = 8 + difficulty * 3 + rng.Next(0, 12) };
    }

    private GateRule MakeRiskGate(int difficulty, System.Random rng)
    {
        if (rng.NextDouble() < 0.34)
            return new GateRule { operation = GateOperation.Divide, value = difficulty >= 6 && rng.NextDouble() < 0.25 ? 3 : 2 };
        if (rng.NextDouble() < 0.62)
            return new GateRule { operation = GateOperation.Subtract, value = 4 + difficulty * 2 + rng.Next(0, 8) };
        return MakePositiveGate(difficulty, rng);
    }

    private void AddGateEvent(float z, GateRule left, GateRule right)
    {
        RunEvent e = new RunEvent { type = RunEventType.GatePair, z = z, left = left, right = right };
        e.visualRoot = new GameObject("GatePair_" + z.ToString("0"));
        worldObjects.Add(e.visualRoot);
        CreateGateVisual(e.visualRoot.transform, -LaneX, z, left, IsPositive(left));
        CreateGateVisual(e.visualRoot.transform, LaneX, z, right, IsPositive(right));
        events.Add(e);
    }

    private bool IsPositive(GateRule rule)
    {
        return rule.operation == GateOperation.Add || rule.operation == GateOperation.Multiply;
    }

    private void AddEnemyEvent(float z, int count)
    {
        RunEvent e = new RunEvent { type = RunEventType.Enemy, z = z, enemyCount = count };
        e.visualRoot = new GameObject("EnemyCrowd_" + z.ToString("0"));
        worldObjects.Add(e.visualRoot);
        int visible = Mathf.Min(count, 90);
        for (int i = 0; i < visible; i++)
        {
            GameObject unit = CreateUnit("Enemy", enemyMat, e.visualRoot.transform);
            Vector3 local = CrowdSlot(i, visible, 0.56f, 10);
            unit.transform.position = new Vector3(local.x, 0.6f, z + local.z);
            e.enemyVisuals.Add(unit);
        }
        events.Add(e);
    }

    private void AddFinishEvent(float z)
    {
        RunEvent e = new RunEvent { type = RunEventType.Finish, z = z };
        e.visualRoot = new GameObject("Finish");
        worldObjects.Add(e.visualRoot);
        GameObject left = CreateCube("FinishLeft", new Vector3(-3.9f, 1.9f, z), new Vector3(0.35f, 3.8f, 0.35f), finishMat, e.visualRoot.transform);
        GameObject right = CreateCube("FinishRight", new Vector3(3.9f, 1.9f, z), new Vector3(0.35f, 3.8f, 0.35f), finishMat, e.visualRoot.transform);
        GameObject top = CreateCube("FinishTop", new Vector3(0f, 3.6f, z), new Vector3(8.1f, 0.35f, 0.35f), finishMat, e.visualRoot.transform);
        events.Add(e);
    }

    private void CreateGateVisual(Transform parent, float x, float z, GateRule rule, bool positive)
    {
        Material mat = positive ? positiveMat : negativeMat;
        CreateCube("PostL", new Vector3(x - 1.55f, 1.25f, z), new Vector3(0.18f, 2.5f, 0.25f), mat, parent);
        CreateCube("PostR", new Vector3(x + 1.55f, 1.25f, z), new Vector3(0.18f, 2.5f, 0.25f), mat, parent);
        CreateCube("Header", new Vector3(x, 2.45f, z), new Vector3(3.25f, 0.26f, 0.25f), mat, parent);
        GameObject label = new GameObject("Label");
        label.transform.SetParent(parent, false);
        label.transform.position = new Vector3(x, 2.45f, z - 0.2f);
        label.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = rule.Label;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.35f;
        tm.fontSize = 64;
        tm.color = Color.white;
    }

    private void UpdateNearbyGateHint()
    {
        nearbyChoice = string.Empty;
        for (int i = 0; i < events.Count; i++)
        {
            RunEvent e = events[i];
            if (e.processed || e.type != RunEventType.GatePair) continue;
            float d = e.z - playerZ;
            if (d > 0f && d < 12f) nearbyChoice = e.left.Label + "     |     " + e.right.Label;
            break;
        }
    }

    private void BuildTrack(LevelData l)
    {
        int segments = Mathf.CeilToInt((l.length + 16f) / 8f);
        for (int i = 0; i < segments; i++)
        {
            float z = i * 8f;
            CreateCube("Road", new Vector3(0f, -0.12f, z), new Vector3(10f, 0.22f, 8.05f), roadMat, null);
            if (i % 2 == 0)
            {
                CreateCube("EdgeL", new Vector3(-5.2f, 0.08f, z), new Vector3(0.18f, 0.18f, 8f), positiveMat, null);
                CreateCube("EdgeR", new Vector3(5.2f, 0.08f, z), new Vector3(0.18f, 0.18f, 8f), positiveMat, null);
            }
        }
    }

    private void BuildPlayer()
    {
        playerRoot = new GameObject("PlayerRoot").transform;
        worldObjects.Add(playerRoot.gameObject);
        crowdRoot = new GameObject("CrowdRoot").transform;
        crowdRoot.SetParent(playerRoot, false);
        playerRoot.position = new Vector3(playerX, 0f, playerZ);
    }

    private void SyncCrowdVisuals()
    {
        if (crowdRoot == null) return;
        int target = Mathf.Min(crowdCount, VisibleCrowdLimit);
        while (crowdUnits.Count < target)
        {
            GameObject unit = CreateUnit("Runner", playerMat, crowdRoot);
            crowdUnits.Add(unit);
        }
        for (int i = 0; i < crowdUnits.Count; i++)
        {
            bool active = i < target;
            crowdUnits[i].SetActive(active);
            if (!active) continue;
            Vector3 p = CrowdSlot(i, target, 0.52f, 14);
            crowdUnits[i].transform.localPosition = new Vector3(p.x, 0.6f, p.z);
        }
    }

    private Vector3 CrowdSlot(int index, int count, float spacing, int maxColumns)
    {
        int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))), 1, maxColumns);
        int row = index / columns;
        int col = index % columns;
        float width = (columns - 1) * spacing;
        return new Vector3(col * spacing - width * 0.5f, 0f, -row * spacing);
    }

    private void SetEnemyVisibleCount(RunEvent e, int logicalCount)
    {
        if (e == null) return;
        int target = Mathf.Min(logicalCount, e.enemyVisuals.Count);
        for (int i = 0; i < e.enemyVisuals.Count; i++) e.enemyVisuals[i].SetActive(i < target);
    }

    private GameObject CreateUnit(string name, Material mat, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(0.34f, 0.48f, 0.34f);
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        return go;
    }

    private GameObject CreateCube(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (parent != null) go.transform.SetParent(parent, true);
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        if (parent == null) worldObjects.Add(go);
        return go;
    }

    private void CreateCameraAndLight()
    {
        GameObject camGo = new GameObject("Main Camera");
        mainCamera = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.58f, 0.78f, 0.92f);
        mainCamera.fieldOfView = 58f;
        camGo.transform.position = new Vector3(0f, 9f, -10f);
        camGo.transform.rotation = Quaternion.Euler(28f, 0f, 0f);

        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.05f;
        lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
    }

    private void UpdateCamera()
    {
        if (mainCamera == null) return;
        Vector3 target = new Vector3(playerX * 0.18f, 8.5f, playerZ - 10.5f);
        mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, target, 8f * Time.deltaTime);
        mainCamera.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
    }

    private void CreateMaterials()
    {
        roadMat = MakeMaterial(new Color(0.18f, 0.2f, 0.23f));
        playerMat = MakeMaterial(new Color(0.08f, 0.58f, 1f));
        enemyMat = MakeMaterial(new Color(0.95f, 0.18f, 0.2f));
        positiveMat = MakeMaterial(new Color(0.12f, 0.82f, 0.34f));
        negativeMat = MakeMaterial(new Color(0.92f, 0.18f, 0.23f));
        finishMat = MakeMaterial(new Color(1f, 0.72f, 0.08f));
    }

    private Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material m = new Material(shader);
        m.color = color;
        return m;
    }

    private void BuildMenuBackdrop()
    {
        for (int i = 0; i < 7; i++)
        {
            GameObject cube = CreateCube("MenuTile", new Vector3(0f, -0.1f, i * 8f), new Vector3(10f, 0.2f, 8.05f), roadMat, null);
        }
        mainCamera.transform.position = new Vector3(0f, 8.5f, -10f);
    }

    private void ClearWorld()
    {
        for (int i = 0; i < worldObjects.Count; i++) if (worldObjects[i] != null) Destroy(worldObjects[i]);
        worldObjects.Clear();
        events.Clear();
        crowdUnits.Clear();
    }

    private void LoadSave()
    {
        coins = PlayerPrefs.GetInt("cr_coins", 0);
        highestLevel = Mathf.Clamp(PlayerPrefs.GetInt("cr_level", 1), 1, MaxLevels);
        startingCrowdUpgrade = Mathf.Clamp(PlayerPrefs.GetInt("cr_start_upgrade", 0), 0, 20);
    }

    private void SaveProgress()
    {
        PlayerPrefs.SetInt("cr_coins", coins);
        PlayerPrefs.SetInt("cr_level", highestLevel);
        PlayerPrefs.SetInt("cr_start_upgrade", startingCrowdUpgrade);
        PlayerPrefs.Save();
    }

    private int UpgradeCost { get { return 100 + startingCrowdUpgrade * 75; } }

    private void BuyStartingCrowdUpgrade()
    {
        int cost = UpgradeCost;
        if (coins < cost) return;
        coins -= cost;
        startingCrowdUpgrade++;
        SaveProgress();
    }

    private void InitStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.width * 0.085f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.width * 0.048f), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.width * 0.072f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.width * 0.035f), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(Screen.width * 0.045f), fontStyle = FontStyle.Bold };
    }

    private void OnGUI()
    {
        InitStyles();
        float w = Screen.width;
        float h = Screen.height;

        if (state == CrowdRushState.MainMenu)
        {
            GUI.Box(new Rect(w * 0.08f, h * 0.14f, w * 0.84f, h * 0.62f), string.Empty);
            GUI.Label(new Rect(0f, h * 0.18f, w, h * 0.1f), "CROWD RUSH", titleStyle);
            GUI.Label(new Rect(0f, h * 0.29f, w, h * 0.06f), "Fase " + highestLevel + "   |   Moedas " + coins, textStyle);
            if (GUI.Button(new Rect(w * 0.18f, h * 0.39f, w * 0.64f, h * 0.085f), "JOGAR", buttonStyle)) StartLevel(highestLevel);
            GUI.Label(new Rect(0f, h * 0.51f, w, h * 0.05f), "Crowd inicial +" + (startingCrowdUpgrade * 2), smallStyle);
            string upgradeText = coins >= UpgradeCost ? "MELHORAR - " + UpgradeCost : "MELHORIA - " + UpgradeCost + " moedas";
            if (GUI.Button(new Rect(w * 0.18f, h * 0.57f, w * 0.64f, h * 0.075f), upgradeText, buttonStyle)) BuyStartingCrowdUpgrade();
            GUI.Label(new Rect(0f, h * 0.68f, w, h * 0.04f), "Arraste para mover a multidao", smallStyle);
            return;
        }

        if (state == CrowdRushState.Playing || state == CrowdRushState.Battle)
        {
            GUI.Label(new Rect(0f, h * 0.025f, w * 0.45f, h * 0.05f), "FASE " + level.number, textStyle);
            GUI.Label(new Rect(w * 0.55f, h * 0.025f, w * 0.43f, h * 0.05f), coins + " moedas", textStyle);
            GUI.Label(new Rect(0f, h * 0.085f, w, h * 0.09f), crowdCount.ToString(), bigStyle);
            float progress = Mathf.Clamp01(playerZ / Mathf.Max(1f, level.length));
            GUI.Box(new Rect(w * 0.12f, h * 0.18f, w * 0.76f, h * 0.022f), string.Empty);
            GUI.Box(new Rect(w * 0.12f, h * 0.18f, w * 0.76f * progress, h * 0.022f), string.Empty);
            if (!string.IsNullOrEmpty(nearbyChoice)) GUI.Label(new Rect(0f, h * 0.73f, w, h * 0.07f), nearbyChoice, bigStyle);
            if (state == CrowdRushState.Battle) GUI.Label(new Rect(0f, h * 0.68f, w, h * 0.06f), "BATALHA", textStyle);
            return;
        }

        if (state == CrowdRushState.Result)
        {
            GUI.Box(new Rect(w * 0.08f, h * 0.2f, w * 0.84f, h * 0.54f), string.Empty);
            GUI.Label(new Rect(0f, h * 0.25f, w, h * 0.09f), resultWasVictory ? "FASE CONCLUIDA" : "DERROTA", titleStyle);
            if (resultWasVictory)
            {
                GUI.Label(new Rect(0f, h * 0.37f, w, h * 0.06f), "+" + resultReward + " moedas", textStyle);
                GUI.Label(new Rect(0f, h * 0.44f, w, h * 0.05f), "Sobreviventes: " + crowdCount, smallStyle);
            }
            if (resultDelay <= 0f)
            {
                string action = resultWasVictory ? (level.number < MaxLevels ? "PROXIMA FASE" : "MENU") : "TENTAR NOVAMENTE";
                if (GUI.Button(new Rect(w * 0.18f, h * 0.56f, w * 0.64f, h * 0.085f), action, buttonStyle))
                {
                    if (resultWasVictory && level.number < MaxLevels) StartLevel(Mathf.Min(MaxLevels, level.number + 1));
                    else if (!resultWasVictory) StartLevel(level.number);
                    else ReturnToMenu();
                }
                if (GUI.Button(new Rect(w * 0.26f, h * 0.66f, w * 0.48f, h * 0.06f), "MENU", buttonStyle)) ReturnToMenu();
            }
        }
    }

    private void ReturnToMenu()
    {
        ClearWorld();
        state = CrowdRushState.MainMenu;
        BuildMenuBackdrop();
    }
}
