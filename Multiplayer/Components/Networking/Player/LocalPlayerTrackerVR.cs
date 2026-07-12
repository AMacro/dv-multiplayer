using Multiplayer.Networking.Data.Player;
using UnityEngine;
using VRTK;

namespace Multiplayer.Components.Networking.Player;

[DisallowMultipleComponent]
internal class LocalPlayerTrackerVR : LocalPlayerTrackerBase
{
    private const float HAND_POSITION_THRESHOLD = 0.01f; // metres
    private const float HAND_ROTATION_THRESHOLD = 0.5f; // degrees

    GameObject controllerLeftHand;
    GameObject controllerRightHand;

    Vector3 lastLeftHandPosition = Vector3.zero;
    Quaternion lastLeftHandRotation = Quaternion.identity;
    Vector3 lastRightHandPosition = Vector3.zero;
    Quaternion lastRightHandRotation = Quaternion.identity;

    protected override void Initialize()
    {
        InitializeControllers();
    }

    private void InitializeControllers()
    {
        controllerLeftHand = VRTK_DeviceFinder.GetControllerLeftHand(false);
        controllerRightHand = VRTK_DeviceFinder.GetControllerRightHand(false);

        Quaternion inverseCameraYaw = GetInverseCameraYaw();

        if (controllerLeftHand != null)
        {
            lastLeftHandPosition = inverseCameraYaw * (controllerLeftHand.transform.position - PlayerManager.PlayerTransform.position);
            lastLeftHandRotation = inverseCameraYaw * controllerLeftHand.transform.rotation;
        }

        if (controllerRightHand != null)
        {
            lastRightHandPosition = inverseCameraYaw * (controllerRightHand.transform.position - PlayerManager.PlayerTransform.position);
            lastRightHandRotation = inverseCameraYaw * controllerRightHand.transform.rotation;
        }

        if (controllerLeftHand == null || controllerRightHand == null)
            Multiplayer.LogWarning($"LocalPlayerTrackerVR: VRTK controllers not found. leftIsNull: {controllerLeftHand == null}, rightIsNull: {controllerRightHand == null}");
    }

    protected override float GetHeadPitch()
    {
        return fps.m_Camera.transform.localEulerAngles.x;
    }

    // Inject VR controller data into the tracking data
    protected override void PopulateTrackingData(ref PlayerTrackingData data)
    {
        if (controllerLeftHand == null || controllerRightHand == null)
            InitializeControllers();

        // Encode hand positions relative to camera yaw so the receiver can correctly
        // reconstruct world positions using selfTransform.position + targetRotation * localOffset,
        // where targetRotation is also derived from the camera yaw.
        Quaternion inverseCameraYaw = GetInverseCameraYaw();

        if (controllerLeftHand != null)
        {
            Vector3 leftHandPosition = inverseCameraYaw * (controllerLeftHand.transform.position - PlayerManager.PlayerTransform.position);
            Quaternion leftHandRotation = inverseCameraYaw * controllerLeftHand.transform.rotation;

            if (Vector3.Distance(leftHandPosition, lastLeftHandPosition) > HAND_POSITION_THRESHOLD)
            {
                data.LeftHandPosition = leftHandPosition;
                lastLeftHandPosition = leftHandPosition;
            }
            if (Quaternion.Angle(leftHandRotation, lastLeftHandRotation) > HAND_ROTATION_THRESHOLD)
            {
                data.LeftHandRotation = leftHandRotation;
                lastLeftHandRotation = leftHandRotation;
            }
        }

        if (controllerRightHand != null)
        {
            Vector3 rightHandPosition = inverseCameraYaw * (controllerRightHand.transform.position - PlayerManager.PlayerTransform.position);
            Quaternion rightHandRotation = inverseCameraYaw * controllerRightHand.transform.rotation;

            if (Vector3.Distance(rightHandPosition, lastRightHandPosition) > HAND_POSITION_THRESHOLD)
            {
                data.RightHandPosition = rightHandPosition;
                lastRightHandPosition = rightHandPosition;
            }
            if (Quaternion.Angle(rightHandRotation, lastRightHandRotation) > HAND_ROTATION_THRESHOLD)
            {
                data.RightHandRotation = rightHandRotation;
                lastRightHandRotation = rightHandRotation;
            }
        }
    }

    private static Quaternion GetInverseCameraYaw()
    {
        return Quaternion.Inverse(Quaternion.Euler(0, PlayerManager.PlayerCamera.transform.eulerAngles.y, 0));
    }
}
