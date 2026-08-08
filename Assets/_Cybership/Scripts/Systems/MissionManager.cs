// ============================================================
// NCV-01 Cybership - SMEAC Mission System
//
// Mission lifecycle (Situation, Mission, Execution, Admin, Command):
//   - Master starts a mission; alert level rises with difficulty.
//   - Progress is driven by activity at the synced duty stations.
//   - On completion the result is broadcast to every client, and
//     each crew member is awarded XP locally from PlayerData.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class MissionManager : UdonSharpBehaviour
{
    [Header("Mission Configuration")]
    public string[] missionTypes = new string[]
    {
        "Cargo Transport",
        "System Survey",
        "Emergency Response",
        "Diplomatic Envoy",
        "Combat Patrol"
    };

    public string[] difficultyLevels = new string[]
    {
        "Routine",
        "Standard",
        "Difficult",
        "Extreme"
    };

    [Header("References")]
    public ShipStateManager shipState;
    public PlayerProgressionManager progression;
    public StationController[] stations;
    public HUDManager hudManager;

    [Header("Timing")]
    public float baseMissionTime = 300f;
    public float missionScoreRate = 250f; // avg station score per second = 100% progress

    [UdonSynced]
    private bool _missionActive = false;

    [UdonSynced]
    private int _currentMissionType = 0;

    [UdonSynced]
    private int _missionDifficulty = 0;

    [UdonSynced]
    private float _missionTimer = 0f;

    [UdonSynced]
    private float _missionProgress = 0f;

    private float _syncTimer = 0f;

    // ============================================================
    void Update()
    {
        if (!_missionActive || !Networking.IsMaster) return;

        _missionTimer -= Time.deltaTime;
        UpdateMissionProgress();

        // Throttle syncing the countdown/progress to ~4Hz instead of every frame.
        _syncTimer -= Time.deltaTime;
        if (_syncTimer <= 0f)
        {
            _syncTimer = 0.25f;
            RequestSerialization();
        }

        if (_missionTimer <= 0f)
        {
            EndMission(false);
        }
        else if (_missionProgress >= 100f)
        {
            EndMission(true);
        }
    }
    // ============================================================
    // MISSION CONTROL
    // ============================================================
    public void StartMission(int type, int difficulty)
    {
        if (!Networking.IsMaster) return;
        if (_missionActive) return;
        if (type < 0 || type >= missionTypes.Length) return;
        if (difficulty < 0 || difficulty >= difficultyLevels.Length) return;

        _currentMissionType = type;
        _missionDifficulty = difficulty;
        _missionActive = true;
        _missionProgress = 0f;

        float[] timeMultipliers = { 1f, 1.2f, 1.5f, 2f };
        _missionTimer = baseMissionTime * timeMultipliers[difficulty];

        RequestSerialization();

        if (shipState != null)
        {
            shipState.SetMissionActive(true);
            shipState.SetAlertLevel(difficulty >= 2 ? 2 : 1);
        }

        if (hudManager != null)
            hudManager.ShowNotification("MISSION START: " + GetCurrentMissionName());
    }

    public void StartRandomMission()
    {
        int type = Random.Range(0, missionTypes.Length);
        int difficulty = Random.Range(0, difficultyLevels.Length);
        StartMission(type, difficulty);
    }

    public void EndMission(bool success)
    {
        if (!Networking.IsMaster) return;

        _missionActive = false;
        _missionProgress = 0f;
        RequestSerialization();

        if (shipState != null)
        {
            shipState.SetMissionActive(false);
            shipState.SetAlertLevel(0);
        }

        if (hudManager != null)
            hudManager.ShowNotification(success ? "MISSION COMPLETE" : "MISSION FAILED");

        // Every client awards XP to its own local player.
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All,
            success ? "OnMissionSuccess" : "OnMissionFailed");
    }

    // Runs on every client when the mission ends.
    public void OnMissionSuccess()
    {
        if (progression != null)
            progression.AwardMissionXP(true, _missionDifficulty + 1);
    }

    public void OnMissionFailed()
    {
        if (progression != null)
            progression.AwardMissionXP(false, _missionDifficulty + 1);
    }

    // ============================================================
    // PROGRESS
    // ============================================================
    private void UpdateMissionProgress()
    {
        float totalScore = 0f;
        int activeStations = 0;

        if (stations != null)
        {
            foreach (var station in stations)
            {
                if (station != null && station.IsActive())
                {
                    totalScore += station.GetScore();
                    activeStations++;
                }
            }
        }

        if (activeStations > 0)
        {
            float avgScore = totalScore / activeStations;
            _missionProgress += (avgScore * Time.deltaTime) / missionScoreRate;
            _missionProgress = Mathf.Min(_missionProgress, 100f);
        }
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public bool IsMissionActive() { return _missionActive; }
    public float GetMissionProgress() { return _missionProgress; }
    public float GetMissionTimeRemaining() { return _missionTimer; }

    public string GetCurrentMissionName()
    {
        return difficultyLevels[_missionDifficulty] + " " + missionTypes[_currentMissionType];
    }

    public int GetMissionDifficulty() { return _missionDifficulty; }
}
