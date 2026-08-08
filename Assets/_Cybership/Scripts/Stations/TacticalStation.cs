// ============================================================
// NCV-01 Cybership - Tactical Station (Targeting Game)
//
// Aim with the right-hand tracking ray; the crosshair follows the
// aim point. Pull the Use trigger (InputUse) to fire. Target
// prefabs spawn at configured spawn points; hit as many as possible
// before the timer runs out.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

public class TacticalStation : StationController
{
    [Header("Tactical Game")]
    public GameObject[] targetPrefabs;
    public Transform[] spawnPoints;
    public Transform crosshair;
    public float gameDuration = 60f;
    public float spawnInterval = 2f;
    public float rayRange = 100f;

    [Header("Scoring")]
    public int pointsPerHit = 100;
    public int pointsPerMiss = -25;
    public int bonusStreak = 5;

    private bool _gameActive = false;
    private float _gameTimer;
    private float _spawnTimer;
    private int _score;
    private int _streak;
    private int _totalHits;
    private int _totalMisses;
    private GameObject[] _activeTargets;

    // ============================================================
    protected override void SetupStation()
    {
        base.SetupStation();
        stationName = "Tactical";
        requiredRank = 1;
        departmentId = 3;

        _activeTargets = new GameObject[spawnPoints != null ? spawnPoints.Length : 0];
    }

    void Update()
    {
        if (!_isLocalOperating || !_gameActive) return;

        _gameTimer -= Time.deltaTime;
        if (_gameTimer <= 0f)
        {
            EndGame();
            return;
        }

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            SpawnTarget();
            _spawnTimer = spawnInterval;
        }

        UpdateCrosshair();

        if (stationUIController != null)
            stationUIController.SetTimer(_gameTimer);
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
    // FIRING
    // ============================================================
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (value) FireWeapon();
    }

    public void FireWeapon()
    {
        if (!_isLocalOperating || !_gameActive) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        VRCPlayerApi.TrackingData hand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        Ray ray = new Ray(hand.position, hand.rotation * Vector3.forward);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayRange))
        {
            TacticalTarget target = hit.collider.GetComponent<TacticalTarget>();
            if (target != null)
                target.OnHit();
            else
                OnTargetMiss();
        }
        else
        {
            OnTargetMiss();
        }
    }

    // ============================================================
    // GAME FLOW
    // ============================================================
    public void StartGame()
    {
        _gameActive = true;
        _gameTimer = gameDuration;
        _spawnTimer = spawnInterval;
        _score = 0;
        _streak = 0;
        _totalHits = 0;
        _totalMisses = 0;

        ClearTargets();

        if (crosshair != null)
            crosshair.gameObject.SetActive(true);

        if (stationUIController != null)
        {
            stationUIController.SetScore(0);
            stationUIController.SetTimer(_gameTimer);
        }

        // Tactical engagement -> Condition Yellow.
        if (shipState != null)
            shipState.SetAlertLevel(1);
    }

    public void EndGame()
    {
        _gameActive = false;

        float accuracy = 0f;
        if (_totalHits + _totalMisses > 0)
            accuracy = (float)_totalHits / (_totalHits + _totalMisses);

        int finalScore = Mathf.RoundToInt(_score * (0.5f + accuracy));
        SubmitScore(finalScore);

        ClearTargets();

        if (crosshair != null)
            crosshair.gameObject.SetActive(false);

        // Return to Condition Green if no other system raised the alert.
        if (shipState != null && shipState.AlertLevel == 1)
            shipState.SetAlertLevel(0);
    }

    private void ClearTargets()
    {
        if (_activeTargets == null) return;

        for (int i = 0; i < _activeTargets.Length; i++)
        {
            if (_activeTargets[i] != null)
                Destroy(_activeTargets[i]);
            _activeTargets[i] = null;
        }
    }

    private void SpawnTarget()
    {
        if (targetPrefabs == null || targetPrefabs.Length == 0) return;
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        int attempts = 0;
        while (attempts < 10)
        {
            int idx = Random.Range(0, spawnPoints.Length);
            if (_activeTargets[idx] == null)
            {
                GameObject prefab = targetPrefabs[Random.Range(0, targetPrefabs.Length)];
                if (prefab == null) { attempts++; continue; }

                GameObject target = VRCInstantiate(prefab);
                if (target == null) return;

                target.transform.SetPositionAndRotation(spawnPoints[idx].position, spawnPoints[idx].rotation);

                TacticalTarget tt = target.GetComponent<TacticalTarget>();
                if (tt != null)
                    tt.station = this;

                _activeTargets[idx] = target;
                return;
            }
            attempts++;
        }
    }

    private void UpdateCrosshair()
    {
        if (crosshair == null) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        VRCPlayerApi.TrackingData hand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        Ray ray = new Ray(hand.position, hand.rotation * Vector3.forward);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayRange))
        {
            crosshair.position = hit.point;
            crosshair.rotation = Quaternion.LookRotation(hit.normal);
        }
    }

    // ============================================================
    // SCORING CALLBACKS (called by TacticalTarget)
    // ============================================================
    public void OnTargetHit(GameObject target)
    {
        if (!_gameActive) return;

        _score += pointsPerHit;
        _streak++;
        _totalHits++;

        if (_streak >= bonusStreak)
        {
            _score += 500;
            _streak = 0;
        }

        if (stationUIController != null)
            stationUIController.SetScore(_score);

        for (int i = 0; i < _activeTargets.Length; i++)
        {
            if (_activeTargets[i] == target)
            {
                Destroy(_activeTargets[i]);
                _activeTargets[i] = null;
                return;
            }
        }
    }

    public void OnTargetMiss()
    {
        if (!_gameActive) return;

        _score += pointsPerMiss;
        if (_score < 0) _score = 0;
        _streak = 0;
        _totalMisses++;

        if (stationUIController != null)
            stationUIController.SetScore(_score);
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public int GetHits() { return _totalHits; }
    public int GetMisses() { return _totalMisses; }
    public bool IsGameActive() { return _gameActive; }
}
