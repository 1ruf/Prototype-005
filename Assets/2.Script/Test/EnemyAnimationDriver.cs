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

        if (animator.HasState(layerIndex, fullHash))
            animator.CrossFadeInFixedTime(fullHash, transitionDuration, layerIndex);
        else if (animator.HasState(layerIndex, shortHash))
            animator.CrossFadeInFixedTime(shortHash, transitionDuration, layerIndex);
        else
            return;

        currentState = state;
    }

    public bool HasRequiredStates()
    {
        Initialize();
        return HasState(EnemyAnimationState.Idle)
            && HasState(EnemyAnimationState.Patrol)
            && HasState(EnemyAnimationState.Chase);
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
            _ => patrolStateName
        };
    }
}
