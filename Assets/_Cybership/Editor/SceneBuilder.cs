// ============================================================
// NCV-01 Cybership - Procedural Scene Builder (Editor)
//
// Menu: Cybership > Build NCV-01 Scene
// Run:  Unity -batchmode -quit -executeMethod SceneBuilder.Build
//
// Assembles the full world per docs/01-Scene-Setup.md +
// docs/02-Prefab-Configuration.md + docs/06-Ship-Designer.md:
// hull, five duty stations + captain's chair, MAGI cores,
// reactor, ship-designer mount grid, HUD, console UIs, all
// UdonSharp wiring, sync modes, and the VRCSceneDescriptor.
// ============================================================

using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using SyncType = VRC.SDKBase.Networking.SyncType;

public static class SceneBuilder
{
    const string ScenePath = "Assets/_Cybership/Scenes/NCV-01_Cybership.unity";
    const string MatDir = "Assets/_Cybership/Materials";
    const string PrefabDir = "Assets/_Cybership/Prefabs";

    static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    static Font _font;
    static TMP_FontAsset _tmpFont;

    static Transform _root;
    static Transform _systemsRoot;
    static Transform _stationsRoot;
    static Transform _shipRoot;

    static ShipStateManager _shipState;
    static PlayerProgressionManager _progression;
    static WatchScheduleManager _watch;
    static MAGISystem _magi;
    static EmergencyEventManager _emergency;
    static MissionManager _mission;
    static HUDManager _hud;

    static readonly List<UdonSharpBehaviour> Proxies = new List<UdonSharpBehaviour>();
    static readonly List<StationController> Stations = new List<StationController>();

    [MenuItem("Cybership/Build NCV-01 Scene")]
    public static void Build()
    {
        Debug.Log("[SceneBuilder] Starting build...");

        EnsureUdonSharpProgramAssets();

        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        ResetUdonSharpCaches();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);

        EnsureFolder("Assets/_Cybership/Editor");
        EnsureFolder(MatDir);
        EnsureFolder(PrefabDir);
        EnsureFolder("Assets/_Cybership/Scenes");

        _font = LoadFont();
        _tmpFont = LoadTMPFont();
        CreateMaterials();

        var rootGo = new GameObject("NCV-01");
        _root = rootGo.transform;

        var systemsGo = NewChild(_root, "_SYSTEMS", Vector3.zero);
        _systemsRoot = systemsGo.transform;
        var stationsGo = NewChild(_root, "_STATIONS", Vector3.zero);
        _stationsRoot = stationsGo.transform;
        var shipGo = NewChild(_root, "_SHIP", Vector3.zero);
        _shipRoot = shipGo.transform;

        BuildShip();

        BuildSystems();
        BuildHud();

        BuildStation<TacticalStation>("STATION_TACTICAL", new Vector3(-8f, 0f, 8f), Quaternion.identity, BuildTactical);
        BuildStation<NavigationStation>("STATION_NAVIGATION", new Vector3(-14f, 0f, 6f), Quaternion.identity, BuildNavigation);
        BuildStation<ScienceStation>("STATION_SCIENCE", new Vector3(8f, 0f, 8f), Quaternion.identity, BuildScience);
        BuildStation<EngineeringStation>("STATION_ENGINEERING", new Vector3(16f, 0f, 4f), Quaternion.Euler(0f, -90f, 0f), BuildEngineering);
        BuildStation<CommunicationsStation>("STATION_COMMS", new Vector3(0f, 0f, 0f), Quaternion.identity, BuildComms);
        BuildCaptainsChair();
        BuildShipDesigner();
        _mission.stations = Stations.ToArray();
        BuildLights();

        BuildSpawn(rootGo);

        var descriptor = rootGo.GetComponent<VRCSceneDescriptor>();
        if (descriptor == null)
            descriptor = rootGo.AddComponent<VRCSceneDescriptor>();
        descriptor.spawns = new[] { _spawnPoint.transform };
        descriptor.spawnRadius = 2f;
        descriptor.RespawnHeightY = -5f;

        RegisterDynamicPrefab(rootGo);

        foreach (var proxy in Proxies)
        {
            if (proxy != null)
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        }

