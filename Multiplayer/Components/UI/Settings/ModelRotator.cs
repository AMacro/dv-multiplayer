using UnityEngine;
using UnityEngine.EventSystems;

namespace Multiplayer.Components.UI.Settings;

/// <summary>
/// Attach to a UI element to allow click-drag to rotate a target transform around its Y axis.
/// Works with both mouse and VR laser pointer (both produce standard Unity pointer events).
/// </summary>
public class ModelRotator : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
{
    private const float MOUSE_SENSITIVITY = 0.5f;  // degrees per metre of drag
    private const float VR_SENSITIVITY = 0.5f;   // degrees per metre of laser pointer movement
    private const float INERTIA_DECAY = 8f;            // how quickly spin slows after release

    /// <summary>The transform to rotate (the instantiated model root).</summary>
    public Transform target;

    private float sensitivity;
    private bool isDragging;
    private float rotationVelocity;
    private Vector3 lastWorldPosition;

    protected void Awake()
    {
        sensitivity = VRManager.IsVREnabled() ? VR_SENSITIVITY : MOUSE_SENSITIVITY;
    }

    // Mouse mode
    public void OnPointerDown(PointerEventData eventData)
    {
        isDragging = true;
        rotationVelocity = 0f; // kill any existing spin when a new drag starts
        lastWorldPosition = GetPointerWorldPosition(eventData);
    }

    // Mouse and VR mode
    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || target == null)
            return;

        Vector3 currentWorldPosition = GetPointerWorldPosition(eventData);

        // Project the world positions onto the local horizontal plane of the rotator UI
        Vector3 lastLocal = transform.InverseTransformPoint(lastWorldPosition);
        Vector3 currentLocal = transform.InverseTransformPoint(currentWorldPosition);

        // Calculate the horizontal difference in local space
        float localDeltaX = currentLocal.x - lastLocal.x;

        // Negative so dragging right rotates clockwise
        float delta = -localDeltaX * sensitivity;
        target.Rotate(Vector3.up, delta, Space.World);  

        if (Time.unscaledDeltaTime > 0)
        {
            rotationVelocity = delta / Time.unscaledDeltaTime;
        }

        lastWorldPosition = currentWorldPosition;
    }

    // VR Mode
    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        rotationVelocity = 0f; // kill any existing spin when a new drag starts
        lastWorldPosition = GetPointerWorldPosition(eventData);
    }

    // Mouse mode, also triggered in VR prior to OnBeginDrag
    public void OnPointerUp(PointerEventData eventData)
    {
        isDragging = false;
    }

    // VR handling
    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
    }

    protected void Update()
    {
        if (isDragging || target == null || Mathf.Approximately(rotationVelocity, 0f))
            return;

        // Apply inertia spin and decay it over time
        target.Rotate(Vector3.up, rotationVelocity * Time.unscaledDeltaTime, Space.World);
        rotationVelocity = Mathf.Lerp(rotationVelocity, 0f, INERTIA_DECAY * Time.unscaledDeltaTime);

        if (Mathf.Abs(rotationVelocity) < 0.01f)
            rotationVelocity = 0f;
    }

    private Vector3 GetPointerWorldPosition(PointerEventData eventData)
    {
        // VR Raycasters populating this field provide the exact 3D point hit by the laser
        if (eventData.pointerCurrentRaycast.isValid && eventData.pointerCurrentRaycast.worldPosition != Vector3.zero)
            return eventData.pointerCurrentRaycast.worldPosition;

        // Fallback for desktop mouse drag on Canvas items
        return new Vector3(eventData.position.x, eventData.position.y, transform.position.z);
    }
}
