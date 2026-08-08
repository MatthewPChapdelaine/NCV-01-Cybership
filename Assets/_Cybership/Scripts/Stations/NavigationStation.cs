// ============================================================
// NCV-01 Cybership - Navigation Station (Node Puzzle)
//
// Plot a course by selecting the target waypoint nodes in order.
// Aim the right-hand ray at a node and pull the Use trigger, or
// wire UI buttons to SelectNode0()..SelectNodeN().
// Wrong picks cost time; completing the path within the limit wins.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

public class NavigationStation : StationController
{
    [Header("Navigation Game")]
    public Transform[] waypointNodes;
    public LineRenderer pathRenderer;
    public Material activePathMaterial;
    public Material errorPathMaterial;
    public Material defaultPathMaterial;

    [Header("Game Settings")]
    public int pathLength = 5;
    public float timeLimit = 45f;
    public float timePenaltyPerError = 5f;
    public float rayRange = 10f;

    private int[] _targetPath;
    private int[] _playerPath;
    private int _currentStep = 0;
    private float _gameTimer;
    private bool _gameActive = false;
    private float _errorFlashTimer = 0f;

    // ============================================================
    protected override void SetupStation()
    {
        base.SetupStation();
        stationName = "Navigation";
        requiredRank = 1;
        departmentId = 2;
    }

    void Update()
    {
        if (!_isLocalOperating || !_gameActive) return;

        _gameTimer -= Time.deltaTime;
        if (_gameTimer <= 0f)
        {
            EndGame(false);
            return;
        }

        if (stationUIController != null)
            stationUIController.SetTimer(_gameTimer);

        // Briefly restore the path material after an error flash.
        if (_errorFlashTimer > 0f)
        {
            _errorFlashTimer -= Time.deltaTime;
            if (_errorFlashTimer <= 0f)
                RestorePathMaterial();
        }
    }

    public override void EnterStation()
    {
        base.EnterStation();
        StartGame();
    }

    public override void ExitStation()
    {
        if (_gameActive) EndGame(false);
        base.ExitStation();
    }

    // ============================================================
    // INPUT
    // ============================================================
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (value) TrySelectNode();
    }

    private void TrySelectNode()
    {
        if (!_isLocalOperating || !_gameActive) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        VRCPlayerApi.TrackingData hand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        Ray ray = new Ray(hand.position, hand.rotation * Vector3.forward);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayRange))
        {
            if (waypointNodes == null) return;
            for (int i = 0; i < waypointNodes.Length; i++)
            {
                if (hit.transform == waypointNodes[i])
                {
                    SelectNode(i);
                    return;
                }
            }
        }
    }

    // ============================================================
    // GAME FLOW
    // ============================================================
    public void StartGame()
    {
        if (waypointNodes == null || waypointNodes.Length == 0) return;
        if (pathLength <= 0) pathLength = 1;

        _gameActive = true;
        _gameTimer = timeLimit;
        _currentStep = 0;

        _targetPath = new int[pathLength];
        _playerPath = new int[pathLength];

        // First node is always the starting waypoint (index 0).
        _targetPath[0] = 0;

        for (int i = 1; i < pathLength; i++)
            _targetPath[i] = Random.Range(0, waypointNodes.Length);

        RestorePathMaterial();
        ClearPathRenderer();

        if (stationUIController != null)
        {
            stationUIController.SetScore(0);
            stationUIController.SetTimer(_gameTimer);
        }
    }

    public void SelectNode(int nodeIndex)
    {
        if (!_gameActive) return;
        if (_targetPath == null || _playerPath == null) return;
        if (waypointNodes == null) return;
        if (nodeIndex < 0 || nodeIndex >= waypointNodes.Length) return;

        if (nodeIndex == _targetPath[_currentStep])
        {
            _playerPath[_currentStep] = nodeIndex;
            _currentStep++;

            UpdatePathVisual();

            if (_currentStep >= pathLength)
            {
                EndGame(true);
            }
        }
        else
        {
            _gameTimer -= timePenaltyPerError;
            ShowError();

            if (stationUIController != null)
                stationUIController.SetTimer(_gameTimer);
        }
    }

    // Explicit wrappers so world-space UI buttons can be wired
    // without needing parameterized Udon events.
    public void SelectNode0() { SelectNode(0); }
    public void SelectNode1() { SelectNode(1); }
    public void SelectNode2() { SelectNode(2); }
    public void SelectNode3() { SelectNode(3); }
    public void SelectNode4() { SelectNode(4); }
    public void SelectNode5() { SelectNode(5); }

    public void EndGame(bool success)
    {
        _gameActive = false;

        int score = 0;
        if (success)
            score = 1000 + Mathf.RoundToInt(_gameTimer * 10f);

        SubmitScore(score);

        if (stationUIController != null)
            stationUIController.SetScore(score);

        RestorePathMaterial();
    }

    // ============================================================
    // VISUALS
    // ============================================================
    private void UpdatePathVisual()
    {
        if (pathRenderer == null) return;
        if (pathRenderer.material != activePathMaterial && activePathMaterial != null)
            pathRenderer.material = activePathMaterial;

        pathRenderer.positionCount = _currentStep + 1;
        for (int i = 0; i <= _currentStep; i++)
        {
            pathRenderer.SetPosition(i, waypointNodes[_playerPath[i]].position);
        }
    }

    private void ShowError()
    {
        if (pathRenderer != null && errorPathMaterial != null)
            pathRenderer.material = errorPathMaterial;

        _errorFlashTimer = 0.5f;
    }

    private void RestorePathMaterial()
    {
        if (pathRenderer == null) return;

        if (_currentStep > 0 && activePathMaterial != null)
            pathRenderer.material = activePathMaterial;
        else if (defaultPathMaterial != null)
            pathRenderer.material = defaultPathMaterial;
    }

    private void ClearPathRenderer()
    {
        if (pathRenderer != null)
            pathRenderer.positionCount = 0;
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public int GetCurrentStep() { return _currentStep; }
    public int GetPathLength() { return pathLength; }
    public bool IsGameActive() { return _gameActive; }
}