        UdonSharpProgramAsset.CompileAllCsPrograms(true);

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);

        var buildSettings = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        EditorBuildSettings.scenes = buildSettings.ToArray();

        Debug.Log("[SceneBuilder] Build complete: " + ScenePath);
    }

    // ============================================================
    // SHIP VISUALS
    // ============================================================
    static Renderer _viewscreenRenderer;
    static Renderer _reactorRenderer;
    static Renderer[] _magiCoreRenderers;
    static Light[] _emergencyLights;
    static readonly List<Renderer> AlertSurfaces = new List<Renderer>();

    static void BuildShip()
    {
        AddBox(_shipRoot, "FLOOR", new Vector3(0f, -0.5f, 2f), new Vector3(44f, 1f, 32f), "MAT_Floor");
        AddBox(_shipRoot, "WALL_N", new Vector3(0f, 3f, -12f), new Vector3(44f, 6f, 1f), "MAT_HullWall");
        AddBox(_shipRoot, "WALL_S", new Vector3(0f, 3f, 16f), new Vector3(44f, 6f, 1f), "MAT_HullWall");
        AddBox(_shipRoot, "WALL_W", new Vector3(-22f, 3f, 2f), new Vector3(1f, 6f, 32f), "MAT_HullWall");
        AddBox(_shipRoot, "WALL_E", new Vector3(22f, 3f, 2f), new Vector3(1f, 6f, 32f), "MAT_HullWall");
        AddBox(_shipRoot, "CEILING", new Vector3(0f, 6f, 2f), new Vector3(44f, 1f, 32f), "MAT_HullWall");

        _viewscreenRenderer = AddBox(_shipRoot, "VIEWSCREEN", new Vector3(0f, 3f, 15.3f),
            new Vector3(8f, 3f, 0.1f), "MAT_AlertStrip").GetComponent<Renderer>();
        _viewscreenRenderer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        AlertSurfaces.Add(_viewscreenRenderer);

        _reactorRenderer = AddCylinder(_shipRoot, "REACTOR_CORE", new Vector3(19f, 1.5f, 0f),
            new Vector3(2f, 2.5f, 2f), "MAT_Reactor").GetComponent<Renderer>();
        AlertSurfaces.Add(_reactorRenderer);

        var reactorGlowGo = NewChild(_shipRoot, "REACTOR_GLOW", new Vector3(19f, 2f, 0f));
        var reactorGlow = reactorGlowGo.AddComponent<Light>();
        reactorGlow.type = LightType.Point;
        reactorGlow.color = new Color(1f, 0.5f, 0.1f);
        reactorGlow.range = 12f;
        reactorGlow.intensity = 3f;

        var coreGos = new List<GameObject>();
        for (int i = 0; i < 3; i++)
        {
            var pos = new Vector3(17f + i * 1.5f, 1.5f, -6f);
            coreGos.Add(AddCylinder(_shipRoot, "MAGI_CORE_" + i, pos, new Vector3(1f, 2f, 1f), "MAT_MAGI_Standby"));
        }
        _magiCoreRenderers = coreGos.ConvertAll(g => g.GetComponent<Renderer>()).ToArray();

        AlertSurfaces.Add(AddBox(_shipRoot, "ALERT_STRIP_1", new Vector3(-20f, 0.6f, 2f),
            new Vector3(0.2f, 0.3f, 28f), "MAT_AlertStrip").GetComponent<Renderer>());
        AlertSurfaces.Add(AddBox(_shipRoot, "ALERT_STRIP_2", new Vector3(20f, 0.6f, 2f),
            new Vector3(0.2f, 0.3f, 28f), "MAT_AlertStrip").GetComponent<Renderer>());

        var emergencyGo = NewChild(_shipRoot, "EMERGENCY_LIGHTS", Vector3.zero);
        var positions = new[]
        {
            new Vector3(-18f, 4.5f, 6f), new Vector3(-8f, 4.5f, 14f), new Vector3(8f, 4.5f, 14f),
            new Vector3(18f, 4.5f, 6f), new Vector3(-16f, 4.5f, -8f), new Vector3(16f, 4.5f, -8f)
        };
        var lights = new List<Light>();
        for (int i = 0; i < positions.Length; i++)
        {
            var lg = NewChild(emergencyGo.transform, "EMERGENCY_LIGHT_" + i, positions[i]);
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.15f);
            light.range = 10f;
            light.intensity = 2f;
            light.enabled = false;
            lights.Add(light);
        }
        _emergencyLights = lights.ToArray();
    }

    // ============================================================
    // SYSTEMS
    // ============================================================
    static void BuildSystems()
    {
        var shipGo = NewChild(_systemsRoot, "SHIP_STATE", Vector3.zero);
        _shipState = AddUdon<ShipStateManager>(shipGo, SyncType.Continuous);
        var alertAudio = shipGo.AddComponent<AudioSource>();
        alertAudio.playOnAwake = false;
        _shipState.alertAudioSource = alertAudio;
        _shipState.emergencyLights = _emergencyLights;
        _shipState.alertSurfaces = AlertSurfaces.ToArray();

        var playerGo = NewChild(_systemsRoot, "PLAYER_MANAGER", Vector3.zero);
        _progression = AddUdon<PlayerProgressionManager>(playerGo, SyncType.Continuous);

        var watchGo = NewChild(_systemsRoot, "WATCH_MANAGER", Vector3.zero);
        _watch = AddUdon<WatchScheduleManager>(watchGo, SyncType.Manual);
        _watch.progression = _progression;

        var magiGo = NewChild(_systemsRoot, "MAGI", Vector3.zero);
        _magi = AddUdon<MAGISystem>(magiGo, SyncType.Manual);
        _magi.coreRenderers = _magiCoreRenderers;
        _magi.coreMaterials = new[]
        {
            GetMat("MAT_MAGI_Standby"), GetMat("MAT_MAGI_Processing"),
            GetMat("MAT_MAGI_Yes"), GetMat("MAT_MAGI_No")
        };
        _magi.decisionText = MakeTextMesh(magiGo.transform, "DECISION_TEXT", "MAGI STANDBY",
            new Vector3(1.25f, 3.6f, -6f), 0.35f, TextAnchor.MiddleCenter, Color.cyan);
        _magi.voteStatusText = MakeTextMesh(magiGo.transform, "VOTE_STATUS_TEXT", "",
            new Vector3(1.25f, 3.1f, -6f), 0.2f, TextAnchor.MiddleCenter, Color.white);
        _magi.magiVoice = magiGo.AddComponent<AudioSource>();
        _magi.shipState = _shipState;

        var emergencyGo = NewChild(_systemsRoot, "EMERGENCY_MANAGER", Vector3.zero);
        _emergency = AddUdon<EmergencyEventManager>(emergencyGo, SyncType.Manual);
        _emergency.fireEffect = MakeParticleSystem(emergencyGo.transform, "FIRE_EFFECT", new Vector3(19f, 2f, 0f));
        _emergency.steamLeakEffect = MakeParticleSystem(emergencyGo.transform, "STEAM_LEAK_EFFECT", new Vector3(10f, 1f, -4f));
        _emergency.sparkEffect = MakeParticleSystem(emergencyGo.transform, "SPARK_EFFECT", new Vector3(-5f, 1f, -2f));
        _emergency.alarmAudio = emergencyGo.AddComponent<AudioSource>();
        _emergency.alarmAudio.playOnAwake = false;
        _emergency.shipState = _shipState;
        _emergency.magiSystem = _magi;

        var missionGo = NewChild(_systemsRoot, "MISSION_MANAGER", Vector3.zero);
        _mission = AddUdon<MissionManager>(missionGo, SyncType.Continuous);
        _mission.shipState = _shipState;
        _mission.progression = _progression;
        _mission.hudManager = _hud; // set again after HUD is built
    }

    static void BuildHud()
    {
        var hudCanvas = MakeWorldCanvas(_shipRoot, "HUD", new Vector3(-21.2f, 2.7f, 2f),
            Quaternion.Euler(0f, 90f, 0f), new Vector2(640f, 400f), 0.0025f);

        MakeUIImage(hudCanvas.transform, "HUD_BG", new Vector2(0f, 0f), new Vector2(640f, 400f),
            new Color(0.02f, 0.05f, 0.03f, 0.85f));

        var rank = MakeUIText(hudCanvas.transform, "RANK_TEXT", "RANK: RECRUIT", new Vector2(0f, 150f), new Vector2(600f, 40f), 34, TextAnchor.MiddleCenter, Color.white);
        var xp = MakeUIText(hudCanvas.transform, "XP_TEXT", "XP: 0", new Vector2(0f, 100f), new Vector2(600f, 30f), 26, TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.6f));
        var dept = MakeUIText(hudCanvas.transform, "DEPT_TEXT", "DEPT: UNASSIGNED", new Vector2(0f, 62f), new Vector2(600f, 30f), 24, TextAnchor.MiddleCenter, Color.cyan);
        var alert = MakeUIText(hudCanvas.transform, "ALERT_TEXT", "CONDITION GREEN", new Vector2(0f, 18f), new Vector2(600f, 32f), 28, TextAnchor.MiddleCenter, Color.green);
        var indicator = MakeUIImage(hudCanvas.transform, "ALERT_INDICATOR", new Vector2(-250f, 18f), new Vector2(40f, 40f), Color.green);
        var watch = MakeUIText(hudCanvas.transform, "WATCH_TEXT", "WATCH: NONE", new Vector2(0f, -24f), new Vector2(600f, 26f), 22, TextAnchor.MiddleCenter, Color.yellow);
        var mission = MakeUIText(hudCanvas.transform, "MISSION_TEXT", "MISSION: NONE", new Vector2(0f, -58f), new Vector2(600f, 26f), 22, TextAnchor.MiddleCenter, Color.white);

        var notifPanel = new GameObject("NOTIFICATION_PANEL", typeof(RectTransform));
        notifPanel.transform.SetParent(hudCanvas.transform, false);
        var nRt = notifPanel.GetComponent<RectTransform>();
        nRt.anchoredPosition = new Vector2(0f, -140f);
        nRt.sizeDelta = new Vector2(560f, 70f);
        notifPanel.AddComponent<CanvasRenderer>();
        var nImg = notifPanel.AddComponent<Image>();
        nImg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        var nText = MakeUIText(notifPanel.transform, "NOTIFICATION_TEXT", "", Vector2.zero, new Vector2(540f, 60f), 30, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.2f));
        notifPanel.SetActive(false);

        var hudGo = hudCanvas.gameObject;
        _hud = AddUdon<HUDManager>(hudGo, SyncType.None);
        _hud.rankText = rank;
        _hud.xpText = xp;
        _hud.departmentText = dept;
        _hud.alertText = alert;
        _hud.alertIndicator = indicator;
        _hud.watchText = watch;
        _hud.missionText = mission;
        _hud.notificationPanel = notifPanel;
        _hud.notificationText = nText;
        _hud.progression = _progression;
        _hud.shipState = _shipState;
        _hud.watchSchedule = _watch;
        _hud.emergencyManager = _emergency;
        _hud.missionManager = _mission;

        _shipState.hudManager = _hud;
        _progression.uiManager = _hud;
        _emergency.hudManager = _hud;
        _mission.hudManager = _hud;
    }

    // ============================================================
    // STATIONS
    // ============================================================
    static void BuildStation<T>(string name, Vector3 pos, Quaternion rot, System.Action<GameObject, T, Transform, VRC.SDKBase.VRCStation> builder) where T : StationController
    {
        var root = NewChild(_stationsRoot, name, pos, rot);

        var seat = NewChild(root.transform, "SEAT", new Vector3(0f, 0.45f, 0f), new Vector3(1f, 0.9f, 0.9f));
        AddBoxMaterial(seat, "MAT_Seat");

        var vrStation = seat.AddComponent<VRC.SDK3.Components.VRCStation>();

        var station = AddUdon<T>(seat, SyncType.Manual);
        station.vrStation = vrStation;
        station.shipState = _shipState;
        station.progression = _progression;

        var uiCanvas = BuildStationUICanvas(seat.transform, name, station);
        var locked = BuildLockedUI(seat.transform, name);
        station.stationUI = uiCanvas;
        station.lockedUI = locked;

        builder(root, station, seat.transform, vrStation);

        Stations.Add(station);
    }

    static void BuildTactical(GameObject root, TacticalStation tactical, Transform seat, VRC.SDKBase.VRCStation vrStation)
    {
        AddBox(root.transform, "CONSOLE", new Vector3(0f, 0.5f, 1.4f), new Vector3(1.6f, 0.35f, 0.45f), "MAT_Console");

        var spawnParent = NewChild(root.transform, "TARGET_SPAWNS", Vector3.zero);
        var spawns = new List<Transform>();
        for (int i = 0; i < 6; i++)
        {
            float x = -4.5f + (i % 3) * 2.25f;
            float y = 1.6f + (i / 3) * 1.6f;
            float z = 2f + (i / 3) * 2f;
            var sp = NewChild(spawnParent.transform, "SPAWN_" + i, new Vector3(x, y, z));
            spawns.Add(sp.transform);
        }
        tactical.spawnPoints = spawns.ToArray();

        var cross = AddBox(root.transform, "CROSSHAIR", new Vector3(0f, 1.2f, 1.1f), new Vector3(0.06f, 0.06f, 0.06f), "MAT_EmissiveGreen");
        cross.SetActive(false);
        tactical.crosshair = cross.transform;

        var target = BuildTacticalTargetPrefab();
        tactical.targetPrefabs = new[] { target };
    }

    static void BuildNavigation(GameObject root, NavigationStation nav, Transform seat, VRC.SDKBase.VRCStation vrStation)
    {
        AddBox(root.transform, "CONSOLE", new Vector3(0f, 0.5f, 1.4f), new Vector3(1.6f, 0.35f, 0.45f), "MAT_Console");

        var nodeParent = NewChild(root.transform, "NODES", Vector3.zero);
        var nodes = new List<Transform>();
        for (int i = 0; i < 5; i++)
        {
            float x = -1f + i * 1f;
            var node = AddSphere(nodeParent.transform, "NODE_" + i, new Vector3(x, 1.4f, 4.5f), 0.25f, "MAT_EmissiveCyan");
            nodes.Add(node.transform);
        }
        nav.waypointNodes = nodes.ToArray();

        var lineGo = NewChild(root.transform, "PATH_RENDERER", new Vector3(0f, 1.6f, 1f));
        var lr = lineGo.AddComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.sharedMaterial = GetMat("MAT_PathDefault");
        nav.pathRenderer = lr;
        nav.activePathMaterial = GetMat("MAT_PathActive");
        nav.errorPathMaterial = GetMat("MAT_PathError");
        nav.defaultPathMaterial = GetMat("MAT_PathDefault");
    }

    static void BuildScience(GameObject root, ScienceStation science, Transform seat, VRC.SDKBase.VRCStation vrStation)
    {
        AddBox(root.transform, "CONSOLE", new Vector3(0f, 0.5f, 1.4f), new Vector3(1.6f, 0.35f, 0.45f), "MAT_Console");

        var displays = new List<Renderer>();
        for (int i = 0; i < 2; i++)
        {
            var d = AddBox(root.transform, "DISPLAY_" + i, new Vector3(-0.6f + i * 1.2f, 1.6f, 1.3f), new Vector3(0.7f, 0.7f, 0.1f), "MAT_Sample0");
            displays.Add(d.GetComponent<Renderer>());
        }
        science.sampleDisplays = displays.ToArray();
        science.sampleMaterials = new[]
        {
            GetMat("MAT_Sample0"), GetMat("MAT_Sample1"), GetMat("MAT_Sample2"),
            GetMat("MAT_Sample3"), GetMat("MAT_Sample4")
        };

        BuildClassifyButtons(root.transform, science);
    }

    static void BuildEngineering(GameObject root, EngineeringStation eng, Transform seat, VRC.SDKBase.VRCStation vrStation)
    {
        AddBox(root.transform, "CONSOLE", new Vector3(0f, 0.55f, -1.2f), new Vector3(1.6f, 0.5f, 0.45f), "MAT_Console");

        var gauge1 = AddBox(root.transform, "TEMP_GAUGE", new Vector3(-0.35f, 1.7f, -1.2f), new Vector3(0.3f, 0.7f, 0.1f), "MAT_EmissiveGreen");
        var gauge2 = AddBox(root.transform, "OUTPUT_GAUGE", new Vector3(0.35f, 1.7f, -1.2f), new Vector3(0.3f, 0.7f, 0.1f), "MAT_EmissiveGreen");
        eng.temperatureGauge = gauge1.GetComponent<Renderer>();
        eng.outputGauge = gauge2.GetComponent<Renderer>();

        var glowGo = NewChild(root.transform, "REACTOR_GLOW_REF", new Vector3(0f, 1.6f, -2.2f));
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.4f, 0.1f);
        glow.range = 6f;
        eng.reactorGlow = glow;

        var sliders = NewChild(root.transform, "SLIDERS", new Vector3(0f, 1.1f, -0.9f));
        eng.powerSliderTransform = BuildEngineeringSlider(sliders.transform, "POWER_SLIDER", -0.35f);
        eng.coolantSliderTransform = BuildEngineeringSlider(sliders.transform, "COOLANT_SLIDER", 0.35f);
    }

    static void BuildComms(GameObject root, CommunicationsStation comms, Transform seat, VRC.SDKBase.VRCStation vrStation)
    {
        AddBox(root.transform, "CONSOLE", new Vector3(0f, 0.5f, 1.4f), new Vector3(1.6f, 0.35f, 0.45f), "MAT_Console");

        var pads = new List<Renderer>();
        var padLights = new List<Light>();
        for (int i = 0; i < 4; i++)
        {
            float x = -0.55f + i * 0.5f;
            var pad = AddBox(root.transform, "PAD_" + i, new Vector3(x, 0.75f, 1.4f), new Vector3(0.45f, 0.12f, 0.35f), "MAT_CommPadIdle");
            pads.Add(pad.GetComponent<Renderer>());

            var lg = NewChild(root.transform, "PAD_LIGHT_" + i, new Vector3(x, 0.9f, 1.4f));
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 1.5f;
            light.intensity = 0.8f;
            light.enabled = false;
            padLights.Add(light);
        }
        comms.padRenderers = pads.ToArray();
        comms.padLights = padLights.ToArray();
        comms.signalMaterials = new[]
        {
            GetMat("MAT_Signal0"), GetMat("MAT_Signal1"), GetMat("MAT_Signal2"), GetMat("MAT_Signal3")
        };
        comms.idlePadMaterial = GetMat("MAT_CommPadIdle");

        comms.relayDisplayText = MakeTextMesh(root.transform, "RELAY_TEXT", "RELAY: STANDBY",
            new Vector3(0f, 2.2f, 1.1f), 0.16f, TextAnchor.MiddleCenter, Color.white);
        comms.scoreText = MakeTextMesh(root.transform, "SCORE_TEXT", "SCORE: 0",
            new Vector3(0f, 2f, 1.1f), 0.12f, TextAnchor.MiddleCenter, Color.yellow);

        BuildCommsButtons(root.transform, comms);
    }

    static void BuildCaptainsChair()
    {
        var root = NewChild(_stationsRoot, "CAPTAIN_CHAIR", new Vector3(0f, 0f, 13f), Quaternion.identity);

        var seat = NewChild(root.transform, "SEAT", new Vector3(0f, 0.45f, 0f), new Vector3(1.2f, 1.1f, 1f));
        var seatRenderer = AddBoxMaterial(seat, "MAT_Seat").GetComponent<Renderer>();

        var vrStation = seat.AddComponent<VRC.SDK3.Components.VRCStation>();
        var proxy = AddUdon<CaptainsChair>(seat, SyncType.Manual);
        var chair = (CaptainsChair)proxy;
        chair.chairStation = vrStation;
        chair.shipState = _shipState;
        chair.progression = _progression;
        chair.chairEmissive = seatRenderer;
        chair.activeMaterial = GetMat("MAT_EmissiveGreen");
        chair.inactiveMaterial = GetMat("MAT_Seat");

        var spotGo = NewChild(seat.transform, "CHAIR_SPOTLIGHT", new Vector3(0f, 1.2f, 0f));
        var spot = spotGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 8f;
        spot.spotAngle = 45f;
        spot.color = new Color(0.6f, 1f, 0.7f);
        spot.intensity = 1.5f;
        chair.chairSpotlight = spot;

        var auraGo = NewChild(seat.transform, "COMMAND_AURA", new Vector3(0f, 0f, 0f));
        chair.commandAura = auraGo.AddComponent<ParticleSystem>();

        AddBox(root.transform, "VIEWSCREEN_EDGE", new Vector3(0f, 2.2f, 2.2f), new Vector3(3f, 1.6f, 0.08f), "MAT_HullTrim");

        var commandUI = MakeWorldCanvas(root.transform, "COMMAND_UI", new Vector3(0f, 1.4f, 1.2f),
            Quaternion.identity, new Vector2(560f, 320f), 0.0025f);
        MakeUIImage(commandUI.transform, "CMD_BG", Vector2.zero, new Vector2(560f, 320f), new Color(0.05f, 0.05f, 0.08f, 0.85f));
        MakeButton(commandUI.transform, "BTN_GREEN", new Vector2(-140f, 100f), new Vector2(240f, 60f), "CONDITION GREEN", chair, "SetAlertGreen");
        MakeButton(commandUI.transform, "BTN_YELLOW", new Vector2(140f, 100f), new Vector2(240f, 60f), "CONDITION YELLOW", chair, "SetAlertYellow");
        MakeButton(commandUI.transform, "BTN_RED", new Vector2(-140f, 20f), new Vector2(240f, 60f), "CONDITION RED", chair, "SetAlertRed");
        MakeButton(commandUI.transform, "BTN_BLACK", new Vector2(140f, 20f), new Vector2(240f, 60f), "CONDITION BLACK", chair, "SetAlertBlack");
        MakeButton(commandUI.transform, "BTN_RELINQUISH", new Vector2(0f, -80f), new Vector2(240f, 50f), "RELINQUISH COMMAND", chair, "RelinquishCommand");
        commandUI.gameObject.SetActive(false);
        chair.commandUI = commandUI.gameObject;

        var locked = BuildLockedUI(root.transform, "CAPTAIN_CHAIR");
        chair.lockedUI = locked;
    }

    static void BuildShipDesigner()
    {
        var root = NewChild(_systemsRoot, "SHIP_DESIGNER", new Vector3(-16f, 0f, -9f), Quaternion.Euler(0f, 180f, 0f));

        var seat = NewChild(root.transform, "SEAT", new Vector3(0f, 0.45f, 0f), new Vector3(1f, 0.9f, 0.9f));
        AddBoxMaterial(seat, "MAT_Seat");
        var vrStation = seat.AddComponent<VRC.SDK3.Components.VRCStation>();

        var proxy = AddUdon<ShipDesignerManager>(seat, SyncType.Manual);
        var designer = (ShipDesignerManager)proxy;

        BuildMountGrid(designer);

        var consoleRoot = NewChild(_systemsRoot, "SHIP_DESIGNER_CONSOLE", new Vector3(-16f, 1.0f, -9.8f), Quaternion.identity);
        AddBox(consoleRoot.transform, "DESK", new Vector3(0f, -0.25f, 0f), new Vector3(1.9f, 0.5f, 1.3f), "MAT_Console");
        MakeTextMesh(consoleRoot.transform, "STATUS_TEXT", "DESIGNER STANDBY", new Vector3(0f, 0.06f, 0.55f), 0.14f, TextAnchor.MiddleCenter, Color.cyan, Quaternion.Euler(90f, 0f, 0f));
        MakeTextMesh(consoleRoot.transform, "GRID_TEXT", "", new Vector3(0f, 0.06f, -0.55f), 0.09f, TextAnchor.MiddleCenter, Color.white, Quaternion.Euler(90f, 0f, 0f));

        var canvas = MakeWorldCanvas(consoleRoot.transform, "CANVAS", new Vector3(0f, 0.03f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(700f, 560f), 0.0025f);
        MakeUIImage(canvas.transform, "DG_BG", Vector2.zero, new Vector2(700f, 560f), new Color(0.05f, 0.05f, 0.08f, 0.85f));

        var moduleLabels = new[] { "HULL", "ENGINE", "TURRET", "SENSOR", "POWER", "CARGO" };
        for (int i = 0; i < 6; i++)
        {
            float x = -240f + (i % 3) * 150f;
            float y = 220f - (i / 3) * 70f;
            MakeButton(canvas.transform, "BTN_MODULE_" + i, new Vector2(x, y), new Vector2(140f, 50f), moduleLabels[i], designer, "SelectModule" + i);
        }

        MakeButton(canvas.transform, "BTN_PLACE", new Vector2(-225f, 100f), new Vector2(100f, 44f), "PLACE", designer, "SetPlaceMode");
        MakeButton(canvas.transform, "BTN_ERASE", new Vector2(-115f, 100f), new Vector2(100f, 44f), "ERASE", designer, "SetEraseMode");
        MakeButton(canvas.transform, "BTN_ROTATE", new Vector2(-5f, 100f), new Vector2(100f, 44f), "ROTATE", designer, "RotateSelected");
        MakeButton(canvas.transform, "BTN_PREV", new Vector2(105f, 100f), new Vector2(100f, 44f), "PREV CELL", designer, "CycleCellPrevious");
        MakeButton(canvas.transform, "BTN_NEXT", new Vector2(215f, 100f), new Vector2(100f, 44f), "NEXT CELL", designer, "CycleCellNext");

        MakeButton(canvas.transform, "BTN_CLEAR", new Vector2(-160f, -30f), new Vector2(120f, 44f), "CLEAR", designer, "ClearDesign");
        MakeButton(canvas.transform, "BTN_RANDOM", new Vector2(-30f, -30f), new Vector2(120f, 44f), "RANDOM", designer, "FillRandom");
        MakeButton(canvas.transform, "BTN_SAVE", new Vector2(100f, -30f), new Vector2(120f, 44f), "SAVE", designer, "SaveDesign");
        MakeButton(canvas.transform, "BTN_LOAD", new Vector2(230f, -30f), new Vector2(120f, 44f), "LOAD", designer, "LoadDesign");

        MakeButton(canvas.transform, "BTN_APPLY", new Vector2(0f, -110f), new Vector2(200f, 54f), "APPLY TO SHIP", designer, "ApplyDesign");

        consoleRoot.SetActive(false);
        designer.designerUI = consoleRoot;

        var statusText = consoleRoot.transform.Find("STATUS_TEXT").GetComponent<TextMeshPro>();
        var gridText = consoleRoot.transform.Find("GRID_TEXT").GetComponent<TextMeshPro>();
        designer.statusText = statusText;
        designer.gridText = gridText;
        designer.hudManager = _hud;
    }

    static void BuildMountGrid(ShipDesignerManager designer)
    {
        var mounts = NewChild(_shipRoot, "_MOUNTS", Vector3.zero);

        var cellVisualRoots = new List<GameObject>();
        var cellAnchors = new List<Transform>();
        for (int row = 0; row < designer.gridRows; row++)
        {
            for (int col = 0; col < designer.gridCols; col++)
            {
                int index = row * designer.gridCols + col;
                float x = -16f + (col - 1.5f) * 1.2f;
                float y = 1.2f + row * 1.2f;

                var cell = NewChild(mounts.transform, "CELL_" + index.ToString("00"), new Vector3(x, y, -11.4f), new Vector3(1.1f, 1.1f, 0.15f));
                cell.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
                cell.AddComponent<MeshRenderer>();
                cell.GetComponent<MeshRenderer>().sharedMaterial = GetMat("MAT_HullTrim");
                var colBox = cell.AddComponent<BoxCollider>();
                colBox.size = new Vector3(1f, 1f, 1f);

                var mount = AddUdon<ModuleMount>(cell, SyncType.None);
                mount.cellIndex = index;

                for (int m = 0; m < 6; m++)
                {
                    var vis = NewChild(cell.transform, "MODULE_" + m.ToString("00"), Vector3.zero, new Vector3(0.95f, 0.95f, 0.5f));
                    vis.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
                    vis.AddComponent<MeshRenderer>();
                    vis.GetComponent<MeshRenderer>().sharedMaterial = GetMat("MAT_Module" + m);
                    vis.SetActive(false);
                }

                var anchor = NewChild(mounts.transform, "ANCHOR_" + index.ToString("00"), new Vector3(x, y, -11.2f), new Vector3(0.08f, 0.08f, 0.08f));
                anchor.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
                anchor.AddComponent<MeshRenderer>();
                anchor.GetComponent<MeshRenderer>().sharedMaterial = GetMat("MAT_EmissiveGreen");
                anchor.SetActive(false);

                cellVisualRoots.Add(cell);
                cellAnchors.Add(anchor.transform);
            }
        }

        designer.cellVisualRoots = cellVisualRoots.ToArray();
        designer.cellAnchors = cellAnchors.ToArray();
    }

    static GameObject _spawnPoint;

    static void BuildSpawn(GameObject root)
    {
        _spawnPoint = NewChild(_root, "SPAWN_POINT", new Vector3(0f, 0f, 6f), Quaternion.identity);
    }

    static void BuildLights()
    {
        var sunGo = new GameObject("DIRECTIONAL_LIGHT");
        sunGo.transform.SetParent(_root, false);
        sunGo.transform.localPosition = new Vector3(0f, 8f, 2f);
        sunGo.transform.localRotation = Quaternion.Euler(45f, -35f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.75f;
        sun.color = new Color(0.9f, 0.95f, 1f);

        var deckLights = NewChild(_root, "DECK_LIGHTS", Vector3.zero);
        var positions = new[]
        {
            new Vector3(-10f, 5.5f, 8f), new Vector3(10f, 5.5f, 8f),
            new Vector3(-10f, 5.5f, -4f), new Vector3(10f, 5.5f, -4f)
        };
        for (int i = 0; i < positions.Length; i++)
        {
            var lg = NewChild(deckLights.transform, "DECK_LIGHT_" + i, positions[i]);
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.85f, 0.9f, 1f);
            light.range = 14f;
            light.intensity = 1.2f;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.25f, 0.28f, 0.3f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.05f, 0.08f, 0.1f);
        RenderSettings.fogDensity = 0.012f;
    }

    // ============================================================
    // STATION UI HELPERS
    // ============================================================
    static GameObject BuildStationUICanvas(Transform parent, string stationName, StationController station)
    {
        var canvasGo = MakeWorldCanvas(parent, "STATION_UI", new Vector3(0f, 1.7f, 0.4f), Quaternion.identity, new Vector2(480f, 240f), 0.0025f).gameObject;
        var canvas = canvasGo.GetComponent<Canvas>();

        MakeUIImage(canvas.transform, "UI_BG", Vector2.zero, new Vector2(480f, 240f), new Color(0.03f, 0.04f, 0.06f, 0.9f));
        var nameText = MakeUIText(canvas.transform, "STATION_NAME", stationName, new Vector2(0f, 90f), new Vector2(460f, 34f), 28, TextAnchor.MiddleCenter, Color.cyan);
        var opText = MakeUIText(canvas.transform, "OPERATOR", "", new Vector2(0f, 55f), new Vector2(460f, 24f), 20, TextAnchor.MiddleCenter, Color.white);
        var statusText = MakeUIText(canvas.transform, "STATUS", "STANDBY", new Vector2(0f, 20f), new Vector2(460f, 24f), 20, TextAnchor.MiddleCenter, Color.green);
        var indicator = MakeUIImage(canvas.transform, "STATUS_INDICATOR", new Vector2(-210f, 20f), new Vector2(24f, 24f), new Color(0.5f, 0.5f, 0.5f));
        var scoreText = MakeUIText(canvas.transform, "SCORE", "SCORE: 0", new Vector2(0f, -30f), new Vector2(460f, 26f), 22, TextAnchor.MiddleCenter, Color.yellow);
        var timerText = MakeUIText(canvas.transform, "TIMER", "TIME: 0", new Vector2(0f, -62f), new Vector2(460f, 24f), 18, TextAnchor.MiddleCenter, Color.white);

        var slider = MakeUISlider(canvas.transform, "PROGRESS_BAR", new Vector2(0f, -95f), new Vector2(420f, 18f), 0f, 100f, 0f);

        var controllerGo = canvasGo;
        var controller = AddUdon<StationUIController>(controllerGo, SyncType.None);
        controller.stationNameText = nameText;
        controller.operatorText = opText;
        controller.statusText = statusText;
        controller.statusIndicator = indicator;
        controller.scoreText = scoreText;
        controller.timerText = timerText;
        controller.progressBar = slider;
        controller.station = station;
        station.stationUIController = controller;

        canvasGo.SetActive(false);
        return canvasGo;
    }

    static GameObject BuildLockedUI(Transform parent, string stationName)
    {
        var go = NewChild(parent, "LOCKED_UI", new Vector3(0f, 1.9f, 0.6f));
        MakeTextMesh(go.transform, "LOCKED_TEXT", "STATION LOCKED\nRANK INSUFFICIENT", Vector3.zero, 0.18f, TextAnchor.MiddleCenter, new Color(1f, 0.3f, 0.3f));
        go.SetActive(false);
        return go;
    }

    static void BuildClassifyButtons(Transform parent, ScienceStation science)
    {
        var labels = new[] { "ORGANIC", "MINERAL", "ENERGY", "UNKNOWN", "HAZARDOUS" };
        var methods = new[] { "ClassifyOrganic", "ClassifyMineral", "ClassifyEnergy", "ClassifyUnknown", "ClassifyHazardous" };

        var canvas = MakeWorldCanvas(parent, "SCIENCE_BUTTONS", new Vector3(0f, 1.15f, 1f), Quaternion.identity, new Vector2(520f, 90f), 0.0025f);
        for (int i = 0; i < labels.Length; i++)
        {
            float x = -208f + i * 104f;
            MakeButton(canvas.transform, "BTN_" + methods[i], new Vector2(x, 0f), new Vector2(96f, 70f), labels[i], science, methods[i]);
        }
    }

    static void BuildCommsButtons(Transform parent, CommunicationsStation comms)
    {
        var canvas = MakeWorldCanvas(parent, "COMMS_BUTTONS", new Vector3(0f, 1.55f, 1f), Quaternion.identity, new Vector2(440f, 220f), 0.0025f);
        MakeUIImage(canvas.transform, "CM_BG", Vector2.zero, new Vector2(440f, 220f), new Color(0.03f, 0.04f, 0.06f, 0.85f));

        for (int i = 0; i < 4; i++)
        {
            float x = -165f + i * 110f;
            MakeButton(canvas.transform, "BTN_PAD_" + i, new Vector2(x, 70f), new Vector2(96f, 50f), "SIGNAL " + i, comms, "PressPad" + i);
        }
        for (int i = 0; i < 5; i++)
        {
            float x = -198f + i * 99f;
            MakeButton(canvas.transform, "BTN_CH_" + i, new Vector2(x, -10f), new Vector2(88f, 44f), "CH" + i, comms, "SendChannel" + i);
        }
        MakeButton(canvas.transform, "BTN_DONE", new Vector2(0f, -85f), new Vector2(200f, 44f), "END ROUND", comms, "EndGame");
    }

    static Transform BuildEngineeringSlider(Transform parent, string name, float xPos)
    {
        var go = NewChild(parent, name, new Vector3(xPos, 0f, 0f));
        AddBox(go.transform, "TRACK", Vector3.zero, new Vector3(1.3f, 0.05f, 0.05f), "MAT_HullTrim");
        var thumb = AddBox(go.transform, "THUMB", Vector3.zero, new Vector3(0.14f, 0.12f, 0.12f), "MAT_EmissiveGreen");
        var pickup = thumb.AddComponent<VRC.SDK3.Components.VRCPickup>();
        pickup.pickupable = true;
        pickup.proximity = 0.2f;
        var rb = thumb.GetComponent<Rigidbody>();
        if (rb == null)
            rb = thumb.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        return thumb.transform;
    }

    // ============================================================
    // GENERIC HELPERS
    // ============================================================
    static void EnsureUdonSharpProgramAssets()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!scriptPath.StartsWith("Assets/_Cybership") || !scriptPath.EndsWith(".cs"))
                continue;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
                continue;

            System.Type classType = script.GetClass();
            if (classType == null || classType == typeof(UdonSharpBehaviour) || classType.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(classType))
                continue;

            string assetPath = scriptPath.Substring(0, scriptPath.Length - 3) + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                continue;

            UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = script;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            Debug.Log("[SceneBuilder] Created U# program asset: " + assetPath);
        }
        AssetDatabase.SaveAssets();
    }

    static void ResetUdonSharpCaches()
    {
        System.Reflection.MethodInfo clear = typeof(UdonSharpProgramAsset).GetMethod("ClearProgramAssetCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (clear != null)
            clear.Invoke(null, null);

        System.Reflection.MethodInfo reset = typeof(UdonSharpEditorUtility).GetMethod("ResetCaches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (reset != null)
            reset.Invoke(null, null);
    }

    static T AddUdon<T>(GameObject go, SyncType sync) where T : UdonSharpBehaviour
    {
        var proxy = UdonSharpUndo.AddComponent<T>(go);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        if (backing != null)
            backing.SyncMethod = sync;
        Proxies.Add(proxy);
        return proxy;
    }

    static GameObject NewChild(Transform parent, string name, Vector3 pos)
    {
        return NewChild(parent, name, pos, Quaternion.identity);
    }

    static GameObject NewChild(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var go = NewChild(parent, name, pos, Quaternion.identity);
        go.transform.localScale = scale;
        return go;
    }

    static GameObject NewChild(Transform parent, string name, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        return go;
    }

    static Mesh _cubeMesh;
    static Mesh CubeMesh
    {
        get
        {
            if (_cubeMesh == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cubeMesh = go.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(go);
            }
            return _cubeMesh;
        }
    }

    static GameObject AddBox(Transform parent, string name, Vector3 pos, Vector3 scale, string matKey)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (matKey != null)
            go.GetComponent<Renderer>().sharedMaterial = GetMat(matKey);
        return go;
    }

    static GameObject AddBoxMaterial(GameObject go, string matKey)
    {
        if (go.GetComponent<MeshFilter>() == null)
            go.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
        if (go.GetComponent<MeshRenderer>() == null)
            go.AddComponent<MeshRenderer>();
        go.GetComponent<MeshRenderer>().sharedMaterial = GetMat(matKey);
        return go;
    }

    static GameObject AddSphere(Transform parent, string name, Vector3 pos, float radius, string matKey)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * radius;
        go.GetComponent<Renderer>().sharedMaterial = GetMat(matKey);
        return go;
    }

    static GameObject AddCylinder(Transform parent, string name, Vector3 pos, Vector3 scale, string matKey)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = GetMat(matKey);
        return go;
    }

    static ParticleSystem MakeParticleSystem(Transform parent, string name, Vector3 pos)
    {
        var go = NewChild(parent, name, pos);
        return go.AddComponent<ParticleSystem>();
    }

    // ============================================================
    // TEXT / UI HELPERS
    // ============================================================
    static Font LoadFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    static TMP_FontAsset LoadTMPFont()
    {
        var tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (tmpFont == null)
            tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        return tmpFont;
    }

    static TextMeshPro MakeTextMesh(Transform parent, string name, string text, Vector3 pos, float charSize, TextAnchor anchor, Color color)
    {
        return MakeTextMesh(parent, name, text, pos, charSize, anchor, color, Quaternion.identity);
    }

    static TextMeshPro MakeTextMesh(Transform parent, string name, string text, Vector3 pos, float charSize, TextAnchor anchor, Color color, Quaternion rot)
    {
        var go = NewChild(parent, name, pos, rot);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _tmpFont;
        tmp.text = text;
        tmp.fontSize = 48 * charSize * 10f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return tmp;
    }

    static Canvas MakeWorldCanvas(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, float scale)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = Vector3.one * scale;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        return canvas;
    }

    static Text MakeUIText(Transform parent, string name, string text, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static Image MakeUIImage(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Button MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, string label, UdonSharpBehaviour target, string method)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = new Color(0.15f, 0.2f, 0.25f, 0.95f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(rt, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        var lt = labelGo.AddComponent<Text>();
        lt.font = _font;
        lt.text = label;
        lt.fontSize = 24;
        lt.color = Color.white;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.horizontalOverflow = HorizontalWrapMode.Overflow;
        lt.verticalOverflow = VerticalWrapMode.Overflow;

        BindButton(btn, target, method);
        return btn;
    }

    static void BindButton(Button btn, UdonSharpBehaviour target, string method)
    {
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        if (backing == null)
        {
            Debug.LogError("[SceneBuilder] No backing UdonBehaviour for " + target + " / " + method);
            return;
        }

        var so = new SerializedObject(btn);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (calls == null)
        {
            Debug.LogError("[SceneBuilder] Could not find onClick serialized property on " + btn.name);
            return;
        }
        calls.arraySize += 1;
        var call = calls.GetArrayElementAtIndex(calls.arraySize - 1);
        call.FindPropertyRelative("m_Target").objectReferenceValue = backing;
        call.FindPropertyRelative("m_MethodName").stringValue = "SendCustomEvent";
        call.FindPropertyRelative("m_Mode").enumValueIndex = 5;
        call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue = method;
        call.FindPropertyRelative("m_CallState").enumValueIndex = 2;
        so.ApplyModifiedProperties();
    }

    static Slider MakeUISlider(Transform parent, string name, Vector2 pos, Vector2 size, float min, float max, float value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 1f);

        var slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(rt, false);
        var fa = fillArea.GetComponent<RectTransform>();
        fa.anchorMin = Vector2.zero;
        fa.anchorMax = Vector2.one;
        fa.offsetMin = new Vector2(10f, 0f);
        fa.offsetMax = new Vector2(-10f, 0f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fa, false);
        var fRt = fill.GetComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero;
        fRt.anchorMax = Vector2.one;
        fRt.offsetMin = Vector2.zero;
        fRt.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0f, 1f, 0.25f, 1f);
        slider.fillRect = fRt;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(rt, false);
        var ha = handleArea.GetComponent<RectTransform>();
        ha.anchorMin = Vector2.zero;
        ha.anchorMax = Vector2.one;
        ha.offsetMin = new Vector2(10f, 0f);
        ha.offsetMax = new Vector2(-10f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(ha, false);
        var hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(30f, 30f);
        handle.GetComponent<Image>().color = Color.white;
        slider.handleRect = hRt;
        slider.targetGraphic = handle.GetComponent<Image>();

        return slider;
    }

    // ============================================================
    // TACTICAL TARGET PREFAB
    // ============================================================
    static GameObject _targetPrefab;

    static GameObject BuildTacticalTargetPrefab()
    {
        if (_targetPrefab != null) return _targetPrefab;

        var go = new GameObject("TACTICAL_TARGET");
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Mesh";
        sphere.transform.SetParent(go.transform, false);
        sphere.transform.localScale = Vector3.one * 0.5f;
        sphere.GetComponent<Renderer>().sharedMaterial = GetMat("MAT_EmissiveRed");
        Object.DestroyImmediate(sphere.GetComponent<SphereCollider>());

        var body = go.AddComponent<SphereCollider>();
        body.radius = 0.5f;

        var target = AddUdon<TacticalTarget>(go, SyncType.None);

        string path = PrefabDir + "/TACTICAL_TARGET.prefab";
        _targetPrefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        return _targetPrefab;
    }

    static void RegisterDynamicPrefab(GameObject root)
    {
        if (_targetPrefab == null) return;

        var descriptor = root.GetComponent<VRCSceneDescriptor>();
        if (descriptor == null) return;

        var so = new SerializedObject(descriptor);
        var dynamicPrefabs = so.FindProperty("dynamicPrefabs");
        if (dynamicPrefabs != null)
        {
            dynamicPrefabs.arraySize = 1;
            dynamicPrefabs.GetArrayElementAtIndex(0).objectReferenceValue = _targetPrefab;
            so.ApplyModifiedProperties();
        }
    }

    // ============================================================
    // MATERIALS
    // ============================================================
    static Material GetMat(string key)
    {
        Material m;
        if (!Materials.TryGetValue(key, out m))
        {
            Debug.LogError("[SceneBuilder] Material not found: " + key);
            m = new Material(Shader.Find("Standard"));
            m.color = Color.magenta;
        }
        return m;
    }

    static void CreateMaterials()
    {
        Mat("MAT_Floor", new Color(0.13f, 0.14f, 0.15f));
        Mat("MAT_HullWall", new Color(0.2f, 0.21f, 0.23f));
        Mat("MAT_HullTrim", new Color(0.3f, 0.32f, 0.35f));
        Mat("MAT_Console", new Color(0.08f, 0.09f, 0.12f), 0.1f);
        Mat("MAT_Seat", new Color(0.12f, 0.16f, 0.28f));
        Mat("MAT_Reactor", new Color(0.9f, 0.35f, 0.05f), 2f);
        Mat("MAT_Viewscreen", new Color(0.05f, 0.09f, 0.12f), 0.4f);

        Mat("MAT_EmissiveGreen", new Color(0f, 1f, 0.25f), 2f);
        Mat("MAT_EmissiveCyan", new Color(0f, 1f, 1f), 2f);
        Mat("MAT_EmissiveRed", new Color(1f, 0.2f, 0.2f), 2f);

        Mat("MAT_AlertStrip", new Color(0.9f, 0.9f, 0.95f), 1f);

        Mat("MAT_CommPadIdle", new Color(0.1f, 0.1f, 0.12f));
        Mat("MAT_Signal0", new Color(0f, 1f, 0.25f), 1.5f);
        Mat("MAT_Signal1", new Color(1f, 0.8f, 0f), 1.5f);
        Mat("MAT_Signal2", new Color(1f, 0.2f, 0.2f), 1.5f);
        Mat("MAT_Signal3", new Color(0.4f, 0.3f, 1f), 1.5f);

        Mat("MAT_Sample0", new Color(0.2f, 0.6f, 0.2f));
        Mat("MAT_Sample1", new Color(0.5f, 0.5f, 0.5f));
        Mat("MAT_Sample2", new Color(0.9f, 0.8f, 0.1f));
        Mat("MAT_Sample3", new Color(0.5f, 0.2f, 0.6f));
        Mat("MAT_Sample4", new Color(0.8f, 0.15f, 0.15f));

        Mat("MAT_Module0", new Color(0.45f, 0.47f, 0.5f));
        Mat("MAT_Module1", new Color(0.95f, 0.5f, 0.1f));
        Mat("MAT_Module2", new Color(0.8f, 0.2f, 0.2f));
        Mat("MAT_Module3", new Color(0.2f, 0.7f, 0.8f));
        Mat("MAT_Module4", new Color(0.9f, 0.8f, 0.1f));
        Mat("MAT_Module5", new Color(0.2f, 0.7f, 0.3f));

        Mat("MAT_PathActive", new Color(0f, 1f, 0.25f), 1.5f);
        Mat("MAT_PathError", new Color(1f, 0.2f, 0.2f), 1.5f);
        Mat("MAT_PathDefault", new Color(0.4f, 0.4f, 0.45f));

        Mat("MAT_MAGI_Standby", new Color(0.25f, 0.27f, 0.3f));
        Mat("MAT_MAGI_Processing", new Color(0.2f, 0.5f, 0.9f), 1.5f);
        Mat("MAT_MAGI_Yes", new Color(0f, 1f, 0.25f), 1.5f);
        Mat("MAT_MAGI_No", new Color(1f, 0.2f, 0.2f), 1.5f);
    }

    static void Mat(string key, Color color)
    {
        Mat(key, color, 0f);
    }

    static void Mat(string key, Color color, float emission)
    {
        string path = MatDir + "/" + key + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            Materials[key] = existing;
            return;
        }

        var shader = Shader.Find("Standard");
        var mat = new Material(shader);
        mat.name = key;
        mat.color = color;
        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emission);
        }
        AssetDatabase.CreateAsset(mat, path);
        Materials[key] = mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
