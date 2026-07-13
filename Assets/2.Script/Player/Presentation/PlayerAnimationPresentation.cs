using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAnimationPresentation : MonoBehaviour
{
    private const string IdleStateName = "Base Layer.Idle";
    private const string RunStateName = "Base Layer.Run";
    private const string FirstPersonWalkStateName = "Walk";
    private const string FirstPersonRunningParameter = "Running";
    private const string VisualAnimatorControllerResourcePath = "PlayerVisual";

    [Header("Compatibility")]
    [SerializeField] private bool usePlayerMovementSettings = true;

    [Header("First Person")]
    [SerializeField] private Animator firstPersonAnimator;

    [Header("Third Person")]
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private RuntimeAnimatorController visualAnimatorController;
    [SerializeField] private float visualCrossFadeTime = 0.12f;

    private PlayerMovement playerMovement;
    private Renderer[] visualRenderers;
    private string lastVisualStateName;
    private float lastVisualAnimatorSpeed = -1f;

    public void Initialize(
        PlayerMovement movement,
        Animator legacyFirstPersonAnimator,
        Animator legacyVisualAnimator,
        RuntimeAnimatorController legacyVisualAnimatorController,
        float legacyVisualCrossFadeTime)
    {
        playerMovement = movement;

        if (usePlayerMovementSettings)
        {
            if (legacyFirstPersonAnimator != null)
                firstPersonAnimator = legacyFirstPersonAnimator;

            if (legacyVisualAnimator != null)
                visualAnimator = legacyVisualAnimator;

            if (legacyVisualAnimatorController != null)
                visualAnimatorController = legacyVisualAnimatorController;

            visualCrossFadeTime = legacyVisualCrossFadeTime;
        }

        ResolveVisualAnimator();
    }

    public void HandleOwnerEnabled()
    {
        if (firstPersonAnimator != null)
            firstPersonAnimator.Play(FirstPersonWalkStateName);

        ResolveVisualAnimator();
        ApplyVisualAnimation(false, 1f, true);
    }

    public void ConfigureLocalPlayer(bool local)
    {
        SetLocalVisualRenderersVisible(!local);
    }

    public float CalculateAnimationSpeed(bool moving, bool sprinting, float movementSpeed, float baseSpeed, float sprintMultiplier)
    {
        if (!moving)
            return 1f;

        float safeBaseSpeed = Mathf.Max(baseSpeed, 0.001f);
        float speedRatio = Mathf.Max(0.1f, movementSpeed / safeBaseSpeed);
        if (sprinting)
            speedRatio = Mathf.Max(speedRatio, sprintMultiplier);

        return speedRatio;
    }

    public void PresentMotion(bool moving, bool sprinting, float animationSpeed, bool force = false)
    {
        if (firstPersonAnimator != null)
        {
            firstPersonAnimator.SetBool(FirstPersonRunningParameter, sprinting);
            firstPersonAnimator.speed = moving ? Mathf.Max(0.01f, animationSpeed) : 0f;
        }

        ApplyVisualAnimation(moving, animationSpeed, force);
    }

    private void ApplyVisualAnimation(bool moving, float animationSpeed, bool force)
    {
        ResolveVisualAnimator();
        if (visualAnimator == null)
            return;

        string targetStateName = moving ? RunStateName : IdleStateName;
        if (force || lastVisualStateName != targetStateName)
        {
            if (force)
                visualAnimator.Play(targetStateName, 0, 0f);
            else
                visualAnimator.CrossFade(targetStateName, visualCrossFadeTime, 0);

            lastVisualStateName = targetStateName;
        }

        float targetSpeed = Mathf.Max(0.01f, animationSpeed);
        if (force || !Mathf.Approximately(lastVisualAnimatorSpeed, targetSpeed))
        {
            visualAnimator.speed = targetSpeed;
            lastVisualAnimatorSpeed = targetSpeed;
        }
    }

    private void ResolveVisualAnimator()
    {
        Transform ownerRoot = playerMovement != null ? playerMovement.transform : transform.root;
        if (visualAnimator == null)
        {
            Transform visual = FindChildByName(ownerRoot, "Visual");
            if (visual != null)
            {
                if (!visual.gameObject.activeSelf)
                    visual.gameObject.SetActive(true);

                visualAnimator = visual.GetComponentInChildren<Animator>(true);
                if (visualAnimator == null)
                    visualAnimator = visual.gameObject.AddComponent<Animator>();
            }
        }

        if (visualAnimator == null)
            visualAnimator = ownerRoot.GetComponentInChildren<Animator>(true);

        if (visualAnimator == null)
            return;

        if (!visualAnimator.gameObject.activeSelf)
            visualAnimator.gameObject.SetActive(true);

        if (visualAnimator.runtimeAnimatorController == null)
            visualAnimator.runtimeAnimatorController = ResolveVisualAnimatorController();

        visualAnimator.enabled = true;
        visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        visualAnimator.updateMode = AnimatorUpdateMode.Normal;
        visualAnimator.applyRootMotion = false;
    }

    private RuntimeAnimatorController ResolveVisualAnimatorController()
    {
        if (visualAnimatorController != null)
            return visualAnimatorController;

        visualAnimatorController = Resources.Load<RuntimeAnimatorController>(VisualAnimatorControllerResourcePath);
        return visualAnimatorController;
    }

    private void SetLocalVisualRenderersVisible(bool visible)
    {
        Transform ownerRoot = playerMovement != null ? playerMovement.transform : transform.root;
        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            Transform visual = FindChildByName(ownerRoot, "Visual");
            if (visual != null)
                visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
        }

        if (visualRenderers == null)
            return;

        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer != null)
                visualRenderer.enabled = visible;
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
