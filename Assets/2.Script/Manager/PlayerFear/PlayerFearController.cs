using UnityEngine;

public sealed class PlayerFearController : GameControllerBase
{
    private readonly IPlayerFearDetector detector;
    private readonly IPlayerFearFeedback feedback;
    private readonly float triggerCooldown;
    private readonly float chaseStartedCooldown;

    private CSHEnemy currentVisibleThreat;
    private bool currentVisibleThreatWasChasing;
    private float nextAllowedTriggerTime;
    private float nextAllowedChaseStartedTime;

    public PlayerFearController(IPlayerFearDetector detector, IPlayerFearFeedback feedback, float triggerCooldown, float chaseStartedCooldown)
    {
        this.detector = detector;
        this.feedback = feedback;
        this.triggerCooldown = Mathf.Max(0f, triggerCooldown);
        this.chaseStartedCooldown = Mathf.Max(0f, chaseStartedCooldown);
    }

    public override GameControllerBase Init()
    {
        return this;
    }

    public override void Update()
    {
        if (detector == null || feedback == null)
            return;

        if (!detector.TryGetVisibleThreat(out PlayerFearThreat threat))
        {
            currentVisibleThreat = null;
            currentVisibleThreatWasChasing = false;
            return;
        }

        if (threat.Enemy == currentVisibleThreat)
        {
            if (!currentVisibleThreatWasChasing && threat.IsChasingLocalPlayer)
                PlayChaseStarted(threat);

            currentVisibleThreatWasChasing = threat.IsChasingLocalPlayer;
            return;
        }

        currentVisibleThreat = threat.Enemy;
        currentVisibleThreatWasChasing = threat.IsChasingLocalPlayer;

        if (Time.time < nextAllowedTriggerTime)
            return;

        nextAllowedTriggerTime = Time.time + triggerCooldown;
        feedback.Play(threat);
    }

    private void PlayChaseStarted(PlayerFearThreat threat)
    {
        if (Time.time < nextAllowedChaseStartedTime)
            return;

        nextAllowedChaseStartedTime = Time.time + chaseStartedCooldown;
        feedback.PlayChaseStarted(threat);
    }
}
