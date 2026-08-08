// ============================================================
// NCV-01 Cybership - Communications Station (Signal Relay)
//
// Two sub-systems:
//   1. SIGNAL DECODE - Simon-style memory game. Watch the signal
//      sequence play back, then repeat it on the console pads.
//   2. INTER-SHIP RELAY - synced message board shared with the crew.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class CommunicationsStation : StationController
{
    [Header("Signal Pads")]
    public Renderer[] padRenderers;       // visual pad feedback (4+)
    public Material[] signalMaterials;    // index-aligned with signal id
    public Material idlePadMaterial;      // pad material while idle
    public Light[] padLights;             // optional per-pad lights
    public int signalCount = 4;           // number of distinct signals

    [Header("Game Settings")]
    public int baseSequenceLength = 3;
    public int maxSequenceLength = 8;
    public float signalHoldTime = 0.8f;
    public float signalGapTime = 0.4f;
    public float inputTimeLimit = 10f;

    [Header("Relay Board")]
    public TextMesh relayDisplayText;
    public TextMesh scoreText;

    [Header("Relay Announcements")]
    public string[] relayChannels = new string[]
    {
        "ALL CLEAR",
        "CREW ASSEMBLY",
        "ENGINEERING REPORT",
        "ALERT PROTOCOL",
        "MISSION BRIEFING"
    };

    // Synced relay board.
    [UdonSynced, FieldChangeCallback(nameof(RelaySender))]
    private string _relaySender = "AUTOPILOT";
    [UdonSynced, FieldChangeCallback(nameof(RelayMessage))]
    private string _relayMessage = "WELCOME ABOARD NCV-01";

    public string RelaySender
    {
        get { return _relaySender; }
        set { _relaySender = value; UpdateRelayDisplay(); }
    }

    public string RelayMessage
    {
        get { return _relayMessage; }
        set { _relayMessage = value; UpdateRelayDisplay(); }
    }

    // Game state machine.
    private const int STATE_IDLE = 0;
    private const int STATE_PLAYING = 1;
    private const int STATE_INPUT = 2;
    private const int STATE_RESULT = 3;

    // Buffers for a pending relay post made before ownership arrived.
    private string _pendingRelaySender = null;
    private string _pendingRelayMessage = null;

    private int _gameState = STATE_IDLE;
    private int[] _sequence;
    private int _sequenceLength;
    private int _playbackIndex = 0;
    private int _inputIndex = 0;
    private float _stateTimer = 0f;
    private int _roundsCompleted = 0;
    private int _score = 0;
    private bool _gameActive = false;

    // ============================================================
    protected override void SetupStation()
    {
        base.SetupStation();
        stationName = "Communications";
        requiredRank = 1;
        departmentId = 2;

        UpdateRelayDisplay();
    }

    void Update()
    {
        if (!_isLocalOperating || !_gameActive) return;

        _stateTimer -= Time.deltaTime;

        if (_gameState == STATE_PLAYING)
        {
            if (_stateTimer <= 0f)
                AdvancePlayback();
        }
        else if (_gameState == STATE_INPUT)
        {
            if (_stateTimer <= 0f)
            {
                EndGame();
                return;
            }

            if (stationUIController != null)
                stationUIController.SetTimer(_stateTimer);
        }
    }

    public override void EnterStation()
    {
        base.EnterStation();
        StartGame();
    }

    public override void ExitStation()
    {
        if (_gameActive) EndGame();
        base.ExitStation();
    }

    // ============================================================
    // GAME FLOW
    // ============================================================
    public void StartGame()
    {
        if (signalMaterials == null || signalMaterials.Length == 0) return;

        _gameActive = true;
        _roundsCompleted = 0;
        _score = 0;

        StartNewRound();

        if (stationUIController != null)
            stationUIController.SetScore(0);
    }

    private void StartNewRound()
    {
        _sequenceLength = Mathf.Min(baseSequenceLength + _roundsCompleted, maxSequenceLength);
        _sequence = new int[_sequenceLength];

        for (int i = 0; i < _sequenceLength; i++)
            _sequence[i] = Random.Range(0, signalCount);

        _playbackIndex = 0;
        _gameState = STATE_PLAYING;
        _stateTimer = 0f;
        ClearAllPads();
    }

    private void AdvancePlayback()
    {
        if (_playbackIndex >= _sequenceLength)
        {
            // Playback finished - start input phase.
            _inputIndex = 0;
            _gameState = STATE_INPUT;
            _stateTimer = inputTimeLimit;
            ClearAllPads();

            if (scoreText != null)
                scoreText.text = "REPEAT THE SEQUENCE";

            if (stationUIController != null)
                stationUIController.SetTimer(_stateTimer);
            return;
        }

        int signal = _sequence[_playbackIndex];
        HighlightSignal(signal);

        _playbackIndex++;
        // Gap after each signal, plus a longer gap before input phase begins.
        _stateTimer = signalHoldTime + signalGapTime;
    }

    public void PressPad(int signal)
    {
        if (!_gameActive || _gameState != STATE_INPUT) return;
        if (signal < 0 || signal >= signalCount) return;

        if (signal == _sequence[_inputIndex])
        {
            HighlightSignal(signal);

            _inputIndex++;
            if (_inputIndex >= _sequenceLength)
            {
                RoundComplete();
            }
        }
        else
        {
            // Wrong signal - game over.
            _score += Mathf.RoundToInt(_stateTimer * 10f);
            SubmitScore(_score);
            EndGame();
        }
    }

    // Explicit wrappers for UI button binding.
    public void PressPad0() { PressPad(0); }
    public void PressPad1() { PressPad(1); }
    public void PressPad2() { PressPad(2); }
    public void PressPad3() { PressPad(3); }

    private void RoundComplete()
    {
        _roundsCompleted++;
        _score += 100 * _roundsCompleted;

        if (scoreText != null)
            scoreText.text = "ROUND " + _roundsCompleted + " CLEAR";

        if (stationUIController != null)
            stationUIController.SetScore(_score);

        if (_roundsCompleted >= 4 || _sequenceLength >= maxSequenceLength)
        {
            SubmitScore(_score);
            EndGame();
            return;
        }

        SendCustomEventDelayedSeconds("StartNewRoundDelayed", 1.2f);
    }

    public void StartNewRoundDelayed()
    {
        if (!_gameActive) return;
        StartNewRound();
    }

    public void EndGame()
    {
        _gameActive = false;
        _gameState = STATE_IDLE;
        ClearAllPads();

        if (scoreText != null)
            scoreText.text = "COMMS STATION STANDBY";
    }

    // ============================================================
    // VISUAL FEEDBACK
    // ============================================================
    private void HighlightSignal(int signal)
    {
        if (signal < 0 || signal >= signalMaterials.Length) return;

        foreach (var pad in padRenderers)
        {
            if (pad != null)
                pad.material = signalMaterials[signal];
        }

        if (padLights != null)
        {
            for (int i = 0; i < padLights.Length; i++)
            {
                if (padLights[i] != null)
                    padLights[i].enabled = (i == signal);
            }
        }
    }

    private void ClearAllPads()
    {
        if (padRenderers != null)
        {
            foreach (var pad in padRenderers)
            {
                if (pad != null)
                    pad.material = idlePadMaterial;
            }
        }

        if (padLights != null)
        {
            foreach (var light in padLights)
            {
                if (light != null)
                    light.enabled = false;
            }
        }
    }

    // ============================================================
    // INTER-SHIP MESSAGE RELAY
    // ============================================================
    public void SendRelayMessage(string message)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        if (string.IsNullOrEmpty(message)) return;

        // While a game is running the seated operator owns this station and
        // keeps writing operator/score state - don't steal ownership from them.
        // Otherwise any crew member may post (take ownership, post, hand back).
        if (_currentOperatorId != -1 && _currentOperatorId != local.playerId)
            return;

        if (Networking.IsOwner(gameObject))
        {
            ApplyRelayMessage(local.displayName, message);
        }
        else
        {
            _pendingRelaySender = local.displayName;
            _pendingRelayMessage = message;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    public override void OnOwnershipTransferred()
    {
        base.OnOwnershipTransferred();

        if (Networking.IsOwner(gameObject) && _pendingRelayMessage != null)
        {
            ApplyRelayMessage(_pendingRelaySender, _pendingRelayMessage);
            _pendingRelaySender = null;
            _pendingRelayMessage = null;

            // Hand ownership back so the seat stays neutral for the operator.
            Networking.SetOwner(Networking.Master, gameObject);
        }
    }

    private void ApplyRelayMessage(string sender, string message)
    {
        RelaySender = sender;
        RelayMessage = message;
        RequestSerialization();
    }

    public void SendRelayChannel(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= relayChannels.Length) return;
        SendRelayMessage(relayChannels[channelIndex]);
    }

    public void SendChannel0() { SendRelayChannel(0); }
    public void SendChannel1() { SendRelayChannel(1); }
    public void SendChannel2() { SendRelayChannel(2); }
    public void SendChannel3() { SendRelayChannel(3); }
    public void SendChannel4() { SendRelayChannel(4); }

    private void UpdateRelayDisplay()
    {
        if (relayDisplayText != null)
            relayDisplayText.text = _relaySender + " >> " + _relayMessage;
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public int GetRoundsCompleted() { return _roundsCompleted; }
    public bool IsGameActive() { return _gameActive; }
    public string GetRelaySender() { return _relaySender; }
    public string GetRelayMessage() { return _relayMessage; }
}
