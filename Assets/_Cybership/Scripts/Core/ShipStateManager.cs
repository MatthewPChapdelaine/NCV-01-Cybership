// ============================================================
// NCV-01 Cybership - Global State Controller
// Attach to: Empty GameObject "SHIP_STATE_MANAGER"
// Requires: UdonBehaviour (UdonSharp) with Synchronization = Manual
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class ShipStateManager : UdonSharpBehaviour
{
    [Header("Synced Global State")]
    [UdonSynced, FieldChangeCallback(nameof(AlertLevel))]
    private int _alertLevel = 0; // 0=Green, 1=Yellow, 2=Red, 3=Black

    [UdonSynced]
    private float _reactorOutput = 50f; // 0-100%

    [UdonSynced]
    private float _reactorTemperature = 25f; // Celsius, critical at 150

    [UdonSynced]
    private int _captainPlayerId = -1;

    [UdonSynced]
    private int _executiveOfficerId = -1;

    [UdonSynced]
    private bool _missionActive = false;

    [UdonSynced]
    private int _shipReputation = 100; // 0-200

    [Header("Visual / Audio References")]
    public AudioSource alertAudioSource;
    public AudioClip[] alertSounds; // 0=green, 1=yellow, 2=red, 3=black
    public Light[] emergencyLights; // enabled while level >= 2
    public Renderer[] alertSurfaces; // emissive surfaces tinted by alert level

    [Header("Notification Target")]
    public HUDManager hudManager;

    [Header("Reactor Simulation")]
    public float tempRiseRate = 2f;
    public float tempCoolRate = 1f;
    public float reactorCriticalTemp = 150f;

    private const float REACTOR_MAX_TEMP = 200f;

    // Set by CaptainsChair when the LOCAL player occupies the chair.
    // Bridges the window between claiming command and synced deserialization.
    private bool _localCaptainAuthority = false;

    // ============================================================
    // PUBLIC READONLY ACCESS
    // ============================================================
    public int AlertLevel
    {
        get { return _alertLevel; }
        set
        {
            _alertLevel = value;
            ApplyAlertLevel(value);
        }
    }
    public float ReactorOutput { get { return _reactorOutput; } }
    public float ReactorTemperature { get { return _reactorTemperature; } }
    public int CaptainPlayerId { get { return _captainPlayerId; } }
    public int ExecutiveOfficerId { get { return _executiveOfficerId; } }
    public bool MissionActive { get { return _missionActive; } }
    public int ShipReputation { get { return _shipReputation; } }

    // ============================================================
    void Start()
    {
        if (Networking.IsMaster)
        {
            _alertLevel = 0;
            _reactorOutput = 50f;
            _reactorTemperature = 25f;
            _shipReputation = 100;
            RequestSerialization();
        }

        ApplyAlertLevel(_alertLevel);
    }

    void Update()
    {
        if (!Networking.IsMaster) return;

        // ---- Reactor temperature simulation ----
        float targetTemp = 25f + (_reactorOutput * 1.5f);

        if (_reactorTemperature < targetTemp)
            _reactorTemperature += tempRiseRate * Time.deltaTime;
        else if (_reactorTemperature > targetTemp)
            _reactorTemperature -= tempCoolRate * Time.deltaTime;

        _reactorTemperature = Mathf.Clamp(_reactorTemperature, 0f, REACTOR_MAX_TEMP);

        // ---- Critical temperature check ----
        if (_reactorTemperature >= reactorCriticalTemp && _alertLevel < 2)
        {
            SetAlertLevel(2);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "OnReactorCritical");
        }
    }

    // ============================================================
    // ALERT LEVEL MANAGEMENT
    // ============================================================
    public void SetAlertLevel(int level)
    {
        if (level < 0 || level > 3) return;

        // This object is host-owned, so non-owners can't write synced fields
        // directly. Forward standard alerts to the host as named events.
        if (!Networking.IsMaster)
        {
            // Condition Black stays host-only: a relayed level 3 would lose the
            // sender's identity that the captain gate depends on.
            if (level >= 3) return;

            switch (level)
            {
                case 0:
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Master, "OnAlertLevelRemote0");
                    break;
                case 1:
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Master, "OnAlertLevelRemote1");
                    break;
                case 2:
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Master, "OnAlertLevelRemote2");
                    break;
            }
            return;
        }

        // Any crew member may raise/lower standard alerts (stations use this
        // for gameplay, e.g. tactical raising Condition Yellow). Condition
        // Black is restricted to the host or the local captain.
        bool canSet = level < 3 || IsLocalCaptain() || _localCaptainAuthority;
        if (!canSet) return;

        AlertLevel = level;
        RequestSerialization();
    }

    // Host-side handlers for relayed alert requests from remote clients.
    public void OnAlertLevelRemote0() { if (Networking.IsMaster) SetAlertLevel(0); }
    public void OnAlertLevelRemote1() { if (Networking.IsMaster) SetAlertLevel(1); }
    public void OnAlertLevelRemote2() { if (Networking.IsMaster) SetAlertLevel(2); }

    private void ApplyAlertLevel(int level)
    {
        if (level < 0 || level > 3) level = 0;

        Color alertColor = GetAlertColor(level);

        if (alertSurfaces != null)
        {
            foreach (var surface in alertSurfaces)
            {
                if (surface != null && surface.material != null)
                    surface.material.SetColor("_EmissionColor", alertColor * 2f);
            }
        }

        if (emergencyLights != null)
        {
            foreach (var light in emergencyLights)
            {
                if (light != null)
                    light.enabled = (level >= 2);
            }
        }

        if (alertAudioSource != null && alertSounds != null && level < alertSounds.Length)
        {
            AudioClip clip = alertSounds[level];
            if (clip != null)
            {
                alertAudioSource.clip = clip;
                alertAudioSource.Play();
            }
        }

        if (hudManager != null)
            hudManager.OnAlertLevelChanged(level);
    }

    private Color GetAlertColor(int level)
    {
        if (level == 0) return new Color(0f, 1f, 0.25f);    // Green
        if (level == 1) return new Color(1f, 0.8f, 0f);     // Yellow
        if (level == 2) return new Color(1f, 0.2f, 0.2f);   // Red
        if (level == 3) return new Color(0.8f, 0f, 1f);     // Black / Purple
        return Color.white;
    }

    public void OnReactorCritical()
    {
        if (hudManager != null)
            hudManager.ShowNotification("CRITICAL: REACTOR OVERHEAT DETECTED");
    }

    // ============================================================
    // COMMAND AUTHORITY
    // ============================================================
    public void ClaimCaptain(VRCPlayerApi player)
    {
        if (!Networking.IsMaster) return;
        if (!Utilities.IsValid(player)) return;

        // Promote the current captain to XO if one exists.
        if (_captainPlayerId != -1 && _captainPlayerId != player.playerId)
            _executiveOfficerId = _captainPlayerId;

        _captainPlayerId = player.playerId;
        RequestSerialization();
    }

    public void RelinquishCaptain(VRCPlayerApi player)
    {
        if (!Networking.IsMaster) return;
        if (!Utilities.IsValid(player)) return;
        if (_captainPlayerId != player.playerId) return;

        // Auto-promote XO to Captain.
        _captainPlayerId = _executiveOfficerId;
        _executiveOfficerId = -1;

        RequestSerialization();
    }

    public void SetLocalCaptainAuthority(bool value)
    {
        _localCaptainAuthority = value;
    }

    public bool HasLocalCaptainAuthority()
    {
        return _localCaptainAuthority;
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsMaster) return;
        if (!Utilities.IsValid(player)) return;

        if (player.playerId == _captainPlayerId)
        {
            // Captain disconnected - XO ascends.
            _captainPlayerId = _executiveOfficerId;
            _executiveOfficerId = -1;
            RequestSerialization();

            if (hudManager != null)
                hudManager.ShowNotification("CAPTAIN DEPARTED - COMMAND PASSED TO XO");
        }
        else if (player.playerId == _executiveOfficerId)
        {
            _executiveOfficerId = -1;
            RequestSerialization();
        }
    }

    // ============================================================
    // REACTOR CONTROLS
    // ============================================================
    // Continuous values are relayed from the (operator-owned) Engineering
    // Station via its synced _desiredOutput field; only the host writes here.
    public void SetReactorOutput(float output)
    {
        if (!Networking.IsMaster) return;

        _reactorOutput = Mathf.Clamp(output, 0f, 100f);
        RequestSerialization();
    }

    // ============================================================
    // REPUTATION
    // ============================================================
    public void ModifyReputation(int delta)
    {
        if (!Networking.IsMaster) return;

        _shipReputation = Mathf.Clamp(_shipReputation + delta, 0, 200);
        RequestSerialization();
    }

    // ============================================================
    // MISSION STATE
    // ============================================================
    public void SetMissionActive(bool active)
    {
        if (!Networking.IsMaster) return;

        _missionActive = active;
        RequestSerialization();
    }

    // ============================================================
    // UTILITY
    // ============================================================
    public bool IsCaptain(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return false;
        return player.playerId == _captainPlayerId;
    }

    public bool IsCommandStaff(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return false;
        return player.playerId == _captainPlayerId || player.playerId == _executiveOfficerId;
    }

    public bool IsLocalCaptain()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return false;
        return local.playerId == _captainPlayerId;
    }

    public bool IsLocalCommandStaff()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return false;
        return local.playerId == _captainPlayerId || local.playerId == _executiveOfficerId;
    }
}
