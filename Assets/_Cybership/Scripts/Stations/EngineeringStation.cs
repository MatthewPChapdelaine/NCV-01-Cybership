// ============================================================
// NCV-01 Cybership - Engineering Station (Reactor Balancing)
//
// Balance power and coolant to hold the reactor in the target
// output/temperature window. Hold the ship stable to build score;
// letting temperature spike trips an SCRAM and raises the alert.
// ============================================================

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

public class EngineeringStation : StationController
{
    [Header("Reactor Controls (UI Sliders, 0-100)")]
    public Slider powerSliderUI;
    public Slider coolantSliderUI;

    [Header("Reactor Controls (Transform sliders, optional)")]
    public Transform powerSliderTransform;
    public Transform coolantSliderTransform;
    public float sliderTravel = 0.5f;

    [Header("Visual Feedback")]
    public Renderer temperatureGauge;
    public Renderer outputGauge;
    public ParticleSystem steamEffect;
    public ParticleSystem warningEffect;
    public Light reactorGlow;

    [Header("Target Values")]
    public float targetOutput = 75f;
    public float targetTemp = 80f;
    public float tolerance = 10f;

    // The seated engineer owns this station, so this field is a legal write
    // target for them. The host applies it to the reactor when it changes.
    [UdonSynced, FieldChangeCallback(nameof(OnDesiredOutputChanged))]
    private float _desiredOutput = 50f;

    private float _currentPowerSetting = 50f;
    private float _currentCoolantSetting = 50f;
    private float _stabilityScore = 100f;
    private bool _stationExited = false;
    private float _lastSentOutput = -1f;
    private float _sendTimer = 0f;

    // ============================================================
    protected override void SetupStation()
    {
        base.SetupStation();
        stationName = "Engineering";
        requiredRank = 2;
        departmentId = 4;
    }

    void Update()
    {
        if (!_isLocalOperating) return;

        ReadSliderSettings();

        if (shipState != null)
        {
            float effectiveOutput = _currentPowerSetting * (1f - (_currentCoolantSetting / 200f));

            // Throttle serialization: only sync when the value meaningfully
            // changed or ~0.25s has elapsed, instead of every frame.
            _sendTimer -= Time.deltaTime;
            if (Mathf.Abs(effectiveOutput - _lastSentOutput) > 0.5f || _sendTimer <= 0f)
            {
                _desiredOutput = effectiveOutput;
                RequestSerialization();
                _lastSentOutput = effectiveOutput;
                _sendTimer = 0.25f;
            }
        }

        UpdateGauges();
        CheckStability();

        if (stationUIController != null)
            stationUIController.SetScore((int)_stabilityScore);
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        bool wasLocal = player.isLocal && _isLocalOperating;

        // Submit BEFORE the base call clears _isLocalOperating and returns
        // ownership to the host, otherwise the exit score would be dropped.
        if (wasLocal)
        {
            SubmitScore(Mathf.RoundToInt(_stabilityScore * 10f));
            _stationExited = true;
        }

        base.OnStationExited(player);
    }

    public override void EnterStation()
    {
        base.EnterStation();
        _stabilityScore = 100f;
        _stationExited = false;
    }

    // ============================================================
    // SLIDER READING
    // ============================================================
    private void ReadSliderSettings()
    {
        if (powerSliderUI != null)
            _currentPowerSetting = Mathf.Clamp(powerSliderUI.value, 0f, 100f);
        else if (powerSliderTransform != null)
            _currentPowerSetting = Mathf.InverseLerp(-sliderTravel, sliderTravel, powerSliderTransform.localPosition.x) * 100f;

        if (coolantSliderUI != null)
            _currentCoolantSetting = Mathf.Clamp(coolantSliderUI.value, 0f, 100f);
        else if (coolantSliderTransform != null)
            _currentCoolantSetting = Mathf.InverseLerp(-sliderTravel, sliderTravel, coolantSliderTransform.localPosition.x) * 100f;
    }

    // ============================================================
    // REACTOR OUTPUT RELAY
    // ============================================================
    // The host owns the reactor state, so it applies the synced desired
    // output here rather than the engineer writing to a host-owned object.
    private void OnDesiredOutputChanged()
    {
        if (Networking.IsMaster && shipState != null)
            shipState.SetReactorOutput(_desiredOutput);
    }

    // ============================================================
    // GAUGES & EFFECTS
    // ============================================================
    private void UpdateGauges()
    {
        if (shipState == null) return;

        float temp = shipState.ReactorTemperature;
        float output = shipState.ReactorOutput;

        if (temperatureGauge != null)
        {
            Color tempColor = Color.Lerp(Color.green, Color.red, temp / 150f);
            temperatureGauge.material.SetColor("_EmissionColor", tempColor * 2f);
        }

        if (outputGauge != null)
        {
            Color outColor = Color.Lerp(Color.red, Color.green, output / 100f);
            outputGauge.material.SetColor("_EmissionColor", outColor * 2f);
        }

        if (reactorGlow != null)
            reactorGlow.intensity = output / 10f;

        if (temp > 100f && steamEffect != null && !steamEffect.isPlaying)
            steamEffect.Play();
        else if (temp <= 100f && steamEffect != null && steamEffect.isPlaying)
            steamEffect.Stop();

        if (temp > 140f && warningEffect != null && !warningEffect.isPlaying)
            warningEffect.Play();
        else if (temp <= 140f && warningEffect != null && warningEffect.isPlaying)
            warningEffect.Stop();
    }

    private void CheckStability()
    {
        if (shipState == null) return;

        float output = shipState.ReactorOutput;
        float temp = shipState.ReactorTemperature;

        float outputDiff = Mathf.Abs(output - targetOutput);
        float tempDiff = Mathf.Abs(temp - targetTemp);

        bool outputGood = outputDiff < tolerance;
        bool tempGood = tempDiff < tolerance;

        if (outputGood && tempGood)
            _stabilityScore = Mathf.Min(100f, _stabilityScore + Time.deltaTime * 5f);
        else
            _stabilityScore = Mathf.Max(0f, _stabilityScore - Time.deltaTime * 10f);
    }

    // ============================================================
    // EMERGENCY CONTROLS
    // ============================================================
    public void SCRAMReactor()
    {
        if (shipState != null)
            shipState.SetAlertLevel(2);

        // SCRAM relays to the host via our own synced field (the host applies
        // it in OnDesiredOutputChanged), just like normal slider adjustments.
        _desiredOutput = 0f;
        RequestSerialization();

        if (reactorGlow != null)
            reactorGlow.intensity = 0.1f;

        _stabilityScore = 0f;

        if (stationUIController != null)
            stationUIController.SetScore(0);
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public float GetPowerSetting() { return _currentPowerSetting; }
    public float GetCoolantSetting() { return _currentCoolantSetting; }
    public float GetStabilityScore() { return _stabilityScore; }
    public bool DidExit() { return _stationExited; }
}
