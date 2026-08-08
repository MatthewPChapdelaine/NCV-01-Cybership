// ============================================================
// NCV-01 Cybership - Science Station (Data Analysis)
//
// Analyze a series of samples and classify each one before time
// runs out. Sample materials are shown on the display; the player
// picks a classification via the console buttons.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class ScienceStation : StationController
{
    [Header("Analysis Game")]
    public Renderer[] sampleDisplays;
    public Material[] sampleMaterials; // index-aligned with SAMPLE_TYPES
    public int samplesToAnalyze = 10;
    public float analysisTime = 60f;

    public string[] SAMPLE_TYPES = new string[]
    {
        "Organic",
        "Mineral",
        "Energy",
        "Unknown",
        "Hazardous"
    };

    private int[] _currentSamples;
    private int[] _playerClassifications;
    private int _currentSampleIndex = 0;
    private float _gameTimer;
    private bool _gameActive = false;

    // ============================================================
    protected override void SetupStation()
    {
        base.SetupStation();
        stationName = "Science";
        requiredRank = 2;
        departmentId = 5;

        _currentSamples = new int[samplesToAnalyze];
        _playerClassifications = new int[samplesToAnalyze];
    }

    void Update()
    {
        if (!_isLocalOperating || !_gameActive) return;

        _gameTimer -= Time.deltaTime;
        if (_gameTimer <= 0f)
            EndGame();

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
    // GAME FLOW
    // ============================================================
    public void StartGame()
    {
        if (samplesToAnalyze <= 0) return;
        if (SAMPLE_TYPES == null || SAMPLE_TYPES.Length == 0) return;

        _gameActive = true;
        _gameTimer = analysisTime;
        _currentSampleIndex = 0;

        for (int i = 0; i < samplesToAnalyze; i++)
            _currentSamples[i] = Random.Range(0, SAMPLE_TYPES.Length);

        DisplayCurrentSample();

        if (stationUIController != null)
        {
            stationUIController.SetScore(0);
            stationUIController.SetTimer(_gameTimer);
        }
    }

    private void DisplayCurrentSample()
    {
        if (_currentSampleIndex >= samplesToAnalyze) return;
        if (sampleDisplays == null) return;

        int sampleType = _currentSamples[_currentSampleIndex];

        foreach (var display in sampleDisplays)
        {
            if (display != null && sampleMaterials != null && sampleType < sampleMaterials.Length)
                display.material = sampleMaterials[sampleType];
        }
    }

    public void ClassifySample(int classification)
    {
        if (!_gameActive) return;
        if (samplesToAnalyze <= 0) return;
        if (_currentSampleIndex >= samplesToAnalyze) return;
        if (classification < 0 || classification >= SAMPLE_TYPES.Length) return;

        _playerClassifications[_currentSampleIndex] = classification;
        _currentSampleIndex++;

        if (_currentSampleIndex >= samplesToAnalyze)
        {
            EndGame();
        }
        else
        {
            DisplayCurrentSample();
        }
    }

    // Explicit wrappers for UI button binding.
    public void ClassifyOrganic() { ClassifySample(0); }
    public void ClassifyMineral() { ClassifySample(1); }
    public void ClassifyEnergy() { ClassifySample(2); }
    public void ClassifyUnknown() { ClassifySample(3); }
    public void ClassifyHazardous() { ClassifySample(4); }

    public void EndGame()
    {
        if (!_gameActive) return;

        _gameActive = false;

        int correct = 0;
        int answered = _currentSampleIndex;
        for (int i = 0; i < answered; i++)
        {
            if (_playerClassifications[i] == _currentSamples[i])
                correct++;
        }

        float accuracy = answered > 0 ? (float)correct / answered : 0f;
        int score = Mathf.RoundToInt(accuracy * 1000f) + Mathf.RoundToInt(_gameTimer * 5f);

        SubmitScore(score);

        if (stationUIController != null)
            stationUIController.SetScore(score);
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public int GetCurrentSampleIndex() { return _currentSampleIndex; }
    public string GetCurrentSampleTypeName()
    {
        if (!_gameActive || _currentSampleIndex >= samplesToAnalyze) return "STANDBY";
        return SAMPLE_TYPES[_currentSamples[_currentSampleIndex]];
    }
    public bool IsGameActive() { return _gameActive; }
}
