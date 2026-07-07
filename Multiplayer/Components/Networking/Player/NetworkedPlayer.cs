using DV.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Editor.Components.Player;
using Multiplayer.Networking.Data;
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
    /// for mapping to a NetworkedPlayer
    /// This must be called as soon as the world is loaded, before the local player moves or crouches
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

    public byte PlayerId { get; set; }
    public string CrewName { get; set; }

    private GameObject playerModel;
    private AnimationHandler animationHandler;
    private NameTag nameTag;
    private int ping;

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

    internal bool IsOnCar { get; private set; }
    internal TrainCar OccupiedCar { get; private set; }

    private Transform selfTransform;
    private Transform headTransform;
    private Vector3 headBaseLocalPosition;
    private Vector3 headBaseLocalEuler;
    private Vector3 targetPos;
    private Quaternion targetRotation;
    private float currentHeadPitch;
    private float targetHeadPitch;
    private Vector2 moveDir;
    private Vector2 targetMoveDir;
    private PlayerPostureFlags currentPosture;

    private GameObject itemHeld;
    private Vector3? itemHoldPos;
    private Quaternion? itemHoldRot;

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
            headBaseLocalPosition = Vector3.zero;
            headBaseLocalEuler = Vector3.zero;
        }

        playerModel = Instantiate(newModel, transform);

        animationHandler = playerModel.GetComponent<AnimationHandler>();

        var animator = playerModel.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform == null)
            {
                Multiplayer.LogWarning($"Head bone not found in model {newModel.name}. Tracking will not work");
            }
            else
            {
                headBaseLocalPosition = selfTransform.InverseTransformPoint(headTransform.position);
                headBaseLocalEuler = (Quaternion.Inverse(selfTransform.rotation) * headTransform.rotation).eulerAngles;
            }
        }
        else
        {
            Multiplayer.LogWarning($"Animator not found in model {newModel.name}. Tracking will not work");
        }

        SetPosture(currentPosture);
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
        float t = Time.deltaTime * LERP_SPEED;

        Vector3 position = Vector3.Lerp(IsOnCar ? selfTransform.localPosition : selfTransform.position, IsOnCar ? targetPos : targetPos + WorldMover.currentMove, t);

        // Calculate smoothed head pitch for use in VR and nonVR head positioning and nonVR item positioning
        currentHeadPitch = Mathf.Lerp(currentHeadPitch, targetHeadPitch, t);

        moveDir = Vector2.Lerp(moveDir, targetMoveDir, t);
        animationHandler?.SetMoveDir(moveDir);

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
    }

    protected void LateUpdate()
    {
        // Runs after Animator has applied updates

        if (headTransform == null)
            return;

        // Base orientation: T-pose head rotation expressed in world space from the player root
        Quaternion baseHeadWorldRot = selfTransform.rotation * Quaternion.Euler(headBaseLocalEuler);

        // AngleAxis around selfTransform.right unambiguously rotates the head up/down
        // regardless of how euler angles decompose for this particular bone
        headTransform.rotation = Quaternion.AngleAxis(currentHeadPitch, selfTransform.right) * baseHeadWorldRot;
    }

    public void UpdatePosition(Vector3 position, Vector2 moveDir, float rotationY, float lookPosition, PlayerPostureFlags posture, bool movePacketIsOnCar)
    {
        targetPos = position;
        targetMoveDir = moveDir;

        SetPosture(posture);

        if (IsOnCar != movePacketIsOnCar)
            return;

        targetRotation = Quaternion.Euler(0, rotationY, 0);
        targetHeadPitch = lookPosition;
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
        IsOnCar = NetworkedTrainCar.TryGet(netId, out TrainCar trainCar);
        OccupiedCar = trainCar;

        if (IsOnCar)
            selfTransform.SetParent(OccupiedCar.transform, true);
        else
            selfTransform.SetParent(null, true);
    }

    /// <summary>
    /// Sets the player's currently held item with optional position and rotation offsets
    /// </summary>
    /// <param name="itemGo">The item GameObject to hold</param>
    /// <param name="targetPos">Optional local position offset</param>
    /// <param name="targetRot">Optional local rotation offset</param>
    public void HoldItem(GameObject itemGo, Vector3? targetPos = null, Quaternion? targetRot = null)
    {
        Multiplayer.LogDebug(() => $"NetworkedPlayer.HoldItem({itemGo.GetPath()}) Player: {username}, Before position: {itemGo.transform.localPosition}, rotation:  {itemGo.transform.localRotation}, Target pos: {targetPos}, Target rot: {targetRot}");

        itemHeld = itemGo;
        itemHoldPos = targetPos;
        itemHoldRot = targetRot;
    }

    public void DropItem()
    {
        itemHeld = null;
        itemHoldPos = null;
        itemHoldRot = null;
    }
}
