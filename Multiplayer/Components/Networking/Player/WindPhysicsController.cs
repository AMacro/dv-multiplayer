using DV;
using DV.Damage;
using UnityChan;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal class WindPhysicsController : MonoBehaviour
{
    private const float INERTIA_FORCE_SCALE = 0.015f;
    private const float WIND_SPEED_FORCE_SCALE = 0.0025f;
    private const float ACCELERATION_SMOOTHING = 0.15f;

    // For testing allow inspector override of the force scales, but default to the constants above
    private float windSpeedForceScale = WIND_SPEED_FORCE_SCALE;
    private float inertiaForceScale = INERTIA_FORCE_SCALE;

    private NetworkedPlayer player;

    private RandomWind[] windComponents;
    private SpringBone[] springBones;

    private bool onCar;
    private bool insideCar;
    private Rigidbody carRigidbody;
    private Transform carTransform;
    private WindowsBreakingController windowController;
    private CameraTrigger cameraTrigger;
    private Vector3 lastCarLocalVelocity;
    private Vector3 smoothedLocalAcceleration;

    protected void Awake()
    {
        player = GetComponentInParent<NetworkedPlayer>();
        windComponents = GetComponentsInChildren<RandomWind>(true);
        springBones = GetComponentsInChildren<SpringBone>(true);
    }

    /// <summary>
    /// On each physics update, calculate the smoothed local acceleration of the car.
    /// </summary>
    protected void FixedUpdate()
    {
        if (!onCar || carRigidbody == null || carTransform == null)
            return;

        CheckIsInsideCar();

        float dt = Time.fixedDeltaTime;
        if (dt <= 0f)
            return;

        Vector3 carLocalVelocity = carTransform.InverseTransformDirection(carRigidbody.velocity);
        Vector3 rawAcceleration = (carLocalVelocity - lastCarLocalVelocity) / dt;
        lastCarLocalVelocity = carLocalVelocity;

        // Low-pass filter to suppress residual per-step track-joint/bogie noise, leaving only
        // genuine, sustained acceleration/deceleration trends.
        smoothedLocalAcceleration = Vector3.Lerp(smoothedLocalAcceleration, rawAcceleration, ACCELERATION_SMOOTHING);
    }

    protected void Update()
    {
        if (!onCar || carRigidbody == null || carTransform == null)
            return;

        if (springBones == null || springBones.Length == 0)
            return;

        Vector3 inertiaForce = carTransform.TransformDirection(-smoothedLocalAcceleration) * inertiaForceScale;

        Vector3 windForce = Vector3.zero;

        bool isExposedToWind = !insideCar || windowController == null || windowController.windowsBroken;
        if (isExposedToWind)
        {
            // The player is on the outside of the car, on a car with no windows, or inside a car with broken windows
            // so windforce should be applied.
            float forwardSpeed = carTransform.InverseTransformDirection(carRigidbody.velocity).z;
            windForce = carTransform.TransformDirection(new Vector3(0f, 0f, -forwardSpeed)) * windSpeedForceScale;

            if (Mathf.Approximately(forwardSpeed, 0f))
            {
                // From RandomWind.cs
                var perlin = new Vector3(Mathf.PerlinNoise(Time.time, 0.0f) * 0.005f, 0, 0);
                windForce += carTransform.TransformDirection(perlin);
            }
        }

        Vector3 resultantForce = inertiaForce + windForce;

        SetSpringForces(resultantForce);
    }

    public void SetOnCar(TrainCar car)
    {
        onCar = car != null;
        lastCarLocalVelocity = Vector3.zero;

        if (onCar)
        {
            car.TryGetComponent(out windowController);

            if (car.TryGetComponent(out CabinRenderOrdering cabinRenderOrdering) && cabinRenderOrdering != null)
                cameraTrigger = cabinRenderOrdering.triggerNullable;
          
            carRigidbody = car.rb;
            carTransform = car.transform;
            CheckIsInsideCar();
        }
        else
        {
            SetSpringForces(Vector3.zero);
            windowController = null;
            cameraTrigger = null;
            carRigidbody = null;
            carTransform = null;
            insideCar = false;
        }

        // If player is not on a car enable the random wind
        SetAmbientWindActive(!onCar);
    }

    private void CheckIsInsideCar()
    {
        if (cameraTrigger == null || player == null)
        {
            // default to "inside" if we don't have a camera trigger or player reference
            insideCar = onCar;
        }
        else
        {
            insideCar = cameraTrigger.IsPointInside(player.transform.position);
        }
    }

    private void SetAmbientWindActive(bool enable)
    {
        if (windComponents == null)
            return;

        foreach (var wind in windComponents)
        {
            if (wind == null)
                continue;

            wind.enabled = true;
            wind.isWindActive = enable;
        }
    }

    private void SetSpringForces(Vector3 force)
    {
        if (springBones == null)
            return;

        for (int i = 0; i < springBones.Length; i++)
        {
            if (springBones[i] != null)
                springBones[i].springForce = force;
        }
    }
}
