// ============================================================
// NCV-01 Cybership - Random Emergency Events
//
// The master schedules random ship-wide emergencies. Crew can
// respond at the emergency console (RespondToEvent) - a successful
// response resolves the event and boosts reputation, while timeouts
// and inaction damage it.
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class EmergencyEventManager : UdonSharpBehaviour
{
    [Header("Event Settings")]
    public float minEventInterval = 120f;
    public float maxEventInterval = 300f;
    public float eventResponseTime = 60f;

    [Header("Event Types")]
    public string[] eventNames = new string[]
    {
        "Hull Breach",
        "Fire Suppression",
        "Power Fluctuation",
        "Coolant Leak",
        "Life Support Failure",
        "Intruder Alert"
    };

    [Header("Visual Effects")]
    public ParticleSystem fireEffect;
    public ParticleSystem steamLeakEffect;
    public ParticleSystem sparkEffect;
    public AudioSource alarmAudio;
    public AudioClip alarmClip;

    [Header("References")]
    public ShipStateManager shipState;
    public MAGISystem magiSystem;
    public HUDManager hudManager;

    [UdonSynced, FieldChangeCallback(nameof(ActiveEvent))]
    private int _activeEvent = -1;

    public int ActiveEvent
    {
        get { return _activeEvent; }
        set
        {
            _activeEvent = value;
            if (_activeEvent >= 0 && _activeEvent < eventNames.Length)
                PlayEventEffects();
            else
                StopEventEffects();
        }
    }

    [UdonSynced]
    private float _eventTimer = 0f;

    [UdonSynced]
    private bool _crewResolved = false;

    private float _nextEventTime = 60f;
    private bool _eventActive = false;
    private float _syncTimer = 0f;

    // ============================================================
    void Start()
    {
        if (Networking.IsMaster)
            ScheduleNextEvent();

        StopEventEffects();
    }

    void Update()
    {
        if (!Networking.IsMaster) return;

        if (!_eventActive)
        {
            _nextEventTime -= Time.deltaTime;
            if (_nextEventTime <= 0f)
                TriggerRandomEvent();
        }
        else
        {
            _eventTimer -= Time.deltaTime;

            // Sync the countdown to clients at ~1Hz for the HUD.
            _syncTimer -= Time.deltaTime;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 1f;
                RequestSerialization();
            }

            if (_crewResolved)
            {
                _crewResolved = false;
                RequestSerialization();
                ResolveEvent(true);
                return;
            }

            if (_eventTimer <= 0f)
                EventTimeout();
        }
    }

    // ============================================================
    // EVENT CONTROL
    // ============================================================
    private void ScheduleNextEvent()
    {
        _nextEventTime = Random.Range(minEventInterval, maxEventInterval);
    }

    public void TriggerRandomEvent()
    {
        int eventId = Random.Range(0, eventNames.Length);
        TriggerEvent(eventId);
    }

    public void TriggerEvent(int eventId)
    {
        if (!Networking.IsMaster) return;
        if (eventId < 0 || eventId >= eventNames.Length) return;

        ActiveEvent = eventId;
        _eventTimer = eventResponseTime;
        _eventActive = true;
        _crewResolved = false;

        RequestSerialization();

        if (shipState != null)
            shipState.SetAlertLevel(2);

        if (magiSystem != null)
            magiSystem.InitiateRandomDecision();

        if (hudManager != null)
            hudManager.ShowNotification("EMERGENCY: " + eventNames[eventId]);
    }

    // Any crew member can respond at the emergency console.
    // The event's active state is derived from the synced _activeEvent,
    // so remote clients can also respond. Non-owners can't write synced
    // fields directly, so they forward the response to the master.
    public void RespondToEvent()
    {
        if (_activeEvent < 0) return;

        if (Networking.IsMaster)
        {
            _crewResolved = true;
            RequestSerialization();
        }
        else
        {
            Networking.SetOwner(Networking.Master, gameObject);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, "OnCrewRespondedRemote");
        }

        if (hudManager != null)
            hudManager.ShowNotification("CREW RESPONSE LOGGED");
    }

    public void OnCrewRespondedRemote()
    {
        if (!Networking.IsMaster) return;

        _crewResolved = true;
        RequestSerialization();
    }

    // Public so UI can respond even while a different event is active.
    public void ForceResolve()
    {
        RespondToEvent();
    }

    // ============================================================
    // RESOLUTION
    // ============================================================
    public void ResolveEvent(bool success)
    {
        if (!Networking.IsMaster) return;
        if (!_eventActive) return;

        _eventActive = false;

        if (shipState != null)
        {
            shipState.ModifyReputation(success ? 10 : -15);

            // Only drop the alert if nothing else raised it.
            if (success && shipState.AlertLevel == 2)
                shipState.SetAlertLevel(0);
        }

        if (hudManager != null)
            hudManager.ShowNotification(success ? "EMERGENCY RESOLVED" : "EMERGENCY FAILED");

        StopEventEffects();
        ScheduleNextEvent();

        ActiveEvent = -1;
        _eventTimer = 0f;
        _crewResolved = false;
        RequestSerialization();
    }

    private void EventTimeout()
    {
        ResolveEvent(false);
    }

    // ============================================================
    // EFFECTS
    // ============================================================
    private void PlayEventEffects()
    {
        switch (_activeEvent)
        {
            case 0: // Hull Breach
                PlayEffect(steamLeakEffect);
                break;
            case 1: // Fire Suppression
                PlayEffect(fireEffect);
                break;
            case 2: // Power Fluctuation
                PlayEffect(sparkEffect);
                break;
            case 3: // Coolant Leak
                PlayEffect(steamLeakEffect);
                break;
            default:
                break;
        }

        if (alarmAudio != null)
        {
            if (alarmClip != null)
                alarmAudio.clip = alarmClip;
            alarmAudio.Play();
        }
    }

    private void PlayEffect(ParticleSystem effect)
    {
        if (effect != null && !effect.isPlaying)
            effect.Play();
    }

    private void StopEventEffects()
    {
        if (fireEffect != null) fireEffect.Stop();
        if (steamLeakEffect != null) steamLeakEffect.Stop();
        if (sparkEffect != null) sparkEffect.Stop();

        if (alarmAudio != null)
            alarmAudio.Stop();
    }

    // ============================================================
    // ACCESSORS
    // ============================================================
    public int GetActiveEvent() { return _activeEvent; }

    public string GetActiveEventName()
    {
        if (_activeEvent < 0 || _activeEvent >= eventNames.Length) return "None";
        return eventNames[_activeEvent];
    }

    public float GetEventTimeRemaining() { return _eventTimer; }
    public bool IsEventActive() { return _activeEvent >= 0; }
}
