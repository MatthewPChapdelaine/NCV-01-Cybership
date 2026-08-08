// ============================================================
// NCV-01 Cybership - Tactical Target
//
// Attach to target prefabs spawned by TacticalStation. Auto-despawns
// after its lifetime; reports hits and time-outs back to the station.
// ============================================================

using UdonSharp;
using UnityEngine;

public class TacticalTarget : UdonSharpBehaviour
{
    public TacticalStation station;
    public float lifetime = 6f;

    private float _timeLeft;

    void OnEnable()
    {
        _timeLeft = lifetime;
    }

    void Update()
    {
        _timeLeft -= Time.deltaTime;
        if (_timeLeft <= 0f)
            TimeOut();
    }

    public void OnHit()
    {
        if (station != null)
            station.OnTargetHit(gameObject);
        else
            Destroy(gameObject);
    }

    private void TimeOut()
    {
        if (station != null)
            station.OnTargetMiss();

        Destroy(gameObject);
    }
}
