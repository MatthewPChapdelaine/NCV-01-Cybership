// ============================================================
// NCV-01 Cybership - Watch Schedule Manager
// Attach to: Empty GameObject "WATCH_MANAGER"
//
// Real-time watch rotation driven by the VRChat server clock.
// Each player is assigned a watch by (playerId % watchCount), and
// earns a bonus XP when their assigned watch is on shift.
//
// Synced state: _watchStartTime (server time when the rotation began).
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class WatchScheduleManager : UdonSharpBehaviour
{
    [Header("Watch Configuration")]
    public string[] WATCH_NAMES = new string[] { "ALPHA", "BRAVO", "CHARLIE", "DELTA" };
    public float watchDuration = 3600f; // seconds per shift (1 hour default)

    [Header("References")]
    public PlayerProgressionManager progression;

    [Header("Bonuses")]
    public int watchBonusXP = 25;

    [UdonSynced]
    private float _watchStartTime = 0f;

    private int _lastBonusWatch = -1;

    // ============================================================
    void Start()
    {
        if (Networking.IsMaster)
        {
            _watchStartTime = (float)Networking.GetServerTimeInSeconds();
            RequestSerialization();
        }

        _lastBonusWatch = -1;
    }

    void Update()
    {
        if (!Networking.IsNetworkSettled) return;

        int currentWatch = GetCurrentWatchIndex();

        // Award the watch bonus once per shift when the local player is on duty.
        if (IsLocalPlayerAssignedToWatch(currentWatch) && _lastBonusWatch != currentWatch)
        {
            _lastBonusWatch = currentWatch;

            if (progression != null)
                progression.AwardXP(watchBonusXP, "WATCH DUTY BONUS");
        }
    }

    // ============================================================
    // WATCH MATH
    // ============================================================
    public int GetCurrentWatchIndex()
    {
        if (WATCH_NAMES == null || WATCH_NAMES.Length == 0) return 0;

        float now = (float)Networking.GetServerTimeInSeconds();
        float elapsed = now - _watchStartTime;

        float duration = GetWatchDuration();
        int index = (int)(elapsed / duration) % WATCH_NAMES.Length;
        if (index < 0) index += WATCH_NAMES.Length;
        return index;
    }

    public string GetCurrentWatchName()
    {
        if (WATCH_NAMES == null || WATCH_NAMES.Length == 0) return "UNASSIGNED";
        int index = GetCurrentWatchIndex();
        if (index < 0 || index >= WATCH_NAMES.Length) return "UNASSIGNED";
        return WATCH_NAMES[index];
    }

    public float GetWatchTimeRemaining()
    {
        float now = (float)Networking.GetServerTimeInSeconds();
        float elapsed = now - _watchStartTime;
        float duration = GetWatchDuration();
        float progress = elapsed % duration;
        if (progress < 0f) progress += duration;
        return duration - progress;
    }

    // Guard against an unconfigured/zero shift length.
    private float GetWatchDuration()
    {
        return Mathf.Max(watchDuration, 1f);
    }

    // A player is assigned to the watch at (playerId % watchCount).
    public int GetLocalWatchAssignment()
    {
        if (WATCH_NAMES == null || WATCH_NAMES.Length == 0) return 0;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return 0;
        return local.playerId % WATCH_NAMES.Length;
    }

    public string GetLocalWatchName()
    {
        int index = GetLocalWatchAssignment();
        if (index < 0 || index >= WATCH_NAMES.Length) return "UNASSIGNED";
        return WATCH_NAMES[index];
    }

    public bool IsLocalPlayerAssignedToWatch(int watchIndex)
    {
        return GetLocalWatchAssignment() == watchIndex;
    }

    public bool IsLocalOnActiveWatch()
    {
        return IsLocalPlayerAssignedToWatch(GetCurrentWatchIndex());
    }

    public string GetWatchName(int index)
    {
        if (index < 0 || index >= WATCH_NAMES.Length) return "UNASSIGNED";
        return WATCH_NAMES[index];
    }
}
