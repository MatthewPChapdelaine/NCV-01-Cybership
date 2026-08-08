// ============================================================
// NCV-01 Cybership - Player HUD System
// Attach to the PlayerHUD world-space canvas.
// ============================================================

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

public class HUDManager : UdonSharpBehaviour
{
    [Header("HUD Elements")]
    public Text rankText;
    public Text xpText;
    public Text departmentText;
    public Text alertText;
    public Image alertIndicator;
    public Text watchText;
    public Text missionText;

    [Header("Notification")]
    public GameObject notificationPanel;
    public Text notificationText;
    public Animation notificationAnim;

    [Header("References")]
    public PlayerProgressionManager progression;
    public ShipStateManager shipState;
    public WatchScheduleManager watchSchedule;
    public EmergencyEventManager emergencyManager;
    public MissionManager missionManager;

    private Color[] ALERT_COLORS = new Color[]
    {
        new Color(0f, 1f, 0.25f),
        new Color(1f, 0.8f, 0f),
        new Color(1f, 0.2f, 0.2f),
        new Color(0.8f, 0f, 1f)
    };

    private string[] ALERT_NAMES = new string[]
    {
        "CONDITION GREEN",
        "CONDITION YELLOW",
        "CONDITION RED",
        "CONDITION BLACK"
    };

    // ============================================================
    void Start()
    {
        if (notificationPanel != null)
            notificationPanel.SetActive(false);

        RefreshHUD();
    }

    void Update()
    {
        UpdatePlayerInfo();
        UpdateShipStatus();
        UpdateWatchInfo();
        UpdateMissionInfo();
    }

    // ============================================================
    private void UpdatePlayerInfo()
    {
        if (progression == null) return;

        if (rankText != null)
            rankText.text = "RANK: " + progression.GetCurrentRankName();

        if (xpText != null)
        {
            int xpToNext = progression.GetXPToNextRank();
            if (xpToNext > 0)
                xpText.text = "XP: " + progression.GetCurrentXP() + " (" + xpToNext + " TO NEXT)";
            else
                xpText.text = "XP: " + progression.GetCurrentXP() + " (MAX)";
        }

        if (departmentText != null)
            departmentText.text = "DEPT: " + progression.GetDepartmentName();
    }

    private void UpdateShipStatus()
    {
        if (shipState == null) return;

        int alert = shipState.AlertLevel;

        if (alertText != null)
        {
            if (alert >= 0 && alert < ALERT_NAMES.Length)
                alertText.text = ALERT_NAMES[alert];
        }

        if (alertIndicator != null)
        {
            if (alert >= 0 && alert < ALERT_COLORS.Length)
                alertIndicator.color = ALERT_COLORS[alert];
        }
    }

    private void UpdateWatchInfo()
    {
        if (watchSchedule == null || watchText == null) return;
        if (!Networking.IsNetworkSettled) return;

        float timeLeft = watchSchedule.GetWatchTimeRemaining();
        int minutes = (int)(timeLeft / 60f);
        int seconds = (int)(timeLeft % 60f);

        watchText.text = "WATCH: " + watchSchedule.GetCurrentWatchName() + " [" +
            minutes.ToString() + ":" + (seconds < 10 ? "0" : "") + seconds.ToString() + "]";
    }

    private void UpdateMissionInfo()
    {
        if (missionText == null) return;

        // Emergencies take display priority.
        if (emergencyManager != null && emergencyManager.GetActiveEvent() >= 0)
        {
            float timeLeft = emergencyManager.GetEventTimeRemaining();
            missionText.text = "EMERGENCY: " + emergencyManager.GetActiveEventName() + " [" +
                Mathf.RoundToInt(timeLeft).ToString() + "s]";
            missionText.color = Color.red;
            return;
        }

        if (missionManager != null && missionManager.IsMissionActive())
        {
            missionText.text = "MISSION: " + missionManager.GetCurrentMissionName() + " " +
                Mathf.RoundToInt(missionManager.GetMissionProgress()).ToString() + "% [" +
                Mathf.RoundToInt(missionManager.GetMissionTimeRemaining()).ToString() + "s]";
            missionText.color = Color.yellow;
            return;
        }

        missionText.text = "NO ACTIVE MISSION";
        missionText.color = Color.green;
    }

    // ============================================================
    // NOTIFICATIONS
    // ============================================================
    public void ShowNotification(string message)
    {
        if (notificationPanel == null || notificationText == null) return;

        notificationText.text = message;
        notificationPanel.SetActive(true);

        if (notificationAnim != null)
            notificationAnim.Play();

        SendCustomEventDelayedSeconds("HideNotification", 3f);
    }

    public void HideNotification()
    {
        if (notificationPanel != null)
            notificationPanel.SetActive(false);
    }

    // Called by ShipStateManager whenever the alert level changes.
    public void OnAlertLevelChanged(int level)
    {
        if (alertText != null && level >= 0 && level < ALERT_NAMES.Length)
            alertText.text = ALERT_NAMES[level];

        if (alertIndicator != null && level >= 0 && level < ALERT_COLORS.Length)
            alertIndicator.color = ALERT_COLORS[level];
    }

    public void RefreshHUD()
    {
        UpdatePlayerInfo();
        UpdateShipStatus();
        UpdateWatchInfo();
        UpdateMissionInfo();
    }
}
