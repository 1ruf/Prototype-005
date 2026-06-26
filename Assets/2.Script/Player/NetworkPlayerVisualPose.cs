using System;
using UnityEngine;

[DefaultExecutionOrder(11000)]
[DisallowMultipleComponent]
public class NetworkPlayerVisualPose : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private NetworkPlayerItemHolder itemHolder;
    [SerializeField] private RagdollEntityComponent ragdoll;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform leftLowerArm;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform rightLowerArm;
    [SerializeField] private Transform rightHand;
    [Header("Prefab Pose Targets")]
    [SerializeField] private Transform leftUpperArmAimTarget;
    [SerializeField] private Transform leftLowerArmAimTarget;
    [SerializeField] private Transform leftHandPoseTarget;
    [SerializeField] private Vector3 headWorldEulerOffset = Vector3.zero;
    [SerializeField] private Vector3 headPitchAxis = Vector3.right;
    [HideInInspector]
    [SerializeField] private Vector3 leftUpperArmAimEulerOffset = new Vector3(0f, -86f, -12f);
    [HideInInspector]
    [SerializeField] private Vector3 leftLowerArmAimEulerOffset = new Vector3(0f, -84f, -8f);
    [HideInInspector]
    [SerializeField] private Vector3 leftHandWorldEulerOffset = new Vector3(0f, 0f, 0f);
    [SerializeField] private bool poseSupportHand = false;
    [HideInInspector]
    [SerializeField] private Vector3 rightUpperArmAimEulerOffset = new Vector3(0f, 86f, 12f);
    [HideInInspector]
    [SerializeField] private Vector3 rightLowerArmAimEulerOffset = new Vector3(0f, 84f, 8f);
    [HideInInspector]
    [SerializeField] private Vector3 rightHandWorldEulerOffset = new Vector3(0f, 0f, 0f);

    private bool headBaseCached;
    private Quaternion headBaseLocalRotation = Quaternion.identity;
    private bool shoulderPivotOffsetCached;
    private Vector3 shoulderPivotLocalOffset;

    private void Awake()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (IsDeadOrRagdolled())
            return;

        if (!TryGetNetworkPitch(out float pitch))
            return;

        ApplyHeadPose(pitch);

        if (itemHolder == null || !itemHolder.IsHoldingItemForVisualPose)
            return;

        if (itemHolder.TryGetHeldItemWorldPose(out Vector3 heldPosition, out Quaternion heldRotation))
        {
            if (!itemHolder.IsLocalPlayerForPresentation)
                ApplyShoulderPivotToHeldItem(ref heldPosition, pitch);

            ApplyHeldItemPose(heldPosition, heldRotation);
        }
    }

    private bool TryGetNetworkPitch(out float pitch)
    {
        pitch = 0f;

        if (playerMovement == null)
            return false;

        try
        {
            pitch = playerMovement.CameraPitch;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyHeadPose(float pitch)
    {
        if (head == null)
            return;

        if (!headBaseCached)
        {
            headBaseLocalRotation = head.localRotation;
            headBaseCached = true;
        }

        Quaternion pitchRotation = Quaternion.AngleAxis(-pitch, headPitchAxis.normalized);
        head.localRotation = headBaseLocalRotation * pitchRotation * Quaternion.Euler(headWorldEulerOffset);
    }

    private void ApplyHeldItemPose(Vector3 heldPosition, Quaternion heldRotation)
    {
        Vector3 upperArmAimPosition = leftUpperArmAimTarget != null ? leftUpperArmAimTarget.position : heldPosition;
        Vector3 lowerArmAimPosition = leftLowerArmAimTarget != null ? leftLowerArmAimTarget.position : heldPosition;
        AimBoneAt(leftUpperArm, upperArmAimPosition, leftUpperArmAimEulerOffset);
        AimBoneAt(leftLowerArm, lowerArmAimPosition, leftLowerArmAimEulerOffset);

        if (leftHand != null)
        {
            Quaternion handRotation = heldRotation * (leftHandPoseTarget != null ? leftHandPoseTarget.localRotation : Quaternion.Euler(leftHandWorldEulerOffset));
            leftHand.SetPositionAndRotation(heldPosition, handRotation);
        }

        if (!poseSupportHand)
            return;

        AimBoneAt(rightUpperArm, heldPosition, rightUpperArmAimEulerOffset);
        AimBoneAt(rightLowerArm, heldPosition, rightLowerArmAimEulerOffset);

        if (rightHand != null)
            rightHand.rotation = heldRotation * Quaternion.Euler(rightHandWorldEulerOffset);
    }

    private void ApplyShoulderPivotToHeldItem(ref Vector3 heldPosition, float pitch)
    {
        if (leftUpperArm == null || itemHolder == null || itemHolder.HeldItemTransform == null)
            return;

        Vector3 pivotPosition = leftUpperArm.position;
        if (!shoulderPivotOffsetCached)
        {
            shoulderPivotLocalOffset = Quaternion.Inverse(transform.rotation) * (heldPosition - pivotPosition);
            shoulderPivotOffsetCached = true;
        }

        Vector3 rotatedOffset = transform.rotation * (Quaternion.Euler(pitch, 0f, 0f) * shoulderPivotLocalOffset);
        heldPosition = pivotPosition + rotatedOffset;
        itemHolder.HeldItemTransform.position = heldPosition;
    }

    private static void AimBoneAt(Transform bone, Vector3 targetPosition, Vector3 eulerOffset)
    {
        if (bone == null)
            return;

        Vector3 direction = targetPosition - bone.position;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        bone.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(eulerOffset);
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (itemHolder == null)
            itemHolder = GetComponent<NetworkPlayerItemHolder>();

        if (ragdoll == null)
            ragdoll = GetComponent<RagdollEntityComponent>();

        if (visualAnimator == null)
            visualAnimator = ResolveVisualAnimator();

        if (visualAnimator == null)
            return;

        if (head == null)
            head = ResolveBone(HumanBodyBones.Head, "Head", "Bip001 Head");

        if (leftUpperArm == null)
            leftUpperArm = ResolveBone(HumanBodyBones.LeftUpperArm, "LeftArm", "LeftUpperArm", "LeftShoulder", "Bip001 L UpperArm");

        if (leftLowerArm == null)
            leftLowerArm = ResolveBone(HumanBodyBones.LeftLowerArm, "LeftForeArm", "LeftLowerArm", "LeftForearm", "Bip001 L Forearm");

        if (leftHand == null)
            leftHand = ResolveBone(HumanBodyBones.LeftHand, "LeftHand", "Bip001 L Hand");

        if (rightUpperArm == null)
            rightUpperArm = ResolveBone(HumanBodyBones.RightUpperArm, "RightArm", "RightUpperArm", "RightShoulder", "Bip001 R UpperArm");

        if (rightLowerArm == null)
            rightLowerArm = ResolveBone(HumanBodyBones.RightLowerArm, "RightForeArm", "RightLowerArm", "RightForearm", "Bip001 R Forearm");

        if (rightHand == null)
            rightHand = ResolveBone(HumanBodyBones.RightHand, "RightHand", "Bip001 R Hand");
    }

    private Animator ResolveVisualAnimator()
    {
        Transform visual = FindChildByName(transform, "Visual");
        Animator animator = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
        return animator != null ? animator : GetComponentInChildren<Animator>(true);
    }

    private Transform ResolveBone(HumanBodyBones humanBone, params string[] names)
    {
        if (visualAnimator != null && visualAnimator.isHuman)
        {
            Transform bone = visualAnimator.GetBoneTransform(humanBone);
            if (bone != null)
                return bone;
        }

        foreach (string boneName in names)
        {
            Transform found = FindChildByName(visualAnimator.transform, boneName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (IsBoneNameMatch(root.name, childName))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsBoneNameMatch(string candidate, string requested)
    {
        if (string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase))
            return true;

        if (candidate.EndsWith(":" + requested, StringComparison.OrdinalIgnoreCase))
            return true;

        string compactCandidate = candidate.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        string compactRequested = requested.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return compactCandidate.EndsWith(compactRequested, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDeadOrRagdolled()
    {
        if (ragdoll == null)
            return false;

        try
        {
            return ragdoll.IsDead || ragdoll.IsRagdollEnabled;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
