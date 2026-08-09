// ============================================================
// NCV-01 Cybership - Base Station Controller
// Inherit from this for all duty stations.
//
// A station is a VRCStation (seated) that hosts a synced operator
// slot and a minigame. Subclasses override SetupStation() to
// configure their identity, and EnterStation()/ExitStation() to
// control station-specific gameplay.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class StationController : UdonSharpBehaviour
{
    [Header("Station Configuration")]
    public string stationName = "Station";
    public int stationId = 0;
    public int requiredRank = 0;
    public int departmentId = 0;

    [Header("References")]
    public ShipStateManager shipState;
    public PlayerProgressionManager progression;
    public VRCStation vrStation;

    [Header("UI")]
    public GameObject stationUI;
    public GameObject lockedUI;
    public StationUIController stationUIController;

    [Header("State")]
    [UdonSynced]
    protected int _currentOperatorId = -1;

    [UdonSynced]
    private bool _isActive = false;

    [UdonSynced]
    private int _currentScore = 0;

    private VRCPlayerApi _localOperator;
    protected bool _isLocalOperating = false;

    // Non-owner sitters defer their operator claim until ownership arrives.
    private int _pendingOperatorId = -1;

    // ============================================================
    void Start()
    {
        if (stationUI != null) stationUI.SetActive(false);
        if (lockedUI != null) lockedUI.SetActive(false);

        SetupStation();
    }

    // Subclasses override this to set their name / rank / department.
    protected virtual void SetupStation()
    {
    }

    // ============================================================
    // STATION EVENTS
    // ============================================================
    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        // Rank-gating is self-authoritative: each client checks its own rank.
        // The seat is a physical lock, so a locked player is ejected instead of
        // merely denied, otherwise the station would be stuck forever.
        if (player.isLocal && !CanOperate(player))
        {
            ShowLocked(player);
            EjectLocal(player);
            return;
        }

        // The seated operator owns and writes the station model directly.
        // Ownership transfer is async, so the actual claim lands in
        // OnOwnershipTransferred if we are not yet the owner.
        if (player.isLocal)
        {
            if (Networking.IsOwner(gameObject))
            {
                ClaimStation(player.playerId);
            }
            else
            {
                _pendingOperatorId = player.playerId;
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            _isLocalOperating = true;
            _localOperator = player;
            EnterStation();
        }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        if (player.isLocal)
        {
            if (Networking.IsOwner(gameObject))
            {
                if (player.playerId == _currentOperatorId)
                {
                    _currentOperatorId = -1;
                    _isActive = false;
                    _currentScore = 0;
                    RequestSerialization();
                }

                // Return ownership to the master so the seat is neutral for
                // the next operator.
                _pendingOperatorId = -1;
                Networking.SetOwner(Networking.Master, gameObject);
            }
            else
            {
                _pendingOperatorId = -1;
            }

            _isLocalOperating = false;
            ExitStation();
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        base.OnOwnershipTransferred(player);

        if (Networking.IsOwner(gameObject) && _pendingOperatorId != -1)
        {
            ClaimStation(_pendingOperatorId);
            _pendingOperatorId = -1;
        }
    }

    private void ClaimStation(int playerId)
    {
        _currentOperatorId = playerId;
        _isActive = true;
        RequestSerialization();
    }

    private void EjectLocal(VRCPlayerApi player)
    {
        if (vrStation != null)
            vrStation.ExitStation(player);
    }

    public virtual void EnterStation()
    {
        if (stationUI != null) stationUI.SetActive(true);
        if (lockedUI != null) lockedUI.SetActive(false);

        if (stationUIController != null)
            stationUIController.SetStationState(true);
    }

    public virtual void ExitStation()
    {
        if (stationUI != null) stationUI.SetActive(false);

        if (stationUIController != null)
            stationUIController.SetStationState(false);
    }

    // ============================================================
    // RANK GATING
    // ============================================================
    public bool CanOperate(VRCPlayerApi player)
    {
        if (progression == null) return true;
        int playerRank = progression.GetCurrentRank();
        return playerRank >= requiredRank;
    }

    private void ShowLocked(VRCPlayerApi player)
    {
        if (player != null && player.isLocal)
        {
            if (lockedUI != null)
            {
                lockedUI.SetActive(true);
                SendCustomEventDelayedSeconds("HideLocked", 2f);
            }

            if (progression != null && progression.uiManager != null)
                progression.uiManager.ShowNotification("STATION LOCKED - REQUIRES RANK: " +
                    progression.GetRankName(requiredRank));
        }
    }

    public void HideLocked()
    {
        if (lockedUI != null)
            lockedUI.SetActive(false);
    }

    // ============================================================
    // MINIGAME INTERFACE (overridden by subclasses)
    // ============================================================
    public virtual void StartMinigame()
    {
    }

    public virtual void EndMinigame(bool success)
    {
    }

    public virtual void SubmitScore(int score)
    {
        if (score < 0) score = 0;

        // Only the seated operator may submit a score for this station.
        if (!_isLocalOperating) return;

        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        _currentScore = score;
        RequestSerialization();

        if (progression != null)
            progression.AwardStationXP(stationName, score);

        if (stationUIController != null)
            stationUIController.SetScore(score);
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public bool IsActive() { return _isActive; }
    public int GetScore() { return _currentScore; }
    public int GetOperatorId() { return _currentOperatorId; }
    public bool IsLocalOperating() { return _isLocalOperating; }
    public VRCPlayerApi GetLocalOperator() { return _localOperator; }
}
