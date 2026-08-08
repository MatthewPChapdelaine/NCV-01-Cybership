// ============================================================
// NCV-01 Cybership - Player Rank & XP System
// Attach to: Empty GameObject "PLAYER_MANAGER"
//
// Uses the VRChat PlayerData persistence API (SDK 3.7+).
// PlayerData is only safe to read/write after OnPlayerRestored.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

public class PlayerProgressionManager : UdonSharpBehaviour
{
    [Header("Rank Definitions")]
    public string[] RANK_NAMES = new string[]
    {
        "Recruit",          // 0
        "Private",          // 1
        "Corporal",         // 2
        "Sergeant",         // 3
        "Staff Sergeant",   // 4
        "Lieutenant",       // 5
        "Commander",        // 6
        "Captain"           // 7 - Only via the Captain's chair
    };

    public int[] RANK_XP_THRESHOLDS = new int[]
    {
        0,       // Recruit
        100,     // Private
        300,     // Corporal
        600,     // Sergeant
        1000,    // Staff Sergeant
        1500,    // Lieutenant
        2500,    // Commander
        999999   // Captain (special, not reachable by XP alone)
    };

    [Header("Department Assignments")]
    public string[] DEPARTMENTS = new string[]
    {
        "Unassigned",
        "Command",
        "Operations",
        "Tactical",
        "Engineering",
        "Science"
    };

    [Header("References")]
    public HUDManager uiManager;

    // PlayerData keys (scoped to this world automatically).
    private const string KEY_XP = "cybership_xp";
    private const string KEY_DEPT = "cybership_dept";
    private const string KEY_MISSIONS = "cybership_missions";

    // Local player state.
    private int _localXP = 0;
    private int _localDepartment = 0;
    private int _localMissionsCompleted = 0;
    private bool _dataReady = false;

    // ============================================================
    void Start()
    {
        // Wait for OnPlayerRestored before touching PlayerData.
    }

    public void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player != null && player.isLocal)
        {
            LoadPlayerData();
            _dataReady = true;

            if (uiManager != null)
                uiManager.RefreshHUD();
        }
    }

    public bool IsDataReady() { return _dataReady; }

    // ============================================================
    // XP & RANKING
    // ============================================================
    public void AwardXP(int amount, string reason)
    {
        if (!_dataReady) return;

        int oldRank = GetRankForXP(_localXP);
        _localXP += amount;
        if (_localXP < 0) _localXP = 0;
        int newRank = GetRankForXP(_localXP);

        SavePlayerData();

        if (newRank > oldRank)
        {
            if (uiManager != null)
                uiManager.ShowNotification("PROMOTION: " + RANK_NAMES[newRank]);
        }
        else if (uiManager != null)
        {
            uiManager.ShowNotification("+" + amount + " XP - " + reason);
        }
    }

    public void AwardMissionXP(bool success, int difficulty)
    {
        // Mission-end events can arrive before OnPlayerRestored - guard before
        // touching PlayerData so we never overwrite stored values with zeros.
        if (!_dataReady) return;

        int baseXP = success ? 50 : 10;
        int totalXP = baseXP * difficulty;

        if (success)
            _localMissionsCompleted++;

        SavePlayerData();

        AwardXP(totalXP, success ? "MISSION COMPLETE" : "MISSION ATTEMPT");
    }

    public void AwardStationXP(string stationName, int score)
    {
        int xp = score / 10;
        if (xp < 1) xp = 1;
        AwardXP(xp, stationName + " STATION");
    }

    private int GetRankForXP(int xp)
    {
        for (int i = RANK_XP_THRESHOLDS.Length - 1; i >= 0; i--)
        {
            if (xp >= RANK_XP_THRESHOLDS[i])
                return i;
        }
        return 0;
    }

    // ============================================================
    // DEPARTMENT ASSIGNMENT
    // ============================================================
    public void SetDepartment(int departmentId)
    {
        if (departmentId < 0 || departmentId >= DEPARTMENTS.Length) return;

        _localDepartment = departmentId;
        SavePlayerData();

        if (uiManager != null)
        {
            uiManager.RefreshHUD();
            uiManager.ShowNotification("DEPARTMENT: " + DEPARTMENTS[_localDepartment]);
        }
    }

    // ============================================================
    // DATA PERSISTENCE
    // ============================================================
    private void LoadPlayerData()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;

        _localXP = PlayerData.GetInt(local, KEY_XP);
        _localDepartment = PlayerData.GetInt(local, KEY_DEPT);
        _localMissionsCompleted = PlayerData.GetInt(local, KEY_MISSIONS);

        if (_localDepartment < 0 || _localDepartment >= DEPARTMENTS.Length)
            _localDepartment = 0;
    }

    private void SavePlayerData()
    {
        PlayerData.SetInt(KEY_XP, _localXP);
        PlayerData.SetInt(KEY_DEPT, _localDepartment);
        PlayerData.SetInt(KEY_MISSIONS, _localMissionsCompleted);
    }

    // ============================================================
    // PUBLIC ACCESSORS
    // ============================================================
    public int GetCurrentXP() { return _localXP; }

    public int GetCurrentRank() { return GetRankForXP(_localXP); }

    public string GetCurrentRankName()
    {
        int rank = GetRankForXP(_localXP);
        if (rank < 0 || rank >= RANK_NAMES.Length) return RANK_NAMES[0];
        return RANK_NAMES[rank];
    }

    public int GetDepartment() { return _localDepartment; }

    public string GetDepartmentName()
    {
        if (_localDepartment < 0 || _localDepartment >= DEPARTMENTS.Length)
            return DEPARTMENTS[0];
        return DEPARTMENTS[_localDepartment];
    }

    public int GetMissionsCompleted() { return _localMissionsCompleted; }

    public int GetXPToNextRank()
    {
        int currentRank = GetRankForXP(_localXP);
        if (currentRank >= RANK_XP_THRESHOLDS.Length - 1) return 0;
        return RANK_XP_THRESHOLDS[currentRank + 1] - _localXP;
    }

    public string GetRankName(int rank)
    {
        if (rank < 0 || rank >= RANK_NAMES.Length) return RANK_NAMES[0];
        return RANK_NAMES[rank];
    }
}
