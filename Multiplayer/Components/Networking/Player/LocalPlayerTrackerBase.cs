using Multiplayer.Networking.Data.Player;
using Multiplayer.Patches.Player;
using Multiplayer.Utils;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

public abstract class LocalPlayerTrackerBase : MonoBehaviour
{
    internal const float ROTATION_THRESHOLD = 0.2f;

    internal CustomFirstPersonController fps;

    internal bool lastOnCar;
    internal ushort lastCarNetId;
    internal Vector3 lastPosition;
    internal float lastRotationY;
    internal float lastLookPosition;
    internal float lastSitHeight;
    internal bool sentFinalPosition;
    internal PlayerPostureFlags lastPosture;

    internal bool isJumping;
    internal bool isOnCar;
    internal float sitHeight;
    internal TrainCar car;

    protected virtual void Start()
    {
        CoroutineManager.Instance.StartCoroutine(WaitForCustomFPS());
    }

    IEnumerator WaitForCustomFPS()
    {
        yield return null;

        while (fps == null)
        {
            fps = transform.GetComponent<CustomFirstPersonController>();
            yield return null;
        }

        Multiplayer.Log("CustomFirstPersonController found. Continuing initialisation.");

        NetworkLifecycle.Instance.OnTick += OnTick;

        PlayerManager.CarChanged += OnCarChanged;
        CustomFirstPersonControllerPatch.OnJump += OnJump;

        Initialize();
    }

    protected abstract void Initialize();

    protected virtual void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= OnTick;

        PlayerManager.CarChanged -= OnCarChanged;
        CustomFirstPersonControllerPatch.OnJump -= OnJump;
    }

    protected void OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (isOnCar && car == null)
        {
            car = PlayerManager.Car;
            isOnCar = car != null;
        }

        Vector3 position = isOnCar ? PlayerManager.PlayerTransform.localPosition : PlayerManager.PlayerTransform.GetWorldAbsolutePosition();
        float rotationY = PlayerManager.PlayerCamera.transform.eulerAngles.y;

        float rawHeadPitch = GetHeadPitch();
        float lookPosition = rawHeadPitch > 180f ? rawHeadPitch - 360f : rawHeadPitch;
        bool lookPositionChanged = Math.Abs(lastLookPosition - lookPosition) > ROTATION_THRESHOLD;

        PlayerPostureFlags posture = PlayerPostureFlags.None;

        if (!fps.underwater)
        {
            if (fps.IsCrouching)
                posture |= PlayerPostureFlags.Crouch;
            if (isJumping)
                posture |= PlayerPostureFlags.Jump;

            BuildPosture(ref posture);
        }
        else
        {
            posture = PlayerPostureFlags.Swim;
        }

        ushort carNetID = isOnCar ? car.GetNetId() : (ushort)0;

        bool positionOrRotationChanged = lastPosture != posture ||
                                        lastOnCar != isOnCar ||
                                        (isOnCar && lastCarNetId != carNetID) ||
                                        Vector3.Distance(lastPosition, position) > 0 ||
                                        Math.Abs(lastRotationY - rotationY) > ROTATION_THRESHOLD;

        bool heightChanged = lastSitHeight != sitHeight;

        Vector3 moveDirV3 = PlayerManager.PlayerTransform.InverseTransformDirection(fps.m_MoveDir);
        Vector2 moveDirection = new(moveDirV3.x, moveDirV3.z);

        var trackingData = new PlayerTrackingData
        {
            Position      = positionOrRotationChanged ? position                                                            : null,
            MoveDirection = positionOrRotationChanged ? moveDirection : null,
            RotationY     = positionOrRotationChanged ? rotationY                                                            : null,
            LookPosition  = lookPositionChanged       ? lookPosition                                                        : null,
            SitHeight     = heightChanged             ? sitHeight                                                           : null,
        };

        PopulateTrackingData(ref trackingData);

        bool hasChanges = positionOrRotationChanged || lookPositionChanged || heightChanged || trackingData.HasAdditionalData;

        if (!hasChanges && sentFinalPosition)
            return;

        lastSitHeight = sitHeight;
        lastOnCar = isOnCar;
        lastCarNetId = carNetID;
        lastPosition = position;
        lastRotationY = rotationY;
        lastLookPosition = lookPosition;
        lastPosture = posture;
        sentFinalPosition = !positionOrRotationChanged;

        NetworkLifecycle.Instance.Client.SendPlayerPosition(trackingData, posture, isOnCar, carNetID, isJumping || sentFinalPosition);
        isJumping = false;
    }

    /// <summary>
    /// Returns the raw head pitch Euler angle.
    /// </summary>
    protected abstract float GetHeadPitch();

    /// <summary>
    /// Appends subclass-specific posture flags. Only called when not underwater.
    /// </summary>
    protected virtual void BuildPosture(ref PlayerPostureFlags posture) { }

    /// <summary>
    /// Populates subclass-specific fields in the tracking data struct.
    /// </summary>
    protected virtual void PopulateTrackingData(ref PlayerTrackingData data) { }

    void OnCarChanged(TrainCar trainCar)
    {
        isOnCar = trainCar != null;
        car = trainCar;
    }

    void OnJump()
    {
        isJumping = true;
    }
}
