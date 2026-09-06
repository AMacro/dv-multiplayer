using DV.Interaction;
using DV.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Editor.Components.Player;
using Multiplayer.Networking.Data.Player;
using System.Collections.Generic;
using UnityChan;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

/// <summary>
/// Represents a networked player in the multiplayer environment, handling movement, item holding, and visual state
/// </summary>
public class NetworkedPlayer : MonoBehaviour
{
    #region Static Setup
    private static Vector3 itemAnchorOffset = new(0.2f, 1.5f, 0.4f);

    /// <summary>
    /// Captures the standard offset position for held items relative to the player transform
    /// for mapping to a NetworkedPlayer.
    /// This must be called as soon as the world is loaded, before the local player moves or crouches.
    /// </summary>
    public static void CaptureItemAnchorOffset()
    {
        //todo: there's some minor inconsistency with return values and may be related to:
        // - the direction/rotation of the camera
        // - player loading status (maybe posistion hasn't settled yet)
        if (!VRManager.IsVREnabled())
        {
            itemAnchorOffset = PlayerManager.PlayerTransform.InverseTransformPoint(ItemPositionController.Instance.itemAnchor.position);
            Multiplayer.LogDebug(() => $"NetworkedPlayer.CaptureItemAnchorOffset() itemAnchorOffset: {itemAnchorOffset}");
        }
    }
    #endregion

    private const float LERP_SPEED = 5.0f;
    private const float MAX_LEAN_ANGLE = 50f;
    private const float LEAN_SMOOTHING_DURATION = 0.1f;
    private const float HEAD_LEAN_MULTIPLIER = 1.5f;

    public byte PlayerId { get; set; }
    public string CrewName { get; set; }
    public bool IsVR { get; set; }

    private GameObject playerModel;
    private AnimationHandler animationHandler;
    private NameTag nameTag;
    private int ping;
    private NetworkedPlayerIKHandler ikHandler;

    private string username;

