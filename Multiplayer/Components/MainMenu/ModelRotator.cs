using UnityEngine;
using UnityEngine.EventSystems;

namespace Multiplayer.Components.MainMenu;

/// <summary>
/// Attach to a UI element to allow click-drag to rotate a target transform around its Y axis.
/// Works with both mouse and VR laser pointer (both produce standard Unity pointer events).
/// </summary>
public class ModelRotator : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private const float ROTATION_SENSITIVITY = 0.5f;  // degrees per pixel of drag
    private const float INERTIA_DECAY = 8f;            // how quickly spin slows after release

    /// <summary>The transform to rotate (the instantiated model root).</summary>
    public Transform target;

    private bool isDragging;
    private float rotationVelocity;

    public void OnPointerDown(PointerEventData eventData)
    {
        isDragging = true;
        rotationVelocity = 0f; // kill any existing spin when a new drag starts
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || target == null)
            return;

        // eventData.delta.x is in screen pixels; negative so dragging right rotates clockwise
        float delta = -eventData.delta.x * ROTATION_SENSITIVITY;
        target.Rotate(Vector3.up, delta, Space.World);
        rotationVelocity = delta / Time.unscaledDeltaTime; // store angular velocity for inertia
    }

    public void OnPointerUp(PointerEventData eventData)
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
}
