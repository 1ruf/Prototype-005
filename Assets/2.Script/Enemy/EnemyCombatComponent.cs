using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyCombatComponent : MonoBehaviour
{
    [System.Serializable]
    public struct LegacySettings
    {
        public GameObject DeathUi;
        public float AttackDamage;
        public float AttackAnimationDuration;
        public float KillAnimationDuration;
        public float KillKnockbackUpwardForce;
        public float ProximityDetectionRange;
    }

    [SerializeField] private bool useLegacySettings = true;
    [SerializeField] private LegacySettings settings;

    private readonly HashSet<RagdollEntityComponent> killAnimatedRagdolls = new HashSet<RagdollEntityComponent>();

    private CSHEnemy coordinator;
    private Transform visual;
    private EnemyPerceptionComponent perception;
    private bool hasKilledLocalPlayer;
    private IKnockbackable pendingKillKnockback;
    private RagdollEntityComponent pendingKillRagdoll;
    private Vector3 pendingKillKnockbackDirection;
    private float localKillAnimationEndTime;

    public bool IsLocalKillAnimationPlaying => Time.time < localKillAnimationEndTime;

    public void ConfigureLegacy(
        CSHEnemy owner,
        Transform visualRoot,
        EnemyPerceptionComponent perceptionComponent,
        LegacySettings legacySettings)
    {
        coordinator = owner;
        visual = visualRoot;
        perception = perceptionComponent;
        if (useLegacySettings)
            settings = legacySettings;

        if (settings.DeathUi != null)
            settings.DeathUi.SetActive(false);
    }

    public void CommitLegacySettings()
    {
        useLegacySettings = false;
    }

    public void HandleCollision(Collision collision)
    {
        if (collision == null || coordinator == null || !coordinator.HasStateAuthorityForServices)
            return;

        PlayerMovement player = collision.gameObject.GetComponentInParent<PlayerMovement>();
        if (player != null
            && EnemyPerceptionComponent.IsHiddenTarget(player.transform)
            && !perception.CanSee(player.transform, perception.LoseRange))
            return;

        NetworkHealthComponent health = GetPlayerComponent<NetworkHealthComponent>(collision.gameObject, player);
        RagdollEntityComponent ragdoll = GetPlayerComponent<RagdollEntityComponent>(collision.gameObject, player);
        IKnockbackable knockbackable = GetPlayerKnockbackable(collision.gameObject, player);
        Transform killedVisual = GetPlayerVisualTransform(collision.gameObject, player);

        if (health == null && ragdoll == null && player == null)
            return;

        if (IsDeadPlayerTarget(health, ragdoll) || HasAlreadyPlayedKillFor(ragdoll))
            return;

        Vector3 knockbackDirection = collision.transform.position - coordinator.transform.position;
        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude <= 0.0001f)
            knockbackDirection = visual != null ? visual.forward : coordinator.transform.forward;

        if (health != null)
        {
            health.Damage(settings.AttackDamage);
            if (IsDeadPlayerTarget(health, ragdoll))
            {
                if (ragdoll != null)
                {
                    ragdoll.Kill();
                    ragdoll.ResetRagdollVelocity();
                }

                TriggerKillAnimationOnce(knockbackable, ragdoll, knockbackDirection, killedVisual);
            }
            else
            {
                coordinator.BeginAttackAnimationFromCombat(settings.AttackAnimationDuration);
            }
        }
        else if (ragdoll != null)
        {
            ragdoll.Kill();
            ragdoll.ResetRagdollVelocity();
            TriggerKillAnimationOnce(knockbackable, ragdoll, knockbackDirection, killedVisual);
        }
        else
        {
            coordinator.BeginAttackAnimationFromCombat(settings.AttackAnimationDuration);
        }

        if (player != null && !player.IsLocalNetworkPlayer)
            return;

        if (hasKilledLocalPlayer)
            return;

        hasKilledLocalPlayer = true;

        if (settings.DeathUi == null)
            settings.DeathUi = FindDeathUI();

        if (settings.DeathUi != null)
            settings.DeathUi.SetActive(true);

        StartCoroutine(Exit());
    }

    public bool TryKillPlayer(PlayerMovement player)
    {
        if (player == null || coordinator == null || !coordinator.HasStateAuthorityForServices)
            return false;

        NetworkHealthComponent health = GetPlayerComponent<NetworkHealthComponent>(player.gameObject, player);
        RagdollEntityComponent ragdoll = GetPlayerComponent<RagdollEntityComponent>(player.gameObject, player);
        IKnockbackable knockbackable = GetPlayerKnockbackable(player.gameObject, player);
        Transform killedVisual = GetPlayerVisualTransform(player.gameObject, player);

        if (IsDeadPlayerTarget(health, ragdoll))
            return false;

        Vector3 knockbackDirection = player.transform.position - coordinator.transform.position;
        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude <= 0.0001f)
            knockbackDirection = visual != null ? visual.forward : coordinator.transform.forward;

        if (health != null)
            health.Kill();

        if (ragdoll != null)
        {
            ragdoll.Kill();
            ragdoll.ResetRagdollVelocity();
        }

        TriggerKillAnimationOnce(knockbackable, ragdoll, knockbackDirection, killedVisual);
        return true;
    }

    public bool TryKillCompromisedHiddenTarget(Transform target)
    {
        if (target == null || !EnemyPerceptionComponent.IsCompromisedHiddenTarget(target))
            return false;

        PlayerMovement player = target.GetComponentInParent<PlayerMovement>();
        if (player == null)
            return false;

        float killDistance = Mathf.Max(0.75f, settings.ProximityDetectionRange);
        if (EnemyPerceptionComponent.GetFlatDistance(coordinator.transform.position, target.position) > killDistance)
            return false;

        return TryKillPlayer(player);
    }

    public void ApplyKillKnockback(float force)
    {
        if (coordinator == null || !coordinator.HasStateAuthorityForServices || pendingKillKnockback == null)
            return;

        pendingKillKnockback.ApplyKnockback(
            pendingKillKnockbackDirection,
            Mathf.Max(0f, force),
            Mathf.Max(0f, settings.KillKnockbackUpwardForce));
    }

    public void ClearPendingKill()
    {
        pendingKillKnockback = null;
        pendingKillRagdoll = null;
        localKillAnimationEndTime = 0f;
    }

    private void TriggerKillAnimationOnce(
        IKnockbackable knockbackable,
        RagdollEntityComponent ragdoll,
        Vector3 knockbackDirection,
        Transform killedVisual)
    {
        if (ragdoll != null)
            killAnimatedRagdolls.Add(ragdoll);

        TriggerKillAnimation(knockbackable, ragdoll, knockbackDirection, killedVisual);
    }

    private void TriggerKillAnimation(
        IKnockbackable knockbackable,
        RagdollEntityComponent ragdoll,
        Vector3 knockbackDirection,
        Transform killedVisual)
    {
        if (!coordinator.HasStateAuthorityForServices)
            return;

        coordinator.StopNavigationFromCombat();
        RotateVisualTowardKillTarget(killedVisual);
        pendingKillKnockback = knockbackable;
        pendingKillRagdoll = ragdoll;
        pendingKillKnockbackDirection = knockbackDirection.sqrMagnitude > 0.0001f
            ? knockbackDirection.normalized
            : coordinator.transform.forward;

        float fallback = Mathf.Max(0.1f, settings.KillAnimationDuration);
        float killDuration = coordinator.GetAnimationClipLengthForServices(EnemyAnimationState.Kill, fallback);
        localKillAnimationEndTime = Time.time + killDuration;
        coordinator.BeginKillAnimationFromCombat(killDuration);
    }

    private void RotateVisualTowardKillTarget(Transform killedVisual)
    {
        if (killedVisual == null)
            return;

        Transform rotatingTransform = visual != null ? visual : coordinator.transform;
        Vector3 direction = killedVisual.position - rotatingTransform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        rotatingTransform.rotation = targetRotation;
        coordinator.PublishNetworkRotationFromServices(targetRotation);
    }

    private bool HasAlreadyPlayedKillFor(RagdollEntityComponent ragdoll)
    {
        return ragdoll != null && killAnimatedRagdolls.Contains(ragdoll);
    }

    private static bool IsDeadPlayerTarget(NetworkHealthComponent health, RagdollEntityComponent ragdoll)
    {
        return (health != null && (health.IsDead || health.CurrentHealth <= 0f))
            || (ragdoll != null && (ragdoll.IsDead || ragdoll.IsRagdollEnabled));
    }

    private static T GetPlayerComponent<T>(GameObject source, PlayerMovement player) where T : Component
    {
        T component = source.GetComponentInParent<T>();
        if (component != null)
            return component;

        return player != null ? player.GetComponentInChildren<T>(true) : null;
    }

    private static Transform GetPlayerVisualTransform(GameObject source, PlayerMovement player)
    {
        Transform playerRoot = player != null ? player.transform : source.GetComponentInParent<PlayerMovement>()?.transform;
        if (playerRoot == null)
            return source.transform;

        Transform playerVisual = FindChildByName(playerRoot, "Visual");
        if (playerVisual != null)
            return playerVisual;

        Animator animator = playerRoot.GetComponentInChildren<Animator>(true);
        if (animator != null)
            return animator.transform;

        RagdollPartComponent ragdollPart = source.GetComponentInParent<RagdollPartComponent>();
        if (ragdollPart != null)
            return ragdollPart.transform;

        return playerRoot;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindChildByName(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static IKnockbackable GetPlayerKnockbackable(GameObject source, PlayerMovement player)
    {
        IKnockbackable knockbackable = source.GetComponentInParent<IKnockbackable>();
        if (knockbackable != null || player == null)
            return knockbackable;

        MonoBehaviour[] behaviours = player.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IKnockbackable candidate)
                return candidate;
        }

        return null;
    }

    private static GameObject FindDeathUI()
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject candidate in objects)
        {
            if (candidate.name == "DiedUI" && candidate.scene.IsValid())
                return candidate;
        }

        return null;
    }

    private static IEnumerator Exit()
    {
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }
}