    public string Username
    {
        get => username;
        set
        {
            username = value;
            nameTag?.SetUsername(value);
        }
    }

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(CrewName))
                return username;
            return $"[{CrewName}] {username}";
        }
    }

    // World Positioning
    internal bool IsOnCar { get; private set; }
    internal NetworkedTrainCar OccupiedCar { get; private set; }

    private Transform selfTransform;
    private PlayerPostureFlags currentPosture;

    // Head tracking
    private Transform headTransform;
    private Quaternion headBaseWorldRotation = Quaternion.identity;
    private float currentHeadPitch;
    private float targetHeadPitch;

    // Spine tracking
    private Transform spineTransform;
    private Quaternion spineBaseWorldRotation = Quaternion.identity;

    // Player movement and rotation
    private Vector3 targetPos;
    private Quaternion targetRotation;
    private Vector2 moveDir;
    private Vector2 targetMoveDir;

    private float currentLeanAngle;
    private float angleSmoothRefVel;
    private float currentSitHeight;

    // VR hand tracking — targets set from incoming packets
    private Transform leftHandTransform;
    private Transform rightHandTransform;
    private Vector3 targetLeftHandPos;
    private Quaternion targetLeftHandRot = Quaternion.identity;
    private Vector3 targetRightHandPos;
    private Quaternion targetRightHandRot = Quaternion.identity;

    // Current lerped values — tracked independently to avoid Animator fighting
    private Vector3 currentLeftHandWorldPos;
    private Quaternion currentLeftHandWorldRot = Quaternion.identity;
    private Vector3 currentRightHandWorldPos;
    private Quaternion currentRightHandWorldRot = Quaternion.identity;
    private bool handTrackingInitialized;

    // Inventory and item holding
    private GameObject inventoryRoot;
    public GameObject RightHandItemGO { get; private set; }
    private readonly List<Collider> disabledRHItemColliders = [];
    public GameObject LeftHandItemGO { get; private set; }
    private readonly List<Collider> disabledLHItemColliders = [];
    private Vector3? itemHoldPos;
    private Quaternion? itemHoldRot;

    WindPhysicsController windController;

    private bool isCulled;
    public bool IsCulled
    {
        get => isCulled;
        set
        {
            if (isCulled == value)
                return;

            isCulled = value;

            playerModel?.SetActive(!value);
            nameTag?.gameObject.SetActive(!value);
        }
    }

    protected void Awake()
    {
        nameTag = GetComponentInChildren<NameTag>();

        nameTag.LookTarget = PlayerManager.ActiveCamera.transform;
        PlayerManager.CameraChanged += () => nameTag.LookTarget = PlayerManager.ActiveCamera.transform;

        if (name != null)
            nameTag.SetUsername(name);

        OnSettingsUpdated(Multiplayer.Settings);
        Settings.OnSettingsUpdated += OnSettingsUpdated;

        selfTransform = transform;
        targetPos = selfTransform.position;
        targetRotation = selfTransform.rotation;

        targetHeadPitch = 0f;

        moveDir = Vector2.zero;
        targetMoveDir = Vector2.zero;

        currentPosture = PlayerPostureFlags.None;

        var clampedSitHeight = Mathf.Clamp
            (
                CustomFirstPersonController.PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT
            );
        currentSitHeight = Mathf.InverseLerp
            (
                CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT,
                clampedSitHeight
            );
    }

    protected void OnDestroy()
    {
        Settings.OnSettingsUpdated -= OnSettingsUpdated;
    }

    private void OnSettingsUpdated(Settings settings)
    {
        nameTag.ShowUsername(settings.ShowNameTags);
        nameTag.ShowPing(settings.ShowNameTags && settings.ShowPingInNameTags);
    }

    public void ChangeModel(GameObject newModel)
    {
        if (newModel == playerModel || newModel == null)
            return;

        if (playerModel != null)
        {
            animationHandler = null;
            DestroyImmediate(playerModel);
            headTransform = null;
            leftHandTransform = null;
            rightHandTransform = null;
            handTrackingInitialized = false;
            windController = null;
        }

        playerModel = Instantiate(newModel, transform);
        animationHandler = playerModel.GetComponent<AnimationHandler>();

        // If the model is using wind physics, e.g. for hair, add the WindPhysicsController to manage effects on and off the car
        if (playerModel.GetComponentInChildren<SpringManager>(true) != null)
            windController = playerModel.AddComponent<WindPhysicsController>();

        var animator = playerModel.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            if (IsVR)
            {
                // Track VR Networked player's IK state for hands and feet
                ikHandler = animator.gameObject.AddComponent<NetworkedPlayerIKHandler>();
                ikHandler.IsActive = false;
            }

            headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform == null)
                Multiplayer.LogWarning($"Head bone not found in model {newModel.name}. Head tracking will not work");

            spineTransform = animator.GetBoneTransform(HumanBodyBones.Spine);

            leftHandTransform = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHandTransform = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftHandTransform == null || rightHandTransform == null)
                Multiplayer.LogWarning($"Hand bones not found in model {newModel.name}. VR hand tracking will not work");
        }
        else
        {
            Multiplayer.LogWarning($"Animator not found in model {newModel.name}. Tracking will not work");
        }

        if (spineTransform == null)
        {
            // Fall back to using the model's transform if the spine bone is not found
            spineTransform = playerModel.transform;
        }

        spineBaseWorldRotation = Quaternion.Inverse(selfTransform.rotation) * spineTransform.rotation;

        if (headTransform != null)
            headBaseWorldRotation = Quaternion.Inverse(selfTransform.rotation) * headTransform.rotation;

        SetPosture(currentPosture);

        if (IsCulled)
            playerModel.SetActive(false);
    }

    public void SetPing(int ping)
    {
        nameTag?.SetPing(ping);
        this.ping = ping;
    }

    public int GetPing()
    {
        return ping;
    }

    protected void Update()
    {
        if (IsCulled)
        {
            if (IsOnCar)
                selfTransform.localPosition = targetPos;
            else
                selfTransform.position = targetPos + WorldMover.currentMove;

            selfTransform.rotation = targetRotation;
            return;
        }

        float t = Time.deltaTime * LERP_SPEED;

        Vector3 position = Vector3.Lerp(
            IsOnCar ? selfTransform.localPosition : selfTransform.position,
            IsOnCar ? targetPos : targetPos + WorldMover.currentMove,
            t);

        // Calculate smoothed head pitch for use in VR and nonVR head positioning and nonVR item positioning
        currentHeadPitch = Mathf.Lerp(currentHeadPitch, targetHeadPitch, t);

        moveDir = Vector2.Lerp(moveDir, targetMoveDir, t);
        animationHandler?.SetMoveDir(moveDir);

        if (!IsVR)
            animationHandler?.SetSitHeight(currentSitHeight);

        if (IsOnCar && OccupiedCar != null)
        {
            selfTransform.localPosition = position;

            // Calculate a world-up-respecting rotation
            // This creates a rotation where Y points up in world space
            // but the forward direction aligns with the car's forward projected onto the horizontal plane
            Vector3 carForward = OccupiedCar.transform.forward;
            Vector3 worldUp = Vector3.up;

            // Project car's forward onto the horizontal plane
            Vector3 horizontalForward = Vector3.ProjectOnPlane(carForward, worldUp).normalized;
            if (horizontalForward.sqrMagnitude < 0.001f)
                horizontalForward = Vector3.ProjectOnPlane(OccupiedCar.transform.right, worldUp).normalized;

            // Create base orientation aligned with world up but facing car's forward direction
            Quaternion baseRotation = Quaternion.LookRotation(horizontalForward, worldUp);

            // Calculate the relative rotation: how much is the player rotated relative to the car?
            float carYaw = baseRotation.eulerAngles.y;
            float playerYaw = targetRotation.eulerAngles.y;
            float relativeYaw = playerYaw - carYaw;

            // Apply the desired Y rotation (player's facing direction) on top of this base rotation
            Quaternion targetWorldRotation = baseRotation * Quaternion.Euler(0, relativeYaw, 0);

            // Apply rotation in world space despite being a child transform
            selfTransform.rotation = Quaternion.Lerp(selfTransform.rotation, targetWorldRotation, t);
        }
        else
        {
            selfTransform.position = position;
            selfTransform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, t);
        }

        if (RightHandItemGO != null)
        {
            RightHandItemGO.transform.position = selfTransform.position + GetItemOffsetFromPlayer();
            RightHandItemGO.transform.rotation = selfTransform.rotation * (itemHoldRot ?? Quaternion.identity);//ItemPositionController.Instance.itemAnchor.localRotation);
        }
    }

    /// <summary>
    /// LateUpdate is called after animators have updated, allowing us to apply our own transformations on top of the animated posture.
    /// </summary>
    protected void LateUpdate()
    {
        if (IsCulled)
            return;

        if (!IsVR)
        {
            float targetLeanAngle = 0f;
            if (currentPosture.HasFlag(PlayerPostureFlags.LeanLeft))
                targetLeanAngle = MAX_LEAN_ANGLE;
            else if (currentPosture.HasFlag(PlayerPostureFlags.LeanRight))
                targetLeanAngle = -MAX_LEAN_ANGLE;

            currentLeanAngle = Mathf.SmoothDamp(currentLeanAngle, targetLeanAngle, ref angleSmoothRefVel, LEAN_SMOOTHING_DURATION);
        }

        ApplySpineAndHeadRotation();

        if (IsVR)
            ApplyHandTracking();
    }

    private void ApplySpineAndHeadRotation()
    {
        if (spineTransform != null)
        {
            // Reconstruct the base animated posture for this model in world space
            Quaternion currentModelSpineBase = selfTransform.rotation * spineBaseWorldRotation;

            // Define standard look/lean vectors using the main uniform player root
            // Side lean is always spinning around the root's global FORWARD axis
            Quaternion leanOffset = Quaternion.AngleAxis(currentLeanAngle, selfTransform.forward);

            // Directly assign the uniform world rotation 
            spineTransform.rotation = leanOffset * currentModelSpineBase;
        }

        if (headTransform == null)
            return;

        Quaternion currentModelHeadBase = selfTransform.rotation * headBaseWorldRotation;
        Quaternion pitchRotation = Quaternion.AngleAxis(currentHeadPitch, selfTransform.right);
        Quaternion leanTiltRotation = Quaternion.AngleAxis(currentLeanAngle * HEAD_LEAN_MULTIPLIER, selfTransform.forward);
        headTransform.rotation = pitchRotation * leanTiltRotation * currentModelHeadBase;
    }

    private void ApplyHandTracking()
    {
        if (!handTrackingInitialized || ikHandler == null)
            return;

        float t = Time.deltaTime * LERP_SPEED;

        currentLeftHandWorldPos = Vector3.Lerp(currentLeftHandWorldPos, targetLeftHandPos, t);
        currentLeftHandWorldRot = Quaternion.Lerp(currentLeftHandWorldRot, targetLeftHandRot, t);

        currentRightHandWorldPos = Vector3.Lerp(currentRightHandWorldPos, targetRightHandPos, t);
        currentRightHandWorldRot = Quaternion.Lerp(currentRightHandWorldRot, targetRightHandRot, t);

        ikHandler.LeftHandPosition = selfTransform.position + targetRotation * currentLeftHandWorldPos;
        ikHandler.LeftHandRotation = targetRotation * currentLeftHandWorldRot;
        ikHandler.RightHandPosition = selfTransform.position + targetRotation * currentRightHandWorldPos;
        ikHandler.RightHandRotation = targetRotation * currentRightHandWorldRot;
    }

    /// <summary>
    /// Feed networked tracking data into the NetworkedPlayer to update its position, rotation, and posture.
    /// </summary>
    /// <param name="trackingData"></param>
    /// <param name="posture"></param>
    /// <param name="movePacketIsOnCar"></param>
    public void UpdatePosition(PlayerTrackingData trackingData, PlayerPostureFlags posture, bool movePacketIsOnCar)
    {
        if (trackingData.Position.HasValue)
            targetPos = trackingData.Position.Value;

        if (trackingData.MoveDirection.HasValue)
        {
            targetMoveDir = trackingData.MoveDirection.Value;
        }

        if (trackingData.SitHeight.HasValue)
            currentSitHeight = Mathf.Clamp01(trackingData.SitHeight.Value);

        SetPosture(posture);

        if (IsOnCar != movePacketIsOnCar)
            return;

        if (trackingData.RotationY.HasValue)
            targetRotation = Quaternion.Euler(0, trackingData.RotationY.Value, 0);

        if (trackingData.LookPosition.HasValue)
            targetHeadPitch = trackingData.LookPosition.Value;

        if (trackingData.LeftHandPosition.HasValue)
            targetLeftHandPos = trackingData.LeftHandPosition.Value;
        if (trackingData.LeftHandRotation.HasValue)
            targetLeftHandRot = trackingData.LeftHandRotation.Value;
        if (trackingData.RightHandPosition.HasValue)
            targetRightHandPos = trackingData.RightHandPosition.Value;
        if (trackingData.RightHandRotation.HasValue)
            targetRightHandRot = trackingData.RightHandRotation.Value;

        // Todo: improve sync, the arms can be a little spaghetti-y
        if (!handTrackingInitialized)
        {
            currentLeftHandWorldPos = targetLeftHandPos;
            currentLeftHandWorldRot = targetLeftHandRot;
            currentRightHandWorldPos = targetRightHandPos;
            currentRightHandWorldRot = targetRightHandRot;
            handTrackingInitialized = true;

            if (ikHandler != null)
                ikHandler.IsActive = true;
        }
    }

    private void SetPosture(PlayerPostureFlags posture)
    {
        currentPosture = posture;
        // Swimming overrides other postures
        bool isSwimming = posture.HasFlag(PlayerPostureFlags.Swim);
        animationHandler?.SetIsSwimming(isSwimming);
        if (isSwimming)
        {
            animationHandler?.SetIsCrouching(false);
            animationHandler?.SetIsSitting(false);
            animationHandler?.SetIsJumping(false);
        }
        else
        {
            animationHandler?.SetIsJumping(posture.HasFlag(PlayerPostureFlags.Jump));
            animationHandler?.SetIsCrouching(posture.HasFlag(PlayerPostureFlags.Crouch));
            animationHandler?.SetIsSitting(posture.HasFlag(PlayerPostureFlags.Sit));
        }
    }

    public void UpdateCar(ushort netId)
    {
        bool willBeOnCar = NetworkedTrainCar.TryGet(netId, out NetworkedTrainCar newTrainCar);

        if (OccupiedCar != null)
        {
            if (OccupiedCar == newTrainCar)
                return;

            OccupiedCar.Client_RemovePlayer(this);
            windController?.SetOnCar(null);
        }

        IsOnCar = willBeOnCar && newTrainCar != null;

        if (IsOnCar)
        {
            OccupiedCar = newTrainCar;
            selfTransform.SetParent(OccupiedCar.transform, true);
            OccupiedCar.Client_PlayerOnCar(this);
            windController?.SetOnCar(newTrainCar.TrainCar);
        }
        else
        {
            OccupiedCar = null;
            selfTransform.SetParent(null, true);
        }
    }

    /// <summary>
    /// Attach item to player object when in inventory
    /// </summary>
    /// <param name="itemGo">The item GameObject to attach</param>
    public void AddItemToInventory(GameObject itemGo)
    {
        itemGo.transform.SetParent(inventoryRoot.transform, true);
        itemGo.SetActive(false);
    }

    /// <summary>
    /// Sets the player's currently held item with optional position and rotation offsets
    /// </summary>
    /// <param name="itemGo">The item GameObject to hold</param>
    /// <param name="targetPos">Optional local position offset</param>
    /// <param name="targetRot">Optional local rotation offset</param>
    /// <param name="rightHand">Indicates if the item is held in the right hand. Always true for nonVR</param>

    // TODO: This currently only supports right hand holding and will need to be expanded to support left hand items and dual hand items
    public void HoldItem(GameObject itemGo, Vector3? targetPos = null, Quaternion? targetRot = null, bool rightHand = true)
    {
        Multiplayer.LogDebug(() => $"NetworkedPlayer.HoldItem({itemGo.GetPath()}) Player: {username}, Before position: {itemGo.transform.localPosition}, rotation:  {itemGo.transform.localRotation}, Target pos: {targetPos}, Target rot: {targetRot}");

        itemGo.transform.SetParent(selfTransform, true);
        var itemGrabHandler = itemGo.GetComponentInChildren<GrabHandlerItem>();
        if (itemGrabHandler != null)
        {
            itemGrabHandler.TogglePhysics(false);
            itemGrabHandler.interactionAllowed = false;
        }

        // Disable colliders to stop annoying noises and other potential issues
        disabledRHItemColliders.Clear();
        foreach (Collider col in itemGo.GetComponentsInChildren<Collider>(true))
        {
            Multiplayer.LogDebug(() => $"NetworkedPlayer.HoldItem() Collider: {col.name}, Enabled: {col.enabled}, Type: {col.GetType()}");
            if (col != null && col.enabled)
                col.enabled = false;

            disabledRHItemColliders.Add(col);
        }

        RightHandItemGO = itemGo;
        itemHoldPos = targetPos;
        itemHoldRot = targetRot;
    }

    /// <summary>
    /// Drops the player's currently held item
    /// </summary>

    // TODO: This currently only supports right hand holding and will need to be expanded to support left hand items and dual hand items
    public void DropItem()
    {
        // Re-enable previously disabled colliders
        foreach (Collider col in disabledRHItemColliders)
        {
            Multiplayer.LogDebug(() => $"NetworkedPlayer.DropItem() Re-enabling collider: {col.name}, Type: {col.GetType()}");
            if (col != null)
                col.enabled = true;
        }
        disabledRHItemColliders.Clear();

        var itemGrabHandler = RightHandItemGO.GetComponentInChildren<GrabHandlerItem>();
        if (itemGrabHandler != null)
        {
            itemGrabHandler.TogglePhysics(true);
            itemGrabHandler.interactionAllowed = true;
        }

        RightHandItemGO?.transform.SetParent(WorldMover.OriginShiftParent, true);

        RightHandItemGO = null;
        itemHoldPos = null;
        itemHoldRot = null;
    }

    private Vector3 GetItemOffsetFromPlayer()
    {
        Vector3 baseOffset = itemAnchorOffset;
        Vector3 finalOffset = itemHoldPos.HasValue ? baseOffset + itemHoldPos.Value : baseOffset;
        return selfTransform.TransformDirection(finalOffset);
    }
}
