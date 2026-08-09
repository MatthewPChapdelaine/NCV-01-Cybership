// ============================================================
// NCV-01 Cybership - MAGI Consensus System
// Three AI cores: Melchior (conservative), Balthasar (balanced),
// Caspar (aggressive). The master simulates the deliberation and
// announces a consensus on ship-wide decisions.
//
// coreMaterials layout: 0 = standby, 1 = processing,
//                       2 = aligned (YES), 3 = dissent (NO)
// ============================================================

using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class MAGISystem : UdonSharpBehaviour
{
    [Header("MAGI Cores")]
    public Renderer[] coreRenderers;
    public Material[] coreMaterials;
    public ParticleSystem[] coreParticles;

    [Header("Decision Display")]
    public TextMeshPro decisionText;
    public TextMeshPro voteStatusText;
    public AudioSource magiVoice;
    public AudioClip deliberationSound;
    public AudioClip consensusSound;

    public ShipStateManager shipState;

    public string[] DECISION_TEMPLATES = new string[]
    {
        "EMERGENCY OVERRIDE: Vent atmosphere in Sector 7?",
        "POWER REDIRECT: Divert from shields to engines?",
        "CARGO JETTISON: Eject contaminated materials?",
        "COURSE CORRECTION: Navigate asteroid field directly?",
        "REACTOR SAFETY: SCRAM reactor on temperature spike?",
        "COMMUNICATION: Send distress beacon?"
    };

    // Vote states: 0 = undecided, 1 = YES, 2 = NO
    private int[] _coreVotes = new int[3];
    private string _currentDecision = "";
    private bool _votingActive = false;
    private float _voteTimer = 0f;

    private const float VOTE_DURATION = 15f;

    // ============================================================
    void Start()
    {
        ResetCores();
    }

    void Update()
    {
        if (!Networking.IsMaster) return;
        if (!_votingActive) return;

        _voteTimer -= Time.deltaTime;

        // Cores independently "decide" over time, weighted by personality.
        for (int i = 0; i < 3; i++)
        {
            if (_coreVotes[i] == 0 && Random.value < 0.02f)
                CastSimulatedVote(i);
        }

        if (_voteTimer <= 0f)
            FinalizeDecision();
    }

    // ============================================================
    // PUBLIC CONTROL
    // ============================================================
    public void InitiateDecision(string decision)
    {
        if (!Networking.IsMaster) return;

        _currentDecision = decision;
        _votingActive = true;
        _voteTimer = VOTE_DURATION;

        for (int i = 0; i < 3; i++)
            _coreVotes[i] = 0;

        if (magiVoice != null && deliberationSound != null)
        {
            magiVoice.clip = deliberationSound;
            magiVoice.Play();
        }

        UpdateCoreVisuals();
        BroadcastToAll("MAGI DELIBERATION INITIATED");
    }

    public void InitiateRandomDecision()
    {
        if (DECISION_TEMPLATES == null || DECISION_TEMPLATES.Length == 0) return;
        string decision = DECISION_TEMPLATES[Random.Range(0, DECISION_TEMPLATES.Length)];
        InitiateDecision(decision);
    }

    // Allow a crew override: force a single core's vote.
    public void ForceCoreVote(int coreIndex, bool voteYes)
    {
        if (coreIndex < 0 || coreIndex >= 3) return;
        _coreVotes[coreIndex] = voteYes ? 1 : 2;
        UpdateCoreVisuals();
    }

    // ============================================================
    // SIMULATION
    // ============================================================
    private void CastSimulatedVote(int coreIndex)
    {
        float yesChance = GetCorePersonality(coreIndex);
        _coreVotes[coreIndex] = Random.value < yesChance ? 1 : 2;
        UpdateCoreVisuals();
    }

    private float GetCorePersonality(int coreIndex)
    {
        if (coreIndex == 0) return 0.3f; // Melchior - conservative
        if (coreIndex == 1) return 0.5f; // Balthasar - balanced
        return 0.7f;                     // Caspar - aggressive
    }

    private void FinalizeDecision()
    {
        _votingActive = false;

        // Undecided cores cast their final vote.
        for (int i = 0; i < 3; i++)
        {
            if (_coreVotes[i] == 0)
                _coreVotes[i] = Random.value < 0.5f ? 1 : 2;
        }

        int yesVotes = 0;
        int noVotes = 0;
        for (int i = 0; i < 3; i++)
        {
            if (_coreVotes[i] == 1) yesVotes++;
            else if (_coreVotes[i] == 2) noVotes++;
        }

        bool decisionPassed = yesVotes >= 2;

        ApplyVoteMaterials(decisionPassed);

        if (magiVoice != null && consensusSound != null)
        {
            magiVoice.clip = consensusSound;
            magiVoice.Play();
        }

        ExecuteDecision(decisionPassed);

        string result = decisionPassed ? "APPROVED" : "DENIED";
        BroadcastToAll("MAGI CONSENSUS: " + result + " (" + yesVotes + "/3)");

        SendCustomEventDelayedSeconds("ResetCores", 5f);
    }

    private void ExecuteDecision(bool approved)
    {
        if (!_currentDecision.Contains("Vent"))
        {
            if (!_currentDecision.Contains("Power"))
            {
                if (_currentDecision.Contains("JETTISON"))
                {
                    if (approved && shipState != null)
                        shipState.ModifyReputation(5);
                }
                else if (_currentDecision.Contains("SCRAM"))
                {
                    if (approved && shipState != null)
                        shipState.SetReactorOutput(0f);
                }
            }
        }
        else
        {
            if (approved && shipState != null)
                shipState.ModifyReputation(-10);
        }
    }

    // ============================================================
    // VISUALS
    // ============================================================
    private void ApplyVoteMaterials(bool decisionPassed)
    {
        if (coreRenderers == null || coreMaterials == null) return;

        for (int i = 0; i < 3; i++)
        {
            int matIndex = _coreVotes[i] == 1 ? 2 : 3;
            if (matIndex < coreMaterials.Length && coreRenderers[i] != null)
                coreRenderers[i].material = coreMaterials[matIndex];
        }
    }

    private void UpdateCoreVisuals()
    {
        if (coreRenderers == null || coreMaterials == null) return;

        for (int i = 0; i < 3; i++)
        {
            int matIndex = 1; // processing
            if (_coreVotes[i] == 1) matIndex = 2;
            else if (_coreVotes[i] == 2) matIndex = 3;

            if (matIndex < coreMaterials.Length && coreRenderers[i] != null)
                coreRenderers[i].material = coreMaterials[matIndex];
        }

        if (coreParticles != null)
        {
            for (int i = 0; i < 3; i++)
            {
                if (coreParticles[i] != null)
                {
                    if (_coreVotes[i] != 0 && !coreParticles[i].isPlaying)
                        coreParticles[i].Play();
                }
            }
        }
    }

    public void ResetCores()
    {
        for (int i = 0; i < 3; i++)
            _coreVotes[i] = 0;

        if (coreRenderers != null && coreMaterials != null && coreMaterials.Length > 0)
        {
            foreach (var r in coreRenderers)
            {
                if (r != null)
                    r.material = coreMaterials[0];
            }
        }

        if (coreParticles != null)
        {
            foreach (var p in coreParticles)
            {
                if (p != null)
                    p.Stop();
            }
        }
    }

    // ============================================================
    // UTILITY
    // ============================================================
    private void BroadcastToAll(string message)
    {
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public bool IsVotingActive() { return _votingActive; }
    public float GetVoteTimeRemaining() { return _voteTimer; }
    public int GetVote(int coreIndex)
    {
        if (coreIndex < 0 || coreIndex >= 3) return 0;
        return _coreVotes[coreIndex];
    }
    public string GetCurrentDecision() { return _currentDecision; }
}
