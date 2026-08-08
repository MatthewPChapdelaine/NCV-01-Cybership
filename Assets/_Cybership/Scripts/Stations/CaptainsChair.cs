// ============================================================
// NCV-01 Cybership - Captain's Chair (Command Authority)
//
// Seating grants the Commander+ (rank 6+) player command authority
// and ship-wide alert controls. The instance master always has
// authority regardless of rank. When the captain departs, the XO
// ascends automatically via ShipStateManager.OnPlayerLeft.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class CaptainsChair : UdonSharpBehaviour
{
    [Header("Chair Settings")]
    public VRCStation chairStation;
    public int requiredRank = 6;

    [Header("Visuals")]
    public Light chairSpotlight;
    public ParticleSystem commandAura;
    public Renderer chairEmissive;
    public Material activeMaterial;
    public Material inactiveMaterial;

    [Header("UI")]
    public GameObject commandUI;
    public GameObject lockedUI;

    [Header("References")]
    public ShipStateManager shipState;
    public PlayerProgressionManager progression;

    private VRCPlayerApi _deniedPlayer;
    private bool _isOccupied = false;

    // ============================================================
    void Start()
    {
        if (commandUI != null) commandUI.SetActive(false);
        if (lockedUI != null) lockedUI.SetActive(false);

        SetChairActive(false);
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        bool hasRank = false;
        if (player.isMaster)
            hasRank = true;
        else if (progression != null)
            hasRank = progression.GetCurrentRank() >= requiredRank;

        if (player.isLocal && !hasRank)
        {
            _deniedPlayer = player;
            if (lockedUI != null)
                lockedUI.SetActive(true);

            SendCustomEventDelayedSeconds("EjectDeniedPlayer", 0.5f);
            SendCustomEventDelayedSeconds("HideLocked", 2f);

            if (progression != null && progression.uiManager != null)
                progression.uiManager.ShowNotification("CHAIR LOCKED - REQUIRES RANK: " +
                    progression.GetRankName(requiredRank));
            return;
        }

        // Grant command authority.
        if (shipState != null)
            shipState.ClaimCaptain(player);

        if (player.isLocal)
        {
            shipState.SetLocalCaptainAuthority(true);

            if (commandUI != null)
                commandUI.SetActive(true);

            if (progression != null && progression.uiManager != null)
                progression.uiManager.ShowNotification("COMMAND AUTHORITY GRANTED - " + player.displayName);
        }

        SetChairActive(true);
        _isOccupied = true;
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        if (shipState != null)
            shipState.RelinquishCaptain(player);

        if (player.isLocal)
        {
            shipState.SetLocalCaptainAuthority(false);

            if (commandUI != null)
                commandUI.SetActive(false);
        }

        if (player.isLocal || !_isOccupied)
        {
            SetChairActive(false);
            _isOccupied = false;
        }
    }

    public void EjectDeniedPlayer()
    {
        if (_deniedPlayer == null) return;

        if (chairStation != null)
            chairStation.ExitStation(_deniedPlayer);

        _deniedPlayer = null;
    }

    public void HideLocked()
    {
        if (lockedUI != null)
            lockedUI.SetActive(false);
    }

    // ============================================================
    // COMMAND ACTIONS
    // ============================================================
    public void SetAlertGreen() { if (shipState != null) shipState.SetAlertLevel(0); }
    public void SetAlertYellow() { if (shipState != null) shipState.SetAlertLevel(1); }
    public void SetAlertRed() { if (shipState != null) shipState.SetAlertLevel(2); }
    public void SetAlertBlack() { if (shipState != null) shipState.SetAlertLevel(3); }

    public void RelinquishCommand()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (chairStation != null && Utilities.IsValid(local))
            chairStation.ExitStation(local);
    }

    // ============================================================
    // VISUALS
    // ============================================================
    private void SetChairActive(bool active)
    {
        if (chairSpotlight != null)
            chairSpotlight.enabled = active;

        if (commandAura != null)
        {
            if (active) commandAura.Play();
            else commandAura.Stop();
        }

        if (chairEmissive != null)
            chairEmissive.material = active ? activeMaterial : inactiveMaterial;
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public bool IsOccupied() { return _isOccupied; }
}
