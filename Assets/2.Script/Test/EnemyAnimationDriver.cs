using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyAnimationDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string layerName = "Base Layer";
    [SerializeField] private float transitionDuration = 0.12f;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string patrolStateName = "Patrol";
    [SerializeField] private string chaseStateName = "Chase";
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string killStateName = "Kill";

    private EnemyAnimationState currentState = (EnemyAnimationState)(-1);

    public void Initialize()
    {
        ResolveAnimator();
        ConfigureAnimator();
    }

    public void Play(EnemyAnimationState state, bool force = false)
    {
        if (!force && currentState == state)
            return;

        Initialize();
        if (animator == null)
            return;

        int layerIndex = GetLayerIndex();
        string stateName = GetStateName(state);
        int fullHash = Animator.StringToHash(animator.GetLayerName(layerIndex) + "." + stateName);
        int shortHash = Animator.StringToHash(stateName);

        int playableHash;
        if (animator.HasState(layerIndex, fullHash))
            playableHash = fullHash;
        else if (animator.HasState(layerIndex, shortHash))
            playableHash = shortHash;
        else
            return;

        if (force)
        {
            animator.Play(playableHash, layerIndex, 0f);
            animator.Update(0f);
        }
        else
        {
            animator.CrossFadeInFixedTime(playableHash, transitionDuration, layerIndex);
        }

        currentState = state;
    }

    public float GetClipLength(EnemyAnimationState state, float fallback)
    {
        Initialize();
        if (animator == null || animator.runtimeAnimatorController == null)
            return fallback;

        string stateName = GetStateName(state);
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.name == stateName)
                return Mathf.Max(fallback, clip.length);
        }

        return fallback;
    }

    public bool HasRequiredStates()
    {
        Initialize();
        return HasState(EnemyAnimationState.Idle)
            && HasState(EnemyAnimationState.Patrol)
            && HasState(EnemyAnimationState.Chase)
            && HasState(EnemyAnimationState.Attack)
            && HasState(EnemyAnimationState.Kill);
    }

    private void ResolveAnimator()
    {
        if (animator != null)
            return;

        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    private void ConfigureAnimator()
    {
        if (animator == null)
            return;

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
    }

    private bool HasState(EnemyAnimationState state)
    {
        if (animator == null)
            return false;

        int layerIndex = GetLayerIndex();
        string stateName = GetStateName(state);
        int fullHash = Animator.StringToHash(animator.GetLayerName(layerIndex) + "." + stateName);
        int shortHash = Animator.StringToHash(stateName);
        return animator.HasState(layerIndex, fullHash) || animator.HasState(layerIndex, shortHash);
    }

    private int GetLayerIndex()
    {
        if (animator == null || animator.layerCount <= 0)
            return 0;

        if (!string.IsNullOrWhiteSpace(layerName))
        {
            int namedLayerIndex = animator.GetLayerIndex(layerName);
            if (namedLayerIndex >= 0)
                return namedLayerIndex;
        }

        return 0;
    }

    private string GetStateName(EnemyAnimationState state)
    {
        return state switch
        {
            EnemyAnimationState.Idle => idleStateName,
            EnemyAnimationState.Chase => chaseStateName,
            EnemyAnimationState.Attack => attackStateName,
            EnemyAnimationState.Kill => killStateName,
            _ => patrolStateName
        };
    }
}
