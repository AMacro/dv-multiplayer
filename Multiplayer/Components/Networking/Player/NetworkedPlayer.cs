using System;
using DV.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Editor.Components.Player;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

/// <summary>
/// Represents a networked player in the multiplayer environment, handling movement, item holding, and visual state
/// </summary>
public class NetworkedPlayer : MonoBehaviour
{
    #region Static Setup
    private static Vector3 itemAnchorOffset;

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
        itemAnchorOffset = PlayerManager.PlayerTransform.InverseTransformPoint(ItemPositionController.Instance.itemAnchor.position);
        Multiplayer.LogDebug(() => $"NetworkedPlayer.CaptureItemAnchorOffset() itemAnchorOffset: {itemAnchorOffset}");
    }

    #endregion

    private const float LERP_SPEED = 5.0f;

    public byte Id;
    //public Guid Guid;

    private AnimationHandler animationHandler;
    private NameTag nameTag;
    private int ping;

    private string username;

    public string Username {
        get => username;
        set {
            username = value;
            nameTag.SetUsername(value);
        }
    }

    private bool isOnCar;

    private Transform selfTransform;
    private Vector3 targetPos;
    private Quaternion targetRotation;
    private Vector2 moveDir;
    private Vector2 targetMoveDir;
    
    private GameObject itemHeld;
    private Vector3? itemHoldPos;
    private Quaternion? itemHoldRot;

    private void Awake()
    {
        animationHandler = GetComponent<AnimationHandler>();

        nameTag = GetComponent<NameTag>();
        nameTag.LookTarget = PlayerManager.ActiveCamera.transform;
        PlayerManager.CameraChanged += () => nameTag.LookTarget = PlayerManager.ActiveCamera.transform;

        OnSettingsUpdated(Multiplayer.Settings);
        Settings.OnSettingsUpdated += OnSettingsUpdated;

        selfTransform = transform;
        targetPos = selfTransform.position;
        targetRotation = selfTransform.rotation;
        moveDir = Vector2.zero;
        targetMoveDir = Vector2.zero;
    }

    private void OnSettingsUpdated(Settings settings)
    {
        nameTag.ShowUsername(settings.ShowNameTags);
        nameTag.ShowPing(settings.ShowNameTags && settings.ShowPingInNameTags);
    }

    public void SetPing(int ping)
    {
        nameTag.SetPing(ping);
        this.ping = ping;
    }

    public int GetPing()
    {
        return ping;
    }

    private void Update()
    {
        float t = Time.deltaTime * LERP_SPEED;

        Vector3 position = Vector3.Lerp(isOnCar ? selfTransform.localPosition : selfTransform.position, isOnCar ? targetPos : targetPos + WorldMover.currentMove, t);
        Quaternion rotation = Quaternion.Lerp(isOnCar ? selfTransform.localRotation : selfTransform.rotation, targetRotation, t);

        moveDir = Vector2.Lerp(moveDir, targetMoveDir, t);
        animationHandler.SetMoveDir(moveDir);

        if (isOnCar)
        {
            selfTransform.localPosition = position;
            selfTransform.localRotation = rotation;
        }
        else
        {
            selfTransform.position = position;
            selfTransform.rotation = rotation;
        }

        if (itemHeld != null)
        {
            itemHeld.transform.position = selfTransform.position + GetItemOffsetFromPlayer();
            itemHeld.transform.rotation = selfTransform.rotation * (itemHoldRot ?? ItemPositionController.Instance.itemAnchor.localRotation);
        }
    }

    public void UpdatePosition(Vector3 position, Vector2 moveDir, float rotation, bool isJumping, bool movePacketIsOnCar)
    {
        targetMoveDir = moveDir;
        animationHandler.SetIsJumping(isJumping);

        if (isOnCar != movePacketIsOnCar)
            return;

        targetPos = position;
        targetRotation = Quaternion.Euler(0, rotation, 0);
    }

    public void UpdateCar(ushort netId)
    {
        isOnCar = NetworkedTrainCar.GetTrainCar(netId, out TrainCar trainCar);

        if(isOnCar && trainCar == null)
        {
            //we have a desync!
            Multiplayer.LogWarning($"Desync detected! Trying to update player '{username}' position to TrainCar netId {netId}, but car is null!");
            return;
        }

        selfTransform.SetParent(isOnCar ? trainCar.transform : null, true);
        targetPos = isOnCar ? transform.localPosition : selfTransform.position;
        targetRotation = isOnCar ? transform.localRotation : selfTransform.rotation;
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

    private Vector3 GetItemOffsetFromPlayer()
    {
        Vector3 baseOffset = itemAnchorOffset;
        Vector3 finalOffset = itemHoldPos.HasValue ? baseOffset + itemHoldPos.Value : baseOffset;
        return selfTransform.TransformDirection(finalOffset);
    }

}
