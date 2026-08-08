// ============================================================
// NCV-01 Cybership - Station Console UI
// Attach to each station's world-space console canvas.
// Displays station state and is driven by its StationController.
// ============================================================

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class StationUIController : UdonSharpBehaviour
{
    [Header("Station Info")]
    public Text stationNameText;
    public Text operatorText;
    public Text statusText;
    public Image statusIndicator;

    [Header("Game UI")]
    public Text scoreText;
    public Text timerText;
    public Slider progressBar;

    [Header("Colors")]
    public Color activeColor = new Color(0f, 1f, 0.25f);
    public Color inactiveColor = new Color(0.5f, 0.5f, 0.5f);
    public Color alertColor = new Color(1f, 0.2f, 0.2f);

    public StationController station;

    // ============================================================
    void Start()
    {
        SetStationState(false);
    }

    void Update()
    {
        if (station == null) return;

        if (stationNameText != null)
            stationNameText.text = station.stationName.ToUpper();

        bool isActive = station.IsActive();

        if (statusText != null)
            statusText.text = isActive ? "ONLINE" : "STANDBY";

        if (statusIndicator != null)
            statusIndicator.color = isActive ? activeColor : inactiveColor;
    }

    // ============================================================
    // DRIVEN BY STATION CONTROLLER
    // ============================================================
    public void SetStationState(bool active)
    {
        if (statusText != null)
            statusText.text = active ? "ONLINE" : "STANDBY";

        if (statusIndicator != null)
            statusIndicator.color = active ? activeColor : inactiveColor;
    }

    public void SetScore(int score)
    {
        if (scoreText != null)
            scoreText.text = "SCORE: " + score.ToString();
    }

    public void SetTimer(float time)
    {
        if (timerText == null) return;

        if (time < 0f) time = 0f;
        int mins = (int)(time / 60f);
        int secs = (int)(time % 60f);
        timerText.text = mins.ToString() + ":" + (secs < 10 ? "0" : "") + secs.ToString();

        if (timerText.color != alertColor && time <= 10f)
            timerText.color = alertColor;
    }

    public void SetProgress(float progress)
    {
        if (progressBar != null)
            progressBar.value = Mathf.Clamp01(progress / 100f);
    }
}
