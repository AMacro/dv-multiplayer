using DV;
using DV.CabControls;
using DV.Customization.Paint;
using DV.Damage;
using DV.HUD;
using DV.Logic.Job;
using DV.MultipleUnit;
using DV.Simulation.Brake;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.Simulation.Fuses;
using DV.ThingTypes;
using JetBrains.Annotations;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static DV.HUD.InteriorControlsManager;
using static Multiplayer.Networking.Data.Train.RigidbodySnapshot;

namespace Multiplayer.Components.Networking.Train;

public class NetworkedTrainCar : IdMonoBehaviour<ushort, NetworkedTrainCar>
{
    #region Lookup Cache

    private static readonly Dictionary<TrainCar, NetworkedTrainCar> trainCarsToNetworkedTrainCars = [];
    private static readonly Dictionary<string, NetworkedTrainCar> trainCarIdToNetworkedTrainCars = [];
    private static readonly Dictionary<string, TrainCar> trainCarIdToTrainCars = [];
    private static readonly Dictionary<HoseAndCock, Coupler> hoseToCoupler = [];

    public static bool TryGet(ushort netId, out NetworkedTrainCar obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedTrainCar> rawObj);
        obj = (NetworkedTrainCar)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out TrainCar trainCar)
    {
        bool b = TryGet(netId, out NetworkedTrainCar networkedTrainCar);
        trainCar = b ? networkedTrainCar.TrainCar : null;
        return b;
    }

    public static bool TryGet(ushort netId, out Car trainCar)
    {
        bool b = TryGet(netId, out NetworkedTrainCar networkedTrainCar);
        trainCar = b ? networkedTrainCar.TrainCar?.logicCar : null;
        return b;
    }

    public static bool TryGetCoupler(HoseAndCock hoseAndCock, out Coupler coupler)
    {
        return hoseToCoupler.TryGetValue(hoseAndCock, out coupler);
    }

    public static bool GetFromTrainId(string carId, out NetworkedTrainCar networkedTrainCar)
    {
        return trainCarIdToNetworkedTrainCars.TryGetValue(carId, out networkedTrainCar);
    }
    public static bool GetTrainCarFromTrainId(string carId, out TrainCar trainCar)
    {
        return trainCarIdToTrainCars.TryGetValue(carId, out trainCar);
    }

    public static bool TryGetFromTrainCar(TrainCar trainCar, out NetworkedTrainCar networkedTrainCar)
    {
        return trainCarsToNetworkedTrainCars.TryGetValue(trainCar, out networkedTrainCar);
    }

    public static bool TryGetNetId(TrainCar trainCar, out ushort netId)
    {
        netId = 0;

        if (!trainCarsToNetworkedTrainCars.TryGetValue(trainCar, out var networkedTrainCar) || networkedTrainCar == false || networkedTrainCar.NetId == 0)
            return false;

        netId = networkedTrainCar.NetId;
        return true;
    }

    public static bool TryGetNetId(Car car, out ushort netId)
    {
        netId = 0;

        if (car == null || !GetFromTrainId(car.ID, out var networkedTrainCar) || networkedTrainCar == false || networkedTrainCar.NetId == 0)
            return false;

        netId = networkedTrainCar.NetId;
        return true;
    }

    #endregion
    private const int MAX_COUPLER_ITERATIONS = 50;
    private const float MAX_PORT_DELTA = 0.001f;
    private const uint MIN_KINEMATIC_CYCLES = 10;
    private const float DISTANCE_TOLERANCE = 2f;
    private const float MAX_PAINT_DISTANCE_SQ = (CommsRadioPaintjob.SIGNAL_RANGE + DISTANCE_TOLERANCE) * (CommsRadioPaintjob.SIGNAL_RANGE + DISTANCE_TOLERANCE);
    private const float POSITION_UPDATE_THRESHOLD = 0.1f; // TrainCar must have a bigger delta to apply position update
    private const float INTERIOR_CONTROLS_TIMEOUT = 2f;
    private const float IN_USE_THRESHOLD = 10 * 60f; // 10 minutes for in use to expire

    #region Port and Fuse Map

    private static readonly Dictionary<uint, string> netIdToPort = [];
    private static readonly Dictionary<string, uint> portToNetId = [];
    private static readonly Dictionary<uint, string> netIdToFuse = [];
    private static readonly Dictionary<string, uint> fuseToNetId = [];

    static uint GetPortNetId(string portId)
    {
        if (portToNetId.TryGetValue(portId, out var netId))
            return netId;

        netId = StringHashing.Fnv1aHash(portId);

        Multiplayer.LogDebug(() => $"GetPortNetId({portId}) Registering with {netId}");

        netIdToPort[netId] = portId;
        portToNetId[portId] = netId;

        return netId;
    }

    static string GetPort(uint netId)
    {
        netIdToPort.TryGetValue(netId, out var portId);
        return portId;
    }

    static uint GetFuseNetId(string fuseId)
    {
        if (fuseToNetId.TryGetValue(fuseId, out var netId))
            return netId;

        netId = StringHashing.Fnv1aHash(fuseId);
        netIdToFuse[netId] = fuseId;
        fuseToNetId[fuseId] = netId;

        return netId;
    }
    static string GetFuse(uint netId)
    {
        netIdToFuse.TryGetValue(netId, out var portId);
        return portId;
    }
    #endregion

    public float CarLengthSq => CarSpawner.Instance.carLiveryToCarLength[TrainCar.carLivery] * CarSpawner.Instance.carLiveryToCarLength[TrainCar.carLivery];

    public string CurrentID { get; private set; }
    public TrainCar TrainCar;
    public uint TicksSinceSync = uint.MaxValue;

    public uint lastTickProcessed = 0;

    private Bogie bogie1;
    private Bogie bogie2;
    private BrakeSystem brakeSystem;

    private bool hasSimFlow;
    private SimulationFlow simulationFlow;
    SimController simController;
    public FireboxSimController firebox;
    public CoalPileSimController coalPile;
    private readonly Dictionary<TrainDamage, TrainDamage.HealthChanged> trainDamageDelegates = [];

    private HashSet<uint> dirtyPorts;
    private Dictionary<uint, float> lastSentPortValues;
    private HashSet<uint> dirtyFuses;
    private readonly HashSet<TrainCarPaint> dirtyPaints = [];
    private readonly Dictionary<string, float> lastSentTrainDamages = [];

    private InteriorControlsManager interiorControlsManager;
    private readonly Dictionary<ControlImplBase, Action<ValueChangedEventArgs>> scrollableControlDelegates = [];
    private Dictionary<InteriorControlsManager.ControlType, OverridableBaseControl> controlTypeToControl = [];
    private readonly Dictionary<uint, OverridableBaseControl> portToBaseControl = [];
    private readonly Dictionary<IScrollable, Coroutine> scrollingControls = [];

    private bool handbrakeDirty;
    private bool mainResPressureDirty;
    private bool brakeOverheatDirty;

    public bool BogieTracksDirty;
    private bool cargoStateDirty;
    private bool cargoHealthDirty;
    private bool cargoIsLoading;
    public byte CargoModelIndex = byte.MaxValue;
    private bool carHealthDirty;
    private bool sendCouplers;
    private bool sendCables;

    public bool IsDestroying;

    #region Server Variables
    //Coupler interaction
    private bool frontInteracting = false;
    private bool rearInteracting = false;

    private ServerPlayer frontInteractionPlayer;
    private ServerPlayer rearInteractionPlayer;

    // Player tracking
    private readonly HashSet<ServerPlayer> serverPlayersInCar = [];

    // Control authority tracking
    private readonly Dictionary<uint, ServerPlayer> portAuthority = [];

    #endregion

    #region Client Variables

    public bool Client_Initialized { get; private set; }
    public TickedQueue<float> Client_trainSpeedQueue;
    public TickedQueue<RigidbodySnapshot> Client_trainRigidbodyQueue;
    public TickedQueue<BogieData> client_bogie1Queue;
    public TickedQueue<BogieData> client_bogie2Queue;

    // Player tracking
    private readonly HashSet<NetworkedPlayer> networkedPlayersInCar = [];

    //Coupler interaction
    private Coupler couplerInteraction;
    private ChainCouplerInteraction.State originalState;
    private Coupler originalCoupledTo;

    private uint kinematicCycles = 0;
    private bool wasKinematicBeforeHardCorrection = false;

    private readonly Dictionary<uint, bool> portNetIdToBlockState = [];
    private readonly Dictionary<uint, ControlImplBase> portNetIdToControl = [];
    private readonly Dictionary<ControlImplBase, uint> controlToPortNetId = [];
    #endregion

    #region Common Variables

    #endregion

    protected override bool IsIdServerAuthoritative => true;

    protected override void Awake()
    {
        base.Awake();

        TrainCar = GetComponent<TrainCar>();
        trainCarsToNetworkedTrainCars[TrainCar] = this;

        TrainCar.LogicCarInitialized += OnLogicCarInitialised;

        bogie1 = TrainCar.Bogies[0];
        bogie2 = TrainCar.Bogies[1];

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkTrainsetWatcher.Instance.CheckInstance(); // Ensure the NetworkTrainsetWatcher is initialized
            Client_Initialized = true;
        }
        else
        {
            Client_trainSpeedQueue = TrainCar.GetOrAddComponent<TrainSpeedQueue>();
            Client_trainRigidbodyQueue = TrainCar.GetOrAddComponent<NetworkedRigidbody>();
            StartCoroutine(Client_InitLater());
        }
    }

    [UsedImplicitly]
    protected void Start()
    {
        float time = Time.time;

        if (TrainCar == null)
        {
            Multiplayer.LogError($"NetworkedTrainCar.Start() TrainCar is null on {gameObject?.name} after {(Time.time - time):F2}s");
            return;
        }

        Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start({TrainCar?.ID}, {NetId})");

        if (TrainCar.couplers == null || TrainCar.couplers.Length == 0)
        {
            Multiplayer.LogWarning($"NetworkedTrainCar.Start() Couplers are null or empty on {CurrentID}");
        }
        else
        {
            foreach (Coupler coupler in TrainCar.couplers)
            {
                hoseToCoupler[coupler.hoseAndCock] = coupler;

                //Multiplayer.LogDebug(() => $"TrainCar.Start() [{TrainCar?.ID}, {NetId}], Coupler exists: {coupler != null}, Is front: {coupler.isFrontCoupler}, ChainScript exists: {coupler.ChainScript != null}");

                //Locos with tenders and tenders only have one chainscript each, no trainscript is used for the hitch between the loco and tender
                if (coupler.ChainScript != null)
                    coupler.ChainScript.StateChanged += (state) => { Client_CouplerStateChange(state, coupler); };
            }

            Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start({CurrentID}, {NetId}) Couplers complete");
        }

        simController = GetComponent<SimController>();
        if (simController != null)
        {
            hasSimFlow = true;
            simulationFlow = simController.SimulationFlow;

            // Find all train controls
            if (simController.controlsOverrider == null)
            {
                Multiplayer.LogWarning($"NetworkedTrainCar.Start() ControlsOverrider is null on car {CurrentID}");
            }
            else
            {
                controlTypeToControl = simController.controlsOverrider.controlsMap;
                foreach (var kvp in controlTypeToControl)
                {
                    var control = kvp.Value;
                    if (control == null)
                    {
                        Multiplayer.LogWarning($"NetworkedTrainCar.Start() Control {kvp.Key} is null on car {CurrentID}");
                        continue;
                    }

                    if (string.IsNullOrEmpty(control.portId))
                    {
                        Multiplayer.LogWarning($"NetworkedTrainCar.Start() Control {kvp.Key} has no portId on car {CurrentID}");
                        continue;
                    }

                    var portNetId = GetPortNetId(control.portId);
                    portToBaseControl[portNetId] = control;

                    Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start() Control {kvp.Key} has portId {control.portId} on car {CurrentID}");
                }
            }

            TrainCar.InteriorLoaded += OnTrainCarInteriorLoaded;
            TrainCar.InteriorAboutToBeUnloaded += OnTrainCarInteriorUnloaded;

            if (TrainCar.loadedInterior != null)
                OnTrainCarInteriorLoaded(TrainCar.loadedInterior.gameObject);

            if (simulationFlow != null)
            {
                dirtyPorts = new HashSet<uint>(simulationFlow.fullPortIdToPort.Count);
                lastSentPortValues = new Dictionary<uint, float>(dirtyPorts.Count);
                foreach (KeyValuePair<string, Port> kvp in simulationFlow.fullPortIdToPort)
                {
                    var portNetId = GetPortNetId(kvp.Key);
                    if (kvp.Value.valueType == PortValueType.CONTROL || NetworkLifecycle.Instance.IsHost())
                    {
                        if (portToBaseControl.TryGetValue(portNetId, out var control))
                        {
                            Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start({CurrentID}, {NetId}) Subscribing to control {control.ControlType} for {kvp.Key} with netId {portNetId}");
                            control.ControlUpdated += _ => { Common_OnPortUpdated(kvp.Value, portNetId); };
                        }
                        else
                        {
                            Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start({CurrentID}, {NetId}) Subscribing to port {kvp.Key}");
                            kvp.Value.ValueUpdatedInternally += _ => { Common_OnPortUpdated(kvp.Value, portNetId); };
                        }
                    }
                }

                dirtyFuses = new HashSet<uint>(simulationFlow.fullFuseIdToFuse.Count);
                foreach (KeyValuePair<string, Fuse> kvp in simulationFlow.fullFuseIdToFuse)
                {
                    var fuseNetId = GetFuseNetId(kvp.Key);
                    kvp.Value.StateUpdated += _ => { Common_OnFuseUpdated(fuseNetId); };
                }
            }
            else
            {
                Multiplayer.LogWarning($"NetworkedTrainCar.Start() SimulationFlow is null on {CurrentID}");
            }

            firebox = simController.firebox;
            coalPile = simController.coalPile;

            // Ports pulsed on an event (adding coal, igniting firebox, etc)
            if (firebox != null)
            {
                firebox.fireboxCoalControlPort.ValueUpdatedInternally += Client_OnFireboxAddCoal;   //Player adding coal
                firebox.fireboxIgnitionPort.ValueUpdatedInternally += Client_OnIgnite;      //Player igniting firebox
            }

            if (coalPile != null)
            {
                coalPile.coalConsumePort.ValueUpdatedInternally += Client_OnCoalPileInteraction; //Coal being added/removed by shovel or feeder
            }
        }

        Multiplayer.LogDebug(() => $"NetworkedTrainCar.Start({TrainCar?.ID}, {NetId}) SimController complete");

        if (TrainCar.brakeSystem != null)
        {
            brakeSystem = TrainCar.brakeSystem;
            brakeSystem.HandbrakePositionChanged += Common_OnHandbrakePositionChanged;
            brakeSystem.BrakeCylinderReleased += Common_OnBrakeCylinderReleased;
        }
        else
        {
            Multiplayer.LogError($"NetworkedTrainCar.Start() BrakeSystem is null on {CurrentID}");
        }

        if (TrainCar.PaintExterior != null)
            TrainCar.PaintExterior.OnThemeChanged += Common_OnPaintThemeChange;
        if (TrainCar.PaintInterior != null)
            TrainCar.PaintInterior.OnThemeChanged += Common_OnPaintThemeChange;

        NetworkLifecycle.Instance.OnTick += Common_OnTick;

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick += Server_OnTick;
            NetworkLifecycle.Instance.Server.PlayerDisconnected += Server_OnPlayerDisconnect;

            bogie1.TrackChanged += Server_BogieTrackChanged;
            bogie2.TrackChanged += Server_BogieTrackChanged;

            TrainCar.frontCoupler.Uncoupled += Server_CouplerUncoupled;
            TrainCar.rearCoupler.Uncoupled += Server_CouplerUncoupled;

            TrainCar.CarDamage.CarEffectiveHealthStateUpdate += Server_CarHealthUpdate;

            //find all TrainDamages and subscribe
            if (TryGetComponent<DamageController>(out DamageController damageController) && damageController != null)
            {
                var trainDamageFields = typeof(DamageController)
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(field => field.FieldType == typeof(TrainDamage))
                    .Select(field => new { Field = field, Damage = (TrainDamage)field.GetValue(damageController) })
                    .Where(value => value.Damage != null)
                    .ToArray();

                if (trainDamageFields != null && trainDamageFields.Length > 0)
                {
                    for (int i = 0; i < trainDamageFields.Length; i++)
                    {
                        var fieldName = trainDamageFields[i].Field.Name;
                        var fieldValue = trainDamageFields[i].Damage;

                        //create a delegate for each field
                        void DamagesUpdate(float health) => Server_TrainDamagesHealthUpdate(fieldName, health);

                        //subscribe to the event
                        trainDamageFields[i].Damage.HealthPercentageChanged += DamagesUpdate;

                        //store delegates and set a last sent value to an impossible value
                        trainDamageDelegates.Add(fieldValue, DamagesUpdate);
                        lastSentTrainDamages.Add(fieldName, -1f);
                    }
                }
            }

            if (brakeSystem != null)
            {
                brakeSystem.MainResPressureChanged += Server_MainResUpdate;
                brakeSystem.heatController.OverheatingActiveStateChanged += Server_BrakeHeatUpdate;
            }

            StartCoroutine(Server_WaitForLogicCar());
        }
        else
        {
            NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerDisconnected += Client_PlayerDisconnected;
        }

        NetworkLifecycle.Instance?.Client.SendTrainSyncRequest(NetId);
    }

    private void OnTrainCarInteriorLoaded(GameObject interior)
    {
        Multiplayer.LogDebug(() => $"OnTrainCarInteriorLoaded() {CurrentID}, interior is null: {interior == null}");

        StartCoroutine(WaitForInterior());
    }

    private IEnumerator WaitForInterior()
    {
        float time = Time.time;
        interiorControlsManager = null;

        yield return new WaitUntil
        (
            () =>
            {
                return TrainCar?.loadedInterior != null || Time.time - time > INTERIOR_CONTROLS_TIMEOUT;
            }
        );

        yield return new WaitForFixedUpdate();

        if (TrainCar?.loadedInterior == null)
        {
            Multiplayer.LogError($"TrainCar {CurrentID} failed to load an interior");
            yield break;
        }

        time = Time.time;

        yield return new WaitUntil
        (
            () =>
            {
                return TrainCar.loadedInterior.TryGetComponent<InteriorControlsManager>(out interiorControlsManager) || Time.time - time > INTERIOR_CONTROLS_TIMEOUT;
            }
        );

        CoroutineManager.Instance.StartCoroutine(HookControls(interiorControlsManager));
    }

    private IEnumerator HookControls(InteriorControlsManager interiorControlsManager)
    {
        yield return new WaitUntil(() => interiorControlsManager.Initialized);

        Multiplayer.LogDebug(() => $"HookControls() Hooking controls for car {CurrentID}, found {interiorControlsManager?.controls?.Count} controls");

        // Find all control overrides
        foreach (var kvp in interiorControlsManager.controls)
        {
            var control = kvp.Value;
            var key = kvp.Key;

            float timeOut = Time.time;
            yield return new WaitUntil
            (
                () =>
                    (control.controlImplBase != null && !string.IsNullOrEmpty(control.overridableBaseControl?.portId)) ||
                    Time.time - timeOut > INTERIOR_CONTROLS_TIMEOUT
            );

            var controlPortId = control.overridableBaseControl?.portId;

            if (string.IsNullOrEmpty(controlPortId))
            {
                Multiplayer.LogWarning($"Unable to hook control {control.overridableBaseControl?.name ?? control.controlImplBase?.spec?.name} ({key}), has no controlPortId on car [{CurrentID}, {NetId}]");
                continue;
            }

            Multiplayer.LogDebug(() => $"HookControls() Control [{controlPortId}] found on car {CurrentID}");
            var netId = GetPortNetId(controlPortId);

            if (control.controlImplBase == null)
            {
                Multiplayer.LogWarning($"Unable to hook control [{controlPortId}, {netId}], no control implementation on car {CurrentID}");
                continue;
            }

            // Check for ObiRopeGrabHandler used by steam whistles
            ObiRopeGrabAreaHandler ObiGrabHandler = control.controlImplBase?.transform?.parent?.GetComponentInChildren<ObiRopeGrabAreaHandler>(false);
            if (ObiGrabHandler != null)
            {
                // Handle grab/ungrab events
                ObiGrabHandler.Grabbed += Client_ControlGrabbed;
                ObiGrabHandler.Ungrabbed += Client_ControlUngrabbed;

                portNetIdToControl[netId] = ObiGrabHandler;
                controlToPortNetId[ObiGrabHandler] = netId;
            }
            else
            {
                // Standard control grab/ungrab events
                control.controlImplBase.Grabbed += Client_ControlGrabbed;
                control.controlImplBase.Ungrabbed += Client_ControlUngrabbed;

                portNetIdToControl[netId] = control.controlImplBase;
                controlToPortNetId[control.controlImplBase] = netId;
            }

            // Handle scroll events for scrollable controls
            if (control.scrollable != null)
            {
                void ScrollChanged(ValueChangedEventArgs args) => Client_ControlScrolled(control);
                scrollableControlDelegates[control.controlImplBase] = ScrollChanged;
                control.controlImplBase.ValueChanged += ScrollChanged;
            }

            if (portNetIdToBlockState.TryGetValue(netId, out var isBlocked) && isBlocked)
            {
                Multiplayer.LogDebug(() => $"WaitForInterior() Control [{controlPortId}, {netId}] is blocked on car {CurrentID}");
                Client_ReceiveAuthorityUpdate(netId, ControlAuthorityState.Blocked);
            }
        }
    }

    private void OnTrainCarInteriorUnloaded(GameObject interior)
    {
        Multiplayer.LogDebug(() => $"OnTrainCarInteriorUnloaded() {CurrentID}");

        foreach (var control in controlToPortNetId.Keys)
        {
            if (control == null)
                continue;

            if (control.IsGrabbed())
                Client_ControlUngrabbed(control);

            control.Grabbed -= Client_ControlGrabbed;
            control.Ungrabbed -= Client_ControlUngrabbed;
        }

        foreach (var kvp in scrollingControls)
        {
            if (kvp.Key == null)
                continue;

            StopCoroutine(kvp.Value);
        }
        scrollingControls.Clear();

        foreach (var kvp in scrollableControlDelegates)
        {
            if (kvp.Key != null && kvp.Value != null)
                kvp.Key.ValueChanged -= kvp.Value;
        }

        portNetIdToControl.Clear();
        controlToPortNetId.Clear();

        interiorControlsManager = null;
    }

    public void OnDisable()
    {
        bool unloading = UnloadWatcher.isUnloading;

        if (UnloadWatcher.isQuitting)
            return;

        //Clean dictionaries
        trainCarsToNetworkedTrainCars.Remove(TrainCar);
        trainCarIdToNetworkedTrainCars.Remove(CurrentID);
        trainCarIdToTrainCars.Remove(CurrentID);

        if (TrainCar?.couplers != null)
            foreach (Coupler coupler in TrainCar.couplers)
                hoseToCoupler.Remove(coupler.hoseAndCock);

        //stop tracking client events
        NetworkLifecycle.Instance.OnTick -= Common_OnTick;

        if (firebox != null)
        {
            firebox.fireboxCoalControlPort.ValueUpdatedInternally -= Client_OnFireboxAddCoal;
            firebox.fireboxIgnitionPort.ValueUpdatedInternally -= Client_OnIgnite;
        }

        if (coalPile != null)
        {
            coalPile.coalConsumePort.ValueUpdatedInternally -= Client_OnCoalPileInteraction;
        }

        if (brakeSystem != null)
        {
            brakeSystem.HandbrakePositionChanged -= Common_OnHandbrakePositionChanged;
            brakeSystem.BrakeCylinderReleased -= Common_OnBrakeCylinderReleased;
        }

        if (TrainCar.PaintExterior != null)
            TrainCar.PaintExterior.OnThemeChanged -= Common_OnPaintThemeChange;
        if (TrainCar.PaintInterior != null)
            TrainCar.PaintInterior.OnThemeChanged -= Common_OnPaintThemeChange;

        //stop tracking server events
        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick -= Server_OnTick;

            if (NetworkLifecycle.Instance.Server != null)
                NetworkLifecycle.Instance.Server.PlayerDisconnected -= Server_OnPlayerDisconnect;

            if (bogie1 != null)
                bogie1.TrackChanged -= Server_BogieTrackChanged;
            if (bogie2 != null)
                bogie2.TrackChanged -= Server_BogieTrackChanged;

            if (TrainCar?.frontCoupler != null)
                TrainCar.frontCoupler.Uncoupled -= Server_CouplerUncoupled;
            if (TrainCar?.rearCoupler != null)
                TrainCar.rearCoupler.Uncoupled -= Server_CouplerUncoupled;

            if (TrainCar?.CargoDamage != null)
            {
                TrainCar.CargoDamage.CargoEffectiveHealthStateUpdate -= Server_CargoHealthUpdate;
                TrainCar.CarDamage.CarEffectiveHealthStateUpdate -= Server_CarHealthUpdate;
            }

            //Unsubscribe from damage updates
            if (trainDamageDelegates != null && lastSentTrainDamages.Count > 0)
                foreach (var kvp in trainDamageDelegates)
                    kvp.Key.HealthPercentageChanged -= kvp.Value;

            if (brakeSystem != null)
            {
                brakeSystem.MainResPressureChanged -= Server_MainResUpdate;

                if (brakeSystem.heatController != null)
                    brakeSystem.heatController.OverheatingActiveStateChanged -= Server_BrakeHeatUpdate;
            }

            if (TrainCar?.logicCar != null)
            {
                TrainCar.logicCar.CargoLoaded -= Server_OnCargoLoaded;
                TrainCar.logicCar.CargoUnloaded -= Server_OnCargoUnloaded;
            }
        }
        else
        {
            if (NetworkLifecycle.Instance.Client != null && NetworkLifecycle.Instance.Client.ClientPlayerManager != null)
                NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerDisconnected -= Client_PlayerDisconnected;
        }

        CurrentID = string.Empty;

        try
        {
            Destroy(this);
        }
        catch { }

        if (UnloadWatcher.isUnloading)
        {
            trainCarsToNetworkedTrainCars.Clear();
            trainCarIdToNetworkedTrainCars.Clear();
            trainCarIdToTrainCars.Clear();
            hoseToCoupler.Clear();
        }
    }

    #region Server

    private void OnLogicCarInitialised()
    {
        //Multiplayer.LogWarning("OnLogicCarInitialised");
        if (TrainCar.logicCar != null)
        {
            CurrentID = TrainCar.ID;
            trainCarIdToNetworkedTrainCars[CurrentID] = this;
            trainCarIdToTrainCars[CurrentID] = TrainCar;

            TrainCar.LogicCarInitialized -= OnLogicCarInitialised;
        }
        else
        {
            Multiplayer.LogWarning("OnLogicCarInitialised Car Not Initialised!");
        }

    }
    private IEnumerator Server_WaitForLogicCar()
    {
        while (TrainCar.logicCar == null)
            yield return null;

        TrainCar.logicCar.CargoLoaded += Server_OnCargoLoaded;
        TrainCar.logicCar.CargoUnloaded += Server_OnCargoUnloaded;

        if (TrainCar.CargoDamage)
            TrainCar.CargoDamage.CargoEffectiveHealthStateUpdate += Server_CargoHealthUpdate;

        Server_DirtyAllState();
    }

    public void Server_DirtyAllState()
    {
        handbrakeDirty = true;
        mainResPressureDirty = true;
        cargoStateDirty = true;
        cargoHealthDirty = true;
        cargoIsLoading = true;
        carHealthDirty = true;
        BogieTracksDirty = true;
        sendCouplers = true;
        sendCables = true;

        if (!hasSimFlow)
            return;
        foreach (string portId in simulationFlow.fullPortIdToPort.Keys)
        {
            var netId = GetPortNetId(portId);
            dirtyPorts.Add(netId);
        }

        foreach (string fuseId in simulationFlow.fullFuseIdToFuse.Keys)
        {
            var netId = GetFuseNetId(fuseId);
            dirtyFuses.Add(netId);
        }
    }

    public bool Server_ValidateClientSimFlowPacket(ServerPlayer player, CommonTrainPortsPacket packet)
    {
        // Only allow control ports to be updated by clients
        if (hasSimFlow)
            foreach (uint portNetId in packet.PortIds)
            {

                var portId = GetPort(portNetId);
                if (simulationFlow.TryGetPort(portId, out Port port))
                {
                    if (port.valueType != PortValueType.CONTROL)
                    {
                        NetworkLifecycle.Instance.Server.LogWarning($"Player {player.Username} tried to send a non-control port! ([{portId}, {portNetId}] on [{CurrentID}, {NetId}])");
                        Common_DirtyPorts(packet.PortIds);
                        return false;
                    }
                }
                else
                {
                    NetworkLifecycle.Instance.Server.LogWarning($"Player {player.Username} sent portId: {portNetId}, value type: {port.valueType}, but the port was not found");
                }
            }

        // Only allow the player to update ports on the car they are in/near
        if (player.CarId == packet.NetId)
            return true;

        // Some ports can be updated by the player even if they are not in the car, like doors and windows.
        // Only deny the request if the player is more than 5 meters away from any point of the car.
        if ((player.WorldPosition - transform.position).sqrMagnitude <= CarLengthSq)
            return true;

        NetworkLifecycle.Instance.Server.LogWarning($"Player {player.Username} tried to send a sim flow packet for a car they are not in!");
        Common_DirtyPorts(packet.PortIds);
        return false;
    }

    private void Server_BogieTrackChanged(RailTrack arg1, Bogie arg2)
    {
        BogieTracksDirty = true;
    }

    private void Server_OnCargoLoaded(CargoType obj)
    {
        cargoStateDirty = true;
        cargoIsLoading = true;
    }

    private void Server_OnCargoUnloaded()
    {
        cargoStateDirty = true;
        cargoIsLoading = false;
        CargoModelIndex = byte.MaxValue;
    }

    private void Server_CargoHealthUpdate(float health)
    {
        cargoHealthDirty = true;
    }

    private void Server_CarHealthUpdate(float health)
    {
        //Multiplayer.LogDebug(() => $"Server_CarHealthUpdate({health}) netId: {NetId}");
        carHealthDirty = true;
    }

    private void Server_TrainDamagesHealthUpdate(string field, float health)
    {
        //Multiplayer.LogDebug(() => $"Server_TrainDamagesHealthUpdate({field}, {health}) netId: {NetId}");

        // Check if value has changed before updating
        if (!lastSentTrainDamages.TryGetValue(field, out float lastValue)
            || Mathf.Abs(lastValue - health) > MAX_PORT_DELTA
            || (health == 0 && lastValue != 0)
            || (health == 1 && lastValue != 1))
        {
            lastSentTrainDamages[field] = health;
            carHealthDirty = true;
        }
    }

    private void Server_MainResUpdate(float normalizedPressure, float pressure)
    {
        mainResPressureDirty = true;
    }

    private void Server_BrakeHeatUpdate(bool overheatActive)
    {
        brakeOverheatDirty = true;
    }

    private void Server_CouplerUncoupled(object _, UncoupleEventArgs args)
    {
        sendCouplers |= args.dueToBrokenCouple;
    }

    private void Server_OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        Server_SendBrakeStates();
        Server_SendCouplers();
        Server_SendCables();
        Server_SendCargoState();
        Server_SendCargoHealthUpdate();
        Server_SendCarHealthState();

        TicksSinceSync++; //keep track of last full sync
    }

    private void Server_SendBrakeStates()
    {
        if (!mainResPressureDirty && !brakeOverheatDirty)
            return;

        mainResPressureDirty = false;
        var hc = brakeSystem.heatController;
        NetworkLifecycle.Instance.Server.SendBrakeState
        (
            NetId,
            brakeSystem.mainReservoirPressure, brakeSystem.brakePipePressure, brakeSystem.brakeCylinderPressure,
            hc.overheatPercentage, hc.overheatReductionFactor, hc.temperature
        );
    }

    private void Server_SendCouplers()
    {
        if (!sendCouplers)
            return;

        sendCouplers = false;

        if (!TrainCar.frontCoupler.IsCoupled())
            //    NetworkLifecycle.Instance.Client.SendTrainCouple(TrainCar.frontCoupler,TrainCar.frontCoupler.coupledTo,false, false);
            //else
            NetworkLifecycle.Instance.Server.SendTrainUncouple(TrainCar.frontCoupler, true, true, false);

        if (!TrainCar.rearCoupler.IsCoupled())
            //    NetworkLifecycle.Instance.Client.SendTrainCouple(TrainCar.rearCoupler,TrainCar.rearCoupler.coupledTo,false, false);
            //else
            NetworkLifecycle.Instance.Server.SendTrainUncouple(TrainCar.rearCoupler, true, true, false);

        if (!TrainCar.frontCoupler.hoseAndCock.IsHoseConnected)
            //    NetworkLifecycle.Instance.Client.SendHoseConnected(TrainCar.frontCoupler, TrainCar.frontCoupler.coupledTo, false);
            //else
            NetworkLifecycle.Instance.Server.SendHoseDisconnected(TrainCar.frontCoupler, true);

        if (!TrainCar.rearCoupler.hoseAndCock.IsHoseConnected)
            //    NetworkLifecycle.Instance.Client.SendHoseConnected(TrainCar.rearCoupler, TrainCar.rearCoupler.coupledTo, false);
            //else
            NetworkLifecycle.Instance.Server.SendHoseDisconnected(TrainCar.rearCoupler, true);

        NetworkLifecycle.Instance.Server.SendCockState(NetId, TrainCar.frontCoupler, TrainCar.frontCoupler.IsCockOpen);
        NetworkLifecycle.Instance.Server.SendCockState(NetId, TrainCar.rearCoupler, TrainCar.rearCoupler.IsCockOpen);
    }

    private void Server_SendCables()
    {
        if (!sendCables)
            return;
        sendCables = false;

        if (TrainCar.muModule == null)
            return;

        if (TrainCar.muModule.frontCable.IsConnected)
            NetworkLifecycle.Instance.Client.SendMuConnected(TrainCar.muModule.frontCable, TrainCar.muModule.frontCable.connectedTo, false);

        if (TrainCar.muModule.rearCable.IsConnected)
            NetworkLifecycle.Instance.Client.SendMuConnected(TrainCar.muModule.rearCable, TrainCar.muModule.rearCable.connectedTo, false);
    }

    private void Server_SendCargoState()
    {
        if (!cargoStateDirty)
            return;
        cargoStateDirty = false;
        if (cargoIsLoading && TrainCar.logicCar.CurrentCargoTypeInCar == CargoType.None)
            return;

        NetworkLifecycle.Instance.Server.SendCargoState(this, cargoIsLoading, CargoModelIndex);
    }

    private void Server_SendCargoHealthUpdate()
    {
        if (!cargoHealthDirty)
            return;

        cargoHealthDirty = false;

        if (TrainCar.logicCar.CurrentCargoTypeInCar == CargoType.None)
            return;

        NetworkLifecycle.Instance.Server.SendCargoHealthUpdate(NetId, TrainCar.CargoDamage.currentHealth);
    }

    private void Server_SendCarHealthState()
    {
        if (!carHealthDirty)
            return;
        carHealthDirty = false;
        NetworkLifecycle.Instance.Server.SendCarHealthUpdate(NetId, TrainCarHealthData.From(TrainCar));
    }

    public bool Server_ValidateCouplerInteraction(CommonCouplerInteractionPacket packet, ServerPlayer player)
    {
        Multiplayer.LogDebug(() =>
                $"Server_ValidateCouplerInteraction([[{(CouplerInteractionType)packet.Flags}], {CurrentID}, {packet.NetId}], {player.PlayerId}) " +
                $"isFront: {packet.IsFrontCoupler}, frontInteracting: {frontInteracting}, frontInteractionPeer: {frontInteractionPlayer}, " +
                $"rearInteracting: {rearInteracting}, rearInteractionPeer: {rearInteractionPlayer}"
                );

        // Ensure no one else is interacting
        if (packet.IsFrontCoupler && frontInteracting && player != frontInteractionPlayer ||
           packet.IsFrontCoupler == false && rearInteracting && player != rearInteractionPlayer)
        {
            Multiplayer.LogDebug(() => $"Server_ValidateCouplerInteraction([{packet.Flags}, {CurrentID}, {packet.NetId}], {player.PlayerId}) Failed to validate!");
            return false;
        }

        Multiplayer.LogDebug(() => $"Server_ValidateCouplerInteraction([{packet.Flags}, {CurrentID}, {packet.NetId}], {player.PlayerId}) No one interacting");

        if (((CouplerInteractionType)packet.Flags).HasFlag(CouplerInteractionType.Start))
        {
            if (packet.IsFrontCoupler)
            {
                frontInteracting = true;
                frontInteractionPlayer = player;
            }
            else
            {
                rearInteracting = true;
                rearInteractionPlayer = player;
            }
        }
        else
        {
            if (packet.IsFrontCoupler)
                frontInteracting = false;
            else
                rearInteracting = false;
        }

        //todo: Additional checks for player location/proximity

        Multiplayer.LogDebug(() => $"Server_ValidateCouplerInteraction([{packet.Flags}, {CurrentID}, {packet.NetId}], {player.PlayerId}) Validation passed!");
        return true;
    }

    public void Server_ReceiveAuthorityRequest(uint portNetId, ServerPlayer player, bool requestAuthority)
    {
        portAuthority.TryGetValue(portNetId, out var currentAuth);

        if (requestAuthority)
        {
            if (currentAuth == null)
            {
                if ((player.WorldPosition - transform.position).sqrMagnitude > CarLengthSq)
                {
                    NetworkLifecycle.Instance.Server.LogWarning($"Player \"{player.Username}\" attempted to gain authority for a control on car {CurrentID}, but they are too far away!");
                    NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Denied, player);
                    NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Released, player);
                    return;
                }

                // No authority exists (or cleanup failed) - grant authority and communicate to all players
                NetworkLifecycle.Instance.Server.LogDebug(() => $"Player \"{player.Username}\" granted authority for a control on car {CurrentID}");
                portAuthority[portNetId] = player;
                NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Blocked, excludePlayer: player);
            }
            else if (currentAuth != player)
            {
                NetworkLifecycle.Instance.Server.LogWarning($"Player \"{player.Username}\" attempted to gain authority for a control that's in use on car {CurrentID}");
                NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Denied, player);
            }
        }
        else
        {
            // Release request
            if (currentAuth == player)
            {
                NetworkLifecycle.Instance.Server.LogDebug(() => $"Player \"{player.Username}\" released authority for a control on car {CurrentID}");
                portAuthority.Remove(portNetId);
                NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Released);
            }
            else if (currentAuth != null)
            {
                NetworkLifecycle.Instance.Server.LogWarning($"Player \"{player.Username}\" attempted to release authority for a control that's not theirs on car {CurrentID}");
                NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, portNetId, ControlAuthorityState.Denied, player);
            }
        }
    }

    public void Server_ValidatePaintThemeChange(TrainCarPaint.Target target, PaintTheme theme, ServerPlayer player)
    {
        var playerPos = TrainCar.transform.InverseTransformPoint(player.WorldPosition);
        Vector3 localClosestPoint = TrainCar.Bounds.ClosestPoint(playerPos);
        Vector3 worldClosestPoint = TrainCar.transform.TransformPoint(localClosestPoint);

        if ((player.WorldPosition - worldClosestPoint).sqrMagnitude > MAX_PAINT_DISTANCE_SQ)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"Player \"{player.Username}\" attempted to change the paint theme for car {CurrentID}, but they are too far away!");

            var paintControllers = GetComponents<TrainCarPaint>();
            int i = 0;
            while (i < paintControllers.Length)
            {
                if (paintControllers[i].TargetArea == target)
                    break;

                i++;
            }

            if (i >= paintControllers.Length)
                return;

            if (!PaintThemeLookup.Instance.TryGetNetId(paintControllers[i].CurrentTheme, out var themeNetId))
            {
                Multiplayer.LogWarning($"ValidatePaintThemeChange could not find themeNetId for theme {paintControllers[i].CurrentTheme.AssetName} on [{CurrentID}, {NetId}]");
                return;
            }

            NetworkLifecycle.Instance.Server.SendPaintThemeChange(this, target, themeNetId, player);

            return;
        }

        Common_ReceivePaintThemeUpdate(target, theme);
    }

    private void Server_OnPlayerDisconnect(ServerPlayer player)
    {
        //todo: resolve player disconnection during chain interaction
        if (frontInteractionPlayer == player || rearInteractionPlayer == player)
        {
            //Multiplayer.LogWarning($"Server_OnPlayerDisconnect() Coupler interaction in unknown state [{CurrentID}, {NetId}] isFront: {frontInteractionPlayer == player}");
            if (frontInteractionPlayer == player)
            {
                frontInteracting = false;
                //NetworkLifecycle.Instance.Client.SendCouplerInteraction(cou, coupler, otherCoupler);
            }
            else
            {
                rearInteracting = false;
            }
        }

        // Clean up blocked controls
        foreach (var kvp in portAuthority.Where(kvp => kvp.Value == player))
        {
            portAuthority.Remove(kvp.Key);
            NetworkLifecycle.Instance.Server.SendTrainControlAuthorityUpdate(NetId, kvp.Key, ControlAuthorityState.Released);
        }

        Server_RemovePlayer(player);
    }

    public void Server_PlayerOnCar(ServerPlayer player)
    {
        //Multiplayer.LogDebug(() => $"Server_PlayerOnCar() {player?.Username} is on car {CurrentID}, netId: {NetId}, player.CarId: {player?.CarId}");
        if (player == null || player.CarId == NetId)
            return;

        if (player.CarId != 0 && TryGet(player.CarId, out NetworkedTrainCar otherCar) && otherCar != null)
            otherCar.Server_RemovePlayer(player);

        serverPlayersInCar.Add(player);
        TrainCar?.visitChecker?.OnPlayerCarChanged(TrainCar);
    }

    public void Server_RemovePlayer(ServerPlayer player)
    {
        //Multiplayer.LogDebug(() => $"Server_RemovePlayer() {player?.Username} is leaving car {CurrentID}, netId: {NetId}, player.CarId: {player?.CarId}");
        serverPlayersInCar.Remove(player);
        TrainCar?.visitChecker?.OnPlayerCarChanged(TrainCar);
    }
    #endregion

    #region Common

    private void Common_OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        Common_SendHandbrakePosition();
        Common_SendFuses();
        Common_SendPorts();
        Common_SendPaintThemes();
    }

    private void Common_SendHandbrakePosition()
    {
        if (!handbrakeDirty)
            return;
        if (!TrainCar.brakeSystem.hasHandbrake)
            return;

        handbrakeDirty = false;
        NetworkLifecycle.Instance.Client.SendHandbrakePositionChanged(NetId, brakeSystem.handbrakePosition);
    }

    public void Common_DirtyPorts(uint[] portNetIds)
    {
        if (!hasSimFlow)
            return;

        foreach (uint portNetId in portNetIds)
        {
            var portId = GetPort(portNetId);
            if (!simulationFlow.TryGetPort(portId, out Port _))
            {
                Multiplayer.LogWarning($"Tried to dirty port [{portId}, {portNetId}] on {CurrentID} but port doesn't exist!");
                continue;
            }

            dirtyPorts.Add(portNetId);
        }
    }

    public void Common_DirtyFuses(uint[] fuseNetIds)
    {
        if (!hasSimFlow)
            return;

        foreach (uint fuseNetId in fuseNetIds)
        {
            var fuseId = GetFuse(fuseNetId);
            if (!simulationFlow.TryGetFuse(fuseId, out Fuse _))
            {
                Multiplayer.LogWarning($"Tried to dirty port [{fuseId}, {fuseNetId}] on {CurrentID} but it doesn't exist!");
                continue;
            }

            dirtyFuses.Add(fuseNetId);
        }
    }

    private void Common_SendPorts()
    {
        if (!hasSimFlow || dirtyPorts.Count == 0)
            return;

        int i = 0;
        uint[] portIds = dirtyPorts.ToArray();
        float[] portValues = new float[portIds.Length];
        foreach (uint portNetId in dirtyPorts)
        {
            var portId = GetPort(portNetId);
            if (simulationFlow.TryGetPort(portId, out Port port))
            {
                float value = port.Value;
                portValues[i] = value;
                lastSentPortValues[portNetId] = value;
            }
            else
            {
                Multiplayer.LogWarning($"Failed to send port \"{portId}\" for [{CurrentID}, {NetId}]");
            }

            i++;
        }

        dirtyPorts.Clear();

        NetworkLifecycle.Instance.Client.SendPorts(NetId, portIds, portValues);
    }

    private void Common_SendFuses()
    {
        if (!hasSimFlow || dirtyFuses.Count == 0)
            return;

        int i = 0;
        uint[] fuseIds = dirtyFuses.ToArray();
        bool[] fuseValues = new bool[fuseIds.Length];

        foreach (uint fuseNetId in dirtyFuses)
        {
            var fuseId = GetFuse(fuseNetId);
            if (simulationFlow.TryGetFuse(fuseId, out Fuse fuse))
                fuseValues[i] = fuse.State;
            else
                Multiplayer.LogWarning($"Failed to send fuse \"{fuseId}\" for [{CurrentID}, {NetId}]");

            i++;
        }

        dirtyFuses.Clear();

        NetworkLifecycle.Instance.Client.SendFuses(NetId, fuseIds, fuseValues);
    }

    private void Common_SendPaintThemes()
    {
        if (dirtyPaints.Count == 0)
            return;

        //Multiplayer.LogDebug(() => $"Common_SendPaintThemes() target: {paintController.TargetArea}, theme: {paintController.CurrentTheme.name}");
        foreach (var paintController in dirtyPaints)
        {
            if (!PaintThemeLookup.Instance.TryGetNetId(paintController.CurrentTheme, out var themeNetId))
            {
                Multiplayer.LogWarning($"Common_SendPaintThemes() could not find themeId for theme {paintController.CurrentTheme.name} on [{CurrentID}, {NetId}]");
                return;
            }

            if (NetworkLifecycle.Instance.IsHost())
                NetworkLifecycle.Instance.Server.SendPaintThemeChange(this, paintController.targetArea, themeNetId);
            else
                NetworkLifecycle.Instance.Client.SendPaintThemeChange(this, paintController.targetArea, themeNetId);
        }

        dirtyPaints.Clear();
    }

    private void Common_OnHandbrakePositionChanged((float, bool) data)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        handbrakeDirty = true;
    }

    private void Common_OnBrakeCylinderReleased()
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        NetworkLifecycle.Instance.Client.SendBrakeCylinderReleased(NetId);
    }

    private void Common_OnPortUpdated(Port port, uint portNetId)
    {
        //Multiplayer.LogDebug(() => $"Common_OnPortUpdated() port [{port?.id}] updated on [{CurrentID}, {NetId}]. Value: {port?.Value}, PrevValue: {port?.prevValue}, ValueType: {port?.valueType}, Tick: {NetworkLifecycle.Instance.Tick}");
        if (port.valueType != PortValueType.CONTROL && !NetworkLifecycle.Instance.IsHost())
        {
            //Multiplayer.LogDebug(() => $"Common_OnPortUpdated() Ignoring non-control port update for [{port.id}] on [{CurrentID}, {NetId}]");
            return;
        }

        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        if (float.IsNaN(port.prevValue) && float.IsNaN(port.Value))
            return;

        bool hasLastSent = lastSentPortValues.TryGetValue(portNetId, out float lastSentValue);
        float delta = Mathf.Abs(lastSentValue - port.Value);

        if (port.valueType == PortValueType.STATE)
        {
            if (!hasLastSent || lastSentValue != port.Value)
            {
                dirtyPorts.Add(portNetId);
            }
        }
        else
        {
            if (!hasLastSent || delta > MAX_PORT_DELTA || (port.Value == 0 && lastSentValue != 0))
            {
                dirtyPorts.Add(portNetId);
            }

            if (port.valueType == PortValueType.CONTROL)
            {
                if (portNetIdToBlockState.TryGetValue(portNetId, out var isBlocked) && isBlocked)
                {
                    Multiplayer.LogDebug(() => $"Common_OnPortUpdated() Control port [{port.id}, {portNetId}] is currently blocked on [{CurrentID}, {NetId}]");
                    return;
                }
                dirtyPorts.Add(portNetId);
            }
        }
    }

    private void Common_OnPaintThemeChange(TrainCarPaint paintController)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        if (paintController == null)
            return;

        dirtyPaints.Add(paintController);
    }

    private void Common_OnFuseUpdated(uint fuseNetId)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        dirtyFuses.Add(fuseNetId);
    }

    public void Common_UpdatePorts(CommonTrainPortsPacket packet)
    {
        if (!hasSimFlow)
            return;

        for (int i = 0; i < packet.PortIds.Length; i++)
        {
            var portId = GetPort(packet.PortIds[i]);
            if (simulationFlow.TryGetPort(portId, out Port port))
            {
                float value = packet.PortValues[i];

                if (port.type == PortType.EXTERNAL_IN)
                    port.ExternalValueUpdate(value);
                else
                    port.Value = value;
            }
            else
            {
                Multiplayer.LogWarning($"Failed to update port [\"portId\", {packet.PortIds[i]}] with value \"{packet.PortValues[i]}\" for [{CurrentID}, {NetId}]");
            }
        }
    }

    public void Common_UpdateFuses(CommonTrainFusesPacket packet)
    {
        if (!hasSimFlow)
            return;

        for (int i = 0; i < packet.FuseIds.Length; i++)
        {
            var fuseId = GetFuse(packet.FuseIds[i]);
            if (simulationFlow.TryGetFuse(fuseId, out Fuse fuse))
                fuse.ChangeState(packet.FuseValues[i]);
            else
                Multiplayer.LogWarning($"Failed to update fuse [\"fuseId\", {packet.FuseIds[i]}] with value \"{packet.FuseValues[i]}\" for [{CurrentID}, {NetId}]");
        }
    }

    public void Common_ReceiveCouplerInteraction(CommonCouplerInteractionPacket packet)
    {
        CouplerInteractionType flags = (CouplerInteractionType)packet.Flags;
        Coupler coupler = packet.IsFrontCoupler ? TrainCar?.frontCoupler : TrainCar?.rearCoupler;
        TrainCar otherCar = null;
        Coupler otherCoupler = null;

        ButtonBase buttonBase = coupler?.ChainScript?.screwButton.GetComponent<ButtonBase>();

        Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() couplerNetId: {NetId}, coupler is front: {packet.IsFrontCoupler}, flags: {flags}, otherCouplerNetId: {packet.OtherNetId}, otherCoupler is front: {packet.IsFrontOtherCoupler}");

        if (coupler == null)
        {
            Multiplayer.LogWarning($"Common_ReceiveCouplerInteraction() did not find coupler for [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}");
            return;
        }

        if (packet.OtherNetId != 0)
        {
            if (TryGet(packet.OtherNetId, out otherCar))
                otherCoupler = packet.IsFrontOtherCoupler ? otherCar?.frontCoupler : otherCar?.rearCoupler;
        }

        Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}, otherCouplerNetId: {packet.OtherNetId}");

        if (flags == CouplerInteractionType.NoAction)
        {
            Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() Interaction rejected! [{CurrentID}, {NetId}]");
            //our interaction was denied
            coupler.ChainScript?.knobGizmo?.ForceEndInteraction();
            couplerInteraction = null;

            if (coupler.ChainScript.state == originalState)
                return;

            switch (originalState)
            {
                case ChainCouplerInteraction.State.Parked:
                    StartCoroutine(ParkCoupler(coupler));
                    break;
                case ChainCouplerInteraction.State.Dangling:
                    if (coupler.ChainScript.state == ChainCouplerInteraction.State.Attached_Tight)
                        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Screw_Used);

                    StartCoroutine(DangleCoupler(coupler));
                    break;
                case ChainCouplerInteraction.State.Attached_Loose:
                    if (coupler.ChainScript.state == ChainCouplerInteraction.State.Attached_Tight)
                        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Screw_Used);
                    else
                        StartCoroutine(LooseAttachCoupler(coupler, originalCoupledTo));
                    break;
                case ChainCouplerInteraction.State.Attached_Tight:
                    if (coupler.ChainScript.state != ChainCouplerInteraction.State.Attached_Loose)
                        StartCoroutine(LooseAttachCoupler(coupler, originalCoupledTo));

                    coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Screw_Used);
                    break;
                default:
                    Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() Unable to return to last state! {originalState}");
                    break;
            }
            return;
        }
        if (flags == CouplerInteractionType.Start && coupler != couplerInteraction)
        {
            Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() Interaction started [{CurrentID}, {NetId}] isFront: {coupler.isFrontCoupler}");
            //We've received a start signal for a coupler we aren't interacting with
            //Another player must be interacting, so let's block us from tampering with it
            if (coupler?.ChainScript?.knobGizmo)
                coupler.ChainScript.knobGizmo.InteractionAllowed = false;
            if (buttonBase)
                buttonBase.InteractionAllowed = false;

            return;
        }

        if (coupler.ChainScript.state == ChainCouplerInteraction.State.Being_Dragged)
        {
            Multiplayer.LogDebug(() => $"Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}, otherCouplerNetId: {packet.OtherNetId} Being Dragged!");
            coupler.ChainScript?.knobGizmo?.ForceEndInteraction();
        }

        if (flags.HasFlag(CouplerInteractionType.CouplerCouple) && packet.OtherNetId != 0)
        {
            Multiplayer.LogDebug(() => $"1 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags} ");
            if (otherCar != null)
            {
                Multiplayer.LogDebug(() => $"2 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}");
                StartCoroutine(LooseAttachCoupler(coupler, otherCoupler));
            }
        }

        if (flags.HasFlag(CouplerInteractionType.CouplerPark))
        {
            Multiplayer.LogDebug(() => $"3 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}, current state: {coupler.state}, Chain state:{coupler.ChainScript.state}, isCoupled: {coupler.IsCoupled()}");

            if (coupler.ChainScript.state != ChainCouplerInteraction.State.Attached_Tight)
                StartCoroutine(ParkCoupler(coupler));
            else
                Multiplayer.LogWarning($"Received Park interaction for [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, but coupler is in the wrong state: {coupler.state}, Chain state:{coupler.ChainScript.state}, isCoupled: {coupler.IsCoupled()}");

            Multiplayer.LogDebug(() => $"4 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags} restorestate: {coupler.state}, current state: {coupler.state}, Chain state:{coupler.ChainScript.state}, isCoupled: {coupler.IsCoupled()}");
        }

        if (flags.HasFlag(CouplerInteractionType.CouplerDrop))
        {
            Multiplayer.LogDebug(() => $"5 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags} restorestate: {coupler.state}, current state: {coupler.state}, Chain state:{coupler.ChainScript.state}, isCoupled: {coupler.IsCoupled()}");

            if (coupler.ChainScript.state != ChainCouplerInteraction.State.Attached_Tight)
                StartCoroutine(DangleCoupler(coupler));
            else
                Multiplayer.LogWarning($"Received Dangle interaction for [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, but coupler is in the wrong state: {coupler.state}, Chain state:{coupler.ChainScript.state}, isCoupled: {coupler.IsCoupled()}");
        }

        if (flags.HasFlag(CouplerInteractionType.CouplerLoosen))
        {
            Multiplayer.LogDebug(() => $"6 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], flags: {flags} current state: {coupler.ChainScript.state}");
            if (coupler.ChainScript.state == ChainCouplerInteraction.State.Attached_Tight)
            {
                Multiplayer.LogDebug(() => $"7 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}");
                coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Screw_Used);
            }
            else if (coupler.ChainScript.CurrentState == ChainCouplerInteraction.State.Disabled && coupler.state == ChainCouplerInteraction.State.Attached_Tight)
            {
                //if it's disabled we'll use the internal routines and the state will restore when this player sees the coupling next
                coupler.SetChainTight(false);
            }
        }

        if (flags.HasFlag(CouplerInteractionType.CouplerTighten))
        {
            Multiplayer.LogDebug(() => $"8 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], flags: {flags} current state: {coupler.ChainScript.state}");
            if (coupler.ChainScript.state != ChainCouplerInteraction.State.Attached_Loose || !coupler.IsCoupled())
            {
                Multiplayer.LogWarning($"Incorrect coupling state for tightening! [{TrainCar?.ID}, {NetId}], flags: {flags} current state: {coupler.ChainScript.state} isCoupled: {coupler.IsCoupled()}");
            }
            if (coupler.ChainScript.state == ChainCouplerInteraction.State.Attached_Loose)
            {
                Multiplayer.LogDebug(() => $"9 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}");
                coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Screw_Used);
            }
            else if (coupler.ChainScript.CurrentState == ChainCouplerInteraction.State.Disabled && coupler.state == ChainCouplerInteraction.State.Attached_Loose)
            {
                //if it's disabled we'll use the internal routines and the state will restore when this player sees the coupling next
                coupler.SetChainTight(true);
            }
        }

        if (flags.HasFlag(CouplerInteractionType.CoupleViaUI))
        {
            //if hose connect also requested, then we want everything to connect, otherwise only connect the chain
            bool chainInteraction = !flags.HasFlag(CouplerInteractionType.HoseConnect);

            Multiplayer.LogDebug(() => $"10 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: [{flags}], other coupler: {otherCoupler != null}, chainInteraction: {chainInteraction}");
            if (otherCoupler != null)
            {
                Multiplayer.LogDebug(() => $"10A Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler state: {coupler.state}, other coupler state: {otherCoupler.state}, coupler coupledTo: {coupler?.coupledTo?.train?.ID}, other coupledTo: {otherCoupler?.coupledTo?.train?.ID}, chainInteraction: {chainInteraction}");
                var car = coupler.CoupleTo(otherCoupler, viaChainInteraction: chainInteraction);

                /* fix for bug in vanilla game */
                coupler.SetChainTight(true);
                if (coupler.ChainScript.enabled)
                {
                    coupler.ChainScript.enabled = false;
                    coupler.ChainScript.enabled = true;
                }
                /* end fix for bug */

                Multiplayer.LogDebug(() => $"10B Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], result: {car != null}");
                //todo: rework hose and MU interactions
            }
        }

        if (flags.HasFlag(CouplerInteractionType.UncoupleViaUI))
        {
            //if hose connect also requested, then we want everything to disconnect, otherwise only disconnect the chain
            bool chainInteraction = !flags.HasFlag(CouplerInteractionType.HoseDisconnect);

            Multiplayer.LogDebug(() => $"11 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}, chainInteraction: {chainInteraction}");
            CouplerLogic.Uncouple(coupler, viaChainInteraction: chainInteraction);

            /* fix for bug in vanilla game */
            coupler.state = ChainCouplerInteraction.State.Parked;
            if (coupler.ChainScript.enabled)
            {
                coupler.ChainScript.enabled = false;
                coupler.ChainScript.enabled = true;
            }
            /* end fix for bug */

            //todo: rework hose and MU interactions 
        }

        if (flags.HasFlag(CouplerInteractionType.CoupleViaRemote))
        {
            Multiplayer.LogDebug(() => $"12 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}, other coupler: {otherCoupler != null}");

            if (TryGetComponent<ExternalCouplingHandler>(out var couplingHandler))
                couplingHandler.Couple();
        }

        if (flags.HasFlag(CouplerInteractionType.UncoupleViaRemote))
        {
            Multiplayer.LogDebug(() => $"13 Common_ReceiveCouplerInteraction() [{TrainCar?.ID}, {NetId}], coupler is front: {packet.IsFrontCoupler}, flags: {flags}");
            if (coupler != null)
            {
                coupler.Uncouple(true, false, false, false);
                MultipleUnitModule.DisconnectCablesIfMultipleUnitSupported(coupler.train, coupler.isFrontCoupler, !coupler.isFrontCoupler);
            }
        }

        //presumably the interaction is now complete, release control to player
        if (coupler?.ChainScript?.knobGizmo)
            coupler.ChainScript.knobGizmo.InteractionAllowed = true;
        if (buttonBase)
            buttonBase.InteractionAllowed = true;
    }

    private IEnumerator LooseAttachCoupler(Coupler coupler, Coupler otherCoupler)
    {
        if (coupler == null || coupler.ChainScript == null ||
            otherCoupler == null || otherCoupler.ChainScript == null ||
            otherCoupler.ChainScript.ownAttachPoint == null)
        {
            Multiplayer.LogDebug(() => $"LooseAttachCoupler() [{TrainCar?.ID}], Null reference! Coupler: {coupler != null}, chainscript: {coupler?.ChainScript != null}, other coupler: {otherCoupler != null}, other chainscript: {otherCoupler?.ChainScript != null}, other attach point: {otherCoupler?.ChainScript?.ownAttachPoint}");
            yield break;
        }

        ChainCouplerInteraction ccInteraction = coupler.ChainScript;

        if (ccInteraction.CurrentState == ChainCouplerInteraction.State.Disabled)
        {
            //since it's disabled FSM events won't fire. Force a coupling if required, otherwise set state ready for player visibility trigger

            if (coupler.coupledTo == null)
                coupler.CoupleTo(otherCoupler, true, true);
            else
                coupler.state = ChainCouplerInteraction.State.Attached_Loose;

            yield break;
        }

        //Simulate player pickup
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Picked_Up_By_Player);

        //Set the knob position to the other coupler's hook
        Vector3 targetHookPos = otherCoupler.ChainScript.ownAttachPoint.transform.position;
        coupler.ChainScript.knob.transform.position = targetHookPos;

        //allow the follower and IK solver to update
        coupler.ChainScript.Update_Being_Dragged();

        //we need to allow the IK solver to calculate the chain ring anchor's position over a number of iterations
        int x = 0;
        float distance = float.MaxValue;
        //game checks for Vector3.Distance(this.chainRingAnchor.position, this.closestAttachPoint.transform.position) < attachDistanceThreshold;
        while (distance >= ChainCouplerInteraction.attachDistanceThreshold && x < MAX_COUPLER_ITERATIONS)
        {
            distance = Vector3.Distance(ccInteraction.chainRingAnchor.position, targetHookPos);

            x++;
            yield return new WaitForSeconds(ccInteraction.ROTATION_SMOOTH_DURATION);
        }

        Multiplayer.LogDebug(() => $"LooseAttachCoupler() [{TrainCar?.ID}], Coupler: {coupler?.train?.ID}, OtherCoupler: {otherCoupler?.train?.ID}, Distance: {distance}, Iterations: {x}");

        //Drop the chain
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Dropped_By_Player);

        Multiplayer.LogDebug(() => $"LooseAttachCoupler() [{TrainCar?.ID}], Coupler: {coupler?.train?.ID}, OtherCoupler: {otherCoupler?.train?.ID}, CoupledTo: {coupler.coupledTo?.train?.ID}");
    }

    private IEnumerator ParkCoupler(Coupler coupler)
    {
        ChainCouplerInteraction ccInteraction = coupler.ChainScript;

        if (ccInteraction.CurrentState == ChainCouplerInteraction.State.Disabled)
        {
            //since it's disabled FSM events won't fire, but state will be restored when the coupling is visible to the current player
            if (coupler.state == ChainCouplerInteraction.State.Attached_Loose && coupler.coupledTo != null)
                coupler.Uncouple(true, false, false, true);

            coupler.state = ChainCouplerInteraction.State.Parked;

            yield break;
        }

        //Simulate player pickup
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Picked_Up_By_Player);

        //Set the knob position
        Vector3 parkPos = coupler.ChainScript.parkedAnchor.position;

        coupler.ChainScript.knob.transform.position = parkPos;

        //allow the follower and IK solver to update
        coupler.ChainScript.Update_Being_Dragged();

        //we need to allow the IK solver to calculate the chain ring anchor's position over a number of iterations
        int x = 0;
        float distance = float.MaxValue;
        //game checks for Vector3.Distance(this.chainRingAnchor.position, this.parkedAnchor.position) < parkDistanceThreshold;
        //need to make sure we are closer than the threshold before dropping
        while (distance > ChainCouplerInteraction.parkDistanceThreshold && x < MAX_COUPLER_ITERATIONS)
        {
            distance = Vector3.Distance(ccInteraction.chainRingAnchor.position, ccInteraction.parkedAnchor.position);

            x++;
            yield return new WaitForSeconds(ccInteraction.ROTATION_SMOOTH_DURATION);
        }

        //Drop the chain
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Dropped_By_Player);
    }

    private IEnumerator DangleCoupler(Coupler coupler)
    {
        ChainCouplerInteraction ccInteraction = coupler.ChainScript;

        if (ccInteraction.CurrentState == ChainCouplerInteraction.State.Disabled)
        {
            //since it's disabled FSM events won't fire, but state will be restored when the coupling is visible to the current player
            if (coupler.state == ChainCouplerInteraction.State.Attached_Loose && coupler.coupledTo != null)
                coupler.Uncouple(true, false, false, true);

            coupler.state = ChainCouplerInteraction.State.Dangling;

            yield break;
        }

        //Simulate player pickup
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Picked_Up_By_Player);

        Vector3 parkPos = coupler.ChainScript.parkedAnchor.position;

        //Set the knob position
        coupler.ChainScript.knob.transform.position = parkPos + Vector3.down; //ensure we are not near the park anchor or other car's anchor

        //allow the follower and IK solver to update
        coupler.ChainScript.Update_Being_Dragged();

        //we need to allow the IK solver to calculate the chain ring anchor's position over a number of iterations
        int x = 0;
        float distance = float.MinValue;
        //game checks for Vector3.Distance(this.chainRingAnchor.position, this.parkedAnchor.position) < parkDistanceThreshold;
        //to determine if it should be parked or dangled, need to make sure we are at least at the threshold before dropping
        while (distance <= ChainCouplerInteraction.parkDistanceThreshold && x < MAX_COUPLER_ITERATIONS)
        {
            distance = Vector3.Distance(ccInteraction.chainRingAnchor.position, ccInteraction.parkedAnchor.position);

            x++;
            yield return new WaitForSeconds(ccInteraction.ROTATION_SMOOTH_DURATION);
        }

        //Drop the chain
        coupler.ChainScript.fsm.Fire(ChainCouplerInteraction.Trigger.Dropped_By_Player);
    }

    public void Common_ReceivePaintThemeUpdate(TrainCarPaint.Target target, PaintTheme paint)
    {
        TrainCarPaint targetPaint = null;

        if (target == TrainCarPaint.Target.Interior)
        {
            Multiplayer.LogDebug(() => $"Received Paint Theme update for [{CurrentID}, {NetId}], targeting Interior");
            targetPaint = TrainCar.PaintInterior;
        }
        else if (target == TrainCarPaint.Target.Exterior)
        {
            Multiplayer.LogDebug(() => $"Received Paint Theme update for [{CurrentID}, {NetId}], targeting Exterior");
            targetPaint = TrainCar.PaintExterior;
        }

        if (targetPaint == null /*|| !targetPaint.IsSupported(paint)*/  )
        {
            Multiplayer.LogWarning($"Received Paint Theme update for [{CurrentID}, {NetId}], but {paint?.AssetName} is not supported");
            return;
        }

        targetPaint.currentTheme = paint;
        targetPaint.UpdateTheme();
        TrainCar.OnPaintThemeChanged(targetPaint);
    }

    public bool HasPlayers()
    {
        if (NetworkLifecycle.Instance.IsHost())
            return PlayerManager.Car == TrainCar || serverPlayersInCar.Count > 0;

        return PlayerManager.Car == TrainCar || networkedPlayersInCar.Count > 0;
    }

    /// <summary>
    /// Checks whether a train car is in use for the purposes of calling Work Trains.
    /// TrainCar is considered in use if:
    ///  - There are any players on the TrainCar or its connected carriages/locomotives.
    ///  - The TrainCar is moving.
    ///  - The TrainCar is connected to a train with one or more active jobs.
    ///  - The TrainCar is connected to another locomotive with a running engine.
    ///  - The TrainCar's engine (or equivalent) is running.
    ///  - The TrainCar's handbrake is not applied.
    ///  - The TrainCar's engine is off and brake applied, but has been occupied in the last 10 mins
    /// </summary>
    /// <param name="reason">The reason the TrainCar is in use</param>
    /// <param name="timeout">The time remaining until the TrainCar is no longer considered in use</param>
    /// <returns>True if the train car is in use, false otherwise</returns>
    public bool InUse(out LocoInUseData.LocoInUseReason reason, out float timeout)
    {
        HashSet<NetworkedTrainCar> visited = new(TrainCar?.trainset?.cars?.Count ?? 1);

        // Derailed demos shouldn't be blocked if brakes aren't applied
        bool isDerailed = TrainCar.derailed && TrainCar.playerSpawnedCar;
        //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] isDerailed: {isDerailed}, derailed: {TrainCar.derailed}, playerSpawnedCar: {TrainCar.playerSpawnedCar}");
        return InUse(isDerailed, out reason, out timeout, visited);
    }

    /// <summary>
    /// Internal implementation of InUse() that tracks visited cars to avoid infinite recursion when checking connected cars.
    /// </summary>
    /// <param name="reason">The reason the TrainCar is in use</param>
    /// <param name="timeout">The time remaining until the TrainCar is no longer considered in use</param>
    /// <param name="visited">A set of already visited NetworkedTrainCar instances to avoid infinite recursion</param>
    /// <returns>True if the train car is in use, false otherwise</returns>
    private bool InUse(bool isDerailed, out LocoInUseData.LocoInUseReason reason, out float timeout, HashSet<NetworkedTrainCar> visited)
    {
        timeout = 0f;

        if (!visited.Add(this))
        {
            //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] already visited, reason: None");
            reason = LocoInUseData.LocoInUseReason.None;
            return false;
        }

        //Multiplayer.LogDebug(() => $"InUse() called for [{CurrentID}, {NetId}]");
        // Common checks for all TrainCars
        if (!TrainCar.isStationary)
        {
            //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] is moving, reason: Moving");
            reason = LocoInUseData.LocoInUseReason.Moving;
            return true;
        }

        if (HasPlayers())
        {
            //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] has players, reason: Occupied");
            reason = LocoInUseData.LocoInUseReason.Occupied;
            return true;
        }

        bool handbrakeApplied = TrainCar.carType == TrainCarType.HandCar ||
                                brakeSystem?.handbrakePosition > 0f;
        if (!handbrakeApplied && !isDerailed)
        {
            //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] handbrake not applied, reason: Brakes isDerailed: {isDerailed}");
            reason = LocoInUseData.LocoInUseReason.Brakes;
            return true;
        }

        // Specific checks for locomotives and carriages
        bool result = false;
        if (TrainCar.IsLoco)
            result = LocoInUse(isDerailed, out reason, out timeout);
        else if (!isDerailed)
            result = CarInUse(out reason);
        else
            reason = LocoInUseData.LocoInUseReason.None;

        if (!result && !isDerailed && OtherCarInUse(isDerailed, visited))
        {
            //Multiplayer.LogDebug(() => $"InUse() [{CurrentID}, {NetId}] other car in use");
            reason = LocoInUseData.LocoInUseReason.CoupledCarInUse;
            return true;
        }

        return result;
    }

    /// <summary>
    /// Checks whether a locomotive is in use for the purposes of calling Work Trains.
    /// </summary>
    /// <param name="reason">The reason the locomotive is in use</param>
    /// <param name="timeout">The time remaining until the locomotive is no longer considered in use</param>
    /// <returns>True if the locomotive is in use, false otherwise</returns>
    private bool LocoInUse(bool isDerailed, out LocoInUseData.LocoInUseReason reason, out float timeout)
    {
        timeout = 0f;

        bool engineRunning = EngineRunning();

        if (engineRunning && !isDerailed)
        {
            //Multiplayer.LogDebug(() => $"LocoInUse() [{CurrentID}, {NetId}] engine running, reason: Engine");
            reason = LocoInUseData.LocoInUseReason.Engine;
            return true;
        }

        // Check if the TrainCar has been occupied in the last n minutes
        float elapsedTime = TrainCar.visitChecker?.recentlyVisitedTimer?.ElapsedTime ?? 0f;
        if ((TrainCar.visitChecker?.IsRecentlyVisited ?? false) &&
            TrainCar.visitChecker?.recentlyVisitedTimer?.ElapsedTime < IN_USE_THRESHOLD)
        {
            timeout = IN_USE_THRESHOLD - elapsedTime;
            reason = LocoInUseData.LocoInUseReason.Timeout;
            var tempTimeout = timeout;
            //Multiplayer.LogDebug(() => $"LocoInUse() [{CurrentID}, {NetId}] recently visited, reason: Timeout, timeout: {tempTimeout}");
            return true;
        }

        //Multiplayer.LogDebug(() => $"LocoInUse() [{CurrentID}, {NetId}] no other train cars in use, reason: None");
        reason = LocoInUseData.LocoInUseReason.None;
        return false;
    }

    /// <summary>
    /// Checks whether a non-locomotive train car is in use for the purposes of calling Work Trains.
    /// </summary>
    /// <param name="reason">The reason the TrainCar is in use</param>
    /// <returns>True if the car is in use, false otherwise</returns>
    private bool CarInUse(out LocoInUseData.LocoInUseReason reason)
    {
        if (!TrainCar.IsLoco && TrainCar.logicCar != null)
        {
            var job = JobsManager.Instance.GetJobOfCar(TrainCar.logicCar, true);
            if (job != null)
            {
                //Multiplayer.LogDebug(() => $"CarInUse() [{CurrentID}, {NetId}] is part of job [{job.ID}]");
                reason = LocoInUseData.LocoInUseReason.Job;
                return true;
            }
        }

        //Multiplayer.LogDebug(() => $"CarInUse() [{CurrentID}, {NetId}] no jobs found, reason: None");
        reason = LocoInUseData.LocoInUseReason.None;
        return false;
    }

    /// <summary>
    /// Checks whether the train car's engine (or equivalent) is running.
    /// TrainCars without an engine (e.g. electric locos) are considered to have a running engine if the main/electrics fuse is on.
    /// </summary>
    /// <returns>True if the engine is running, false otherwise</returns>
    public bool EngineRunning()
    {
        if (!TrainCar.IsLoco)
            return false;

        // Steam and diesel locos have an engineOn reader port, electric locos do not
        var engineReaderPort = simController?.controlsOverrider?.EngineOnReader?.engineOnPort;
        if (engineReaderPort != null)
        {
            return engineReaderPort.Value > 0f;
        }
        else
        {
            // Attempt to get the state of the main/electrics fuse
            if (interiorControlsManager?.TryGetControl(ControlType.ElectricsFuse, out var controlReference) ?? false)
            {
                var fuseFeeder = controlReference.controlImplBase.GetComponent<InteractableFuseFeeder>();
                string fuseId = fuseFeeder?.fuseId;
                simulationFlow.TryGetFuse(fuseId, out Fuse fuse);
                return fuse?.State ?? false;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether any other train cars in the same trainset are in use for the purposes of calling Work Trains.
    /// </summary>
    /// <param name="visited"></param>
    /// <returns>True if a coupled car is in use, false otherwise</returns>
    private bool OtherCarInUse(bool isDerailed, HashSet<NetworkedTrainCar> visited)
    {
        //Multiplayer.LogDebug(() => $"OtherCarInUse() called for [{CurrentID}, {NetId}]");

        if (TrainCar.trainset == null || TrainCar.trainset.cars == null || TrainCar.trainset.cars.Count == 0)
        {
            //Multiplayer.LogDebug(() => $"OtherCarInUse() [{CurrentID}, {NetId}] trainset is null or empty, reason: None");
            return false;
        }

        foreach (var otherTrainCar in TrainCar.trainset.cars)
        {
            if (otherTrainCar == null || otherTrainCar == TrainCar)
                continue;

            if (TryGetFromTrainCar(otherTrainCar, out var otherNetTrainCar) && otherNetTrainCar != null)
                if (otherNetTrainCar.InUse(isDerailed, out _, out _, visited))
                    return true;
        }

        return false;
    }
    #endregion

    #region Client

    private IEnumerator Client_InitLater()
    {
        while ((client_bogie1Queue = bogie1.GetComponent<NetworkedBogie>()) == null)
            yield return null;
        while ((client_bogie2Queue = bogie2.GetComponent<NetworkedBogie>()) == null)
            yield return null;

        Client_Initialized = true;
    }

    /// <summary>
    /// Checks whether the received position exceeds the threshold for a simple position update.
    /// </summary>
    /// <param name="movementPart">The movement part containing the position and movement data.</param>
    /// <param name="tick">The tick at which the update was received.</param>
    /// <returns>Returns true if a hard correction is required, false otherwise.</returns>
    public bool Client_CheckThreshold(in TrainsetMovementPart movementPart, uint tick)
    {
        if (tick <= lastTickProcessed)
            return false;

        if (movementPart.typeFlag.HasFlag(TrainsetMovementPart.MovementType.Position))
        {
            Vector3 worldPos = movementPart.Position + WorldMover.currentMove;

            // Only apply position update if change exceeds threshold (stops cariages from jittering while stationary)
            float positionDelta = Vector3.Distance(TrainCar.transform.position, worldPos);

            if (positionDelta > NetworkTrainsetWatcher.MAX_POSITION_DELTA)
            {
                Multiplayer.LogDebug(() => $"Client_CheckThreshold() Position delta {positionDelta} exceeds threshold for car {CurrentID} at tick {tick}, current position: {TrainCar.transform.position}, expected position: {worldPos}");
                return true;
            }
        }

        return false;
    }

    public void Client_BeginHardCorrection(in TrainsetMovementPart movementPart, uint tick)
    {
        if (!Client_Initialized || TrainCar.rb == null)
            return;

        wasKinematicBeforeHardCorrection = TrainCar.rb.isKinematic;

        TrainCar.rb.isKinematic = true;
        TrainCar.rb.velocity = Vector3.zero;
        TrainCar.rb.angularVelocity = Vector3.zero;

        if (movementPart.typeFlag == TrainsetMovementPart.MovementType.RigidBody)
        {
            if (TrainCar.isEligibleForSleep)
                TrainCar.ForceOptimizationState(false);

            if (!TrainCar.derailed)
                TrainCar.Derail();

            IncludedData flags = (IncludedData)movementPart.RigidbodySnapshot.IncludedDataFlags;

            if (flags.HasFlag(IncludedData.Position))
                TrainCar.rb.position = movementPart.RigidbodySnapshot.Position + WorldMover.currentMove;
            if (flags.HasFlag(IncludedData.Rotation))
                TrainCar.rb.rotation = movementPart.RigidbodySnapshot.Rotation;
        }
        else if (movementPart.typeFlag.HasFlag(TrainsetMovementPart.MovementType.Position))
        {
            TrainCar.rb.position = movementPart.Position + WorldMover.currentMove;
            TrainCar.rb.rotation = movementPart.Rotation;
        }

        // Clear anything queued so stale interpolation data doesn't fight the new state
        Client_trainSpeedQueue.Clear();
        Client_trainRigidbodyQueue.Clear();
        client_bogie1Queue.Clear();
        client_bogie2Queue.Clear();
        TrainCar.stress.ResetTrainStress();
    }

    public void Client_EndHardCorrection(in TrainsetMovementPart movementPart, uint tick)
    {
        if (!Client_Initialized || TrainCar.rb == null)
            return;

        TrainCar.rb.isKinematic = wasKinematicBeforeHardCorrection;
        kinematicCycles = 0;

        Client_ReceiveTrainPhysicsUpdate(in movementPart, tick);
    }

    public void Client_ReceiveTrainPhysicsUpdate(in TrainsetMovementPart movementPart, uint tick)
    {
        if (!Client_Initialized)
            return;

        if (tick <= lastTickProcessed)
        {
            //Multiplayer.LogWarning($"Received physics update for car {CurrentID} at tick {tick}, but last tick processed was {lastTickProcessed}");
            return;
        }

        lastTickProcessed = tick;

        if (movementPart.typeFlag == TrainsetMovementPart.MovementType.RigidBody)
        {
            //Vector3 expectedPosition = movementPart.RigidbodySnapshot.Position + WorldMover.currentMove;
            //Multiplayer.LogDebug(() => $"Processing derailed physics for car {CurrentID} at tick {tick}, current position: {TrainCar.transform.position} expected position: {expectedPosition}");

            if (TrainCar.isEligibleForSleep)
                TrainCar.ForceOptimizationState(false);

            TrainCar.Derail();
            movementPart.RigidbodySnapshot.Apply(TrainCar.rb);

            //    Client_trainRigidbodyQueue.ReceiveSnapshot(movementPart.RigidbodySnapshot, tick);

            //Multiplayer.LogDebug(() => $"Derailed car {TrainCar.ID} positioned at {TrainCar.transform.position}");
        }
        else
        {
            // Only force awake if there's movement
            if (Mathf.Abs(movementPart.Speed) > NetworkTrainsetWatcher.VELOCITY_THRESHOLD && TrainCar.isEligibleForSleep)
                TrainCar.ForceOptimizationState(false);

            //move the car to the correct position first - maybe?
            if (movementPart.typeFlag.HasFlag(TrainsetMovementPart.MovementType.Position))
            {
                Vector3 worldPos = movementPart.Position + WorldMover.currentMove;

                // Only apply position update if change exceeds threshold (stops cariages from jittering while stationary)
                float positionDelta = Vector3.Distance(TrainCar.transform.position, worldPos);

                if (positionDelta > POSITION_UPDATE_THRESHOLD)
                {
                    if (TrainCar.rb != null)
                    {
                        TrainCar.rb.MovePosition(worldPos);

                        //TrainCar.rb.MoveRotation(movementPart.Rotation); // removed due to motion sickness issues
                    }

                    //clear the queues?
                    Client_trainSpeedQueue.Clear();
                    Client_trainRigidbodyQueue.Clear();
                    client_bogie1Queue.Clear();
                    client_bogie2Queue.Clear();

                    TrainCar.stress.ResetTrainStress();
                }
            }

            Client_trainSpeedQueue.ReceiveSnapshot(movementPart.Speed, tick);
            TrainCar.stress.slowBuildUpStress = movementPart.SlowBuildUpStress;
            client_bogie1Queue.ReceiveSnapshot(movementPart.Bogie1, tick);
            client_bogie2Queue.ReceiveSnapshot(movementPart.Bogie2, tick);
        }

        bool kinematic = movementPart.Speed < NetworkTrainsetWatcher.VELOCITY_THRESHOLD && (movementPart.RigidbodySnapshot != null && movementPart.RigidbodySnapshot.Velocity.magnitude < NetworkTrainsetWatcher.VELOCITY_THRESHOLD);

        if (kinematic && kinematicCycles < MIN_KINEMATIC_CYCLES)
            kinematicCycles++;
        else
            TrainCar.rb.isKinematic = kinematic;

        if (!kinematic)
        {
            kinematicCycles = 0;
            TrainCar.rb.isKinematic = kinematic;
        }
    }

    public void Client_ReceiveBrakeStateUpdate(ClientboundBrakeStateUpdatePacket packet)
    {
        if (brakeSystem == null)
            return;

        if (!hasSimFlow)
            return;

        brakeSystem.SetMainReservoirPressure(packet.MainReservoirPressure);

        brakeSystem.brakePipePressure = packet.BrakePipePressure;
        brakeSystem.brakeset.pipePressure = packet.BrakePipePressure;

        brakeSystem.brakeCylinderPressure = packet.BrakeCylinderPressure;

        if (brakeSystem.heatController == null)
            return;

        brakeSystem.heatController.overheatPercentage = packet.OverheatPercent;
        brakeSystem.heatController.overheatReductionFactor = packet.OverheatReductionFactor;
        brakeSystem.heatController.temperature = packet.Temperature;
    }

    private void Client_OnFireboxAddCoal(float coalMassDelta)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        if (coalMassDelta <= 0)
            return;

        NetworkLifecycle.Instance.Client.LogDebug(() => $"Client_OnFireboxAddCoal({CurrentID}): coalMassDelta: {coalMassDelta}");
        NetworkLifecycle.Instance.Client.SendAddCoal(NetId, coalMassDelta);
    }

    private void Client_OnIgnite(float ignition)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        if (ignition == 0f)
            return;

        NetworkLifecycle.Instance.Client.LogDebug(() => $"Common_OnIgnite({CurrentID})");
        NetworkLifecycle.Instance.Client.SendFireboxIgnition(NetId);
    }

    private void Client_OnCoalPileInteraction(float coalMassDelta)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        NetworkLifecycle.Instance.Client.LogDebug(() => $"Client_OnCoalPileInteraction({CurrentID}): coalMassDelta: {coalMassDelta}");
        NetworkLifecycle.Instance.Client.SendTenderCoalPileInteraction(NetId, coalMassDelta);
    }

    public void Client_CouplerStateChange(ChainCouplerInteraction.State state, Coupler coupler)
    {
        //Multiplayer.LogDebug(() => $"1 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}], coupler is front: {coupler?.isFrontCoupler}");

        //if we are processing a packet, then these state changes are likely triggered by a received update, not player interaction
        //in future, maybe patch OnGrab() or add logic to add/remove action subscriptions
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        CouplerInteractionType interactionFlags = CouplerInteractionType.NoAction;
        Coupler otherCoupler = null;

        switch (state)
        {
            case ChainCouplerInteraction.State.Being_Dragged:
                couplerInteraction = coupler;
                originalState = coupler.state;
                originalCoupledTo = coupler.coupledTo;
                interactionFlags = CouplerInteractionType.Start;
                //Multiplayer.LogDebug(() => $"3 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}]");
                break;

            case ChainCouplerInteraction.State.Attached_Loose:
                if (couplerInteraction != null)
                {
                    //couldn't find an appropriate constant in the game code, other than the default value
                    //at B99.3 this distance is 1.5f for both default and constant/magic number
                    otherCoupler = coupler.GetFirstCouplerInRange();
                    //Multiplayer.LogDebug(() => $"4 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}] coupledTo: {coupler?.coupledTo?.train?.ID}, first Coupler: {otherCoupler?.train?.ID}");
                    interactionFlags = CouplerInteractionType.CouplerCouple;
                }
                break;

            case ChainCouplerInteraction.State.Parked:
                if (couplerInteraction != null)
                {
                    //Multiplayer.LogDebug(() => $"6 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}]");
                    interactionFlags = CouplerInteractionType.CouplerPark;
                }
                break;

            case ChainCouplerInteraction.State.Dangling:
                if (couplerInteraction != null)
                {
                    //Multiplayer.LogDebug(() => $"7 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}]");
                    interactionFlags = CouplerInteractionType.CouplerDrop;
                }
                break;

            default:
                //nothing to do
                break;
        }

        if (interactionFlags != CouplerInteractionType.NoAction)
        {
            //Multiplayer.LogDebug(() => $"8 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}], coupler is front: {coupler?.isFrontCoupler}, Sending: {interactionFlags}");
            NetworkLifecycle.Instance.Client.SendCouplerInteraction(interactionFlags, coupler, otherCoupler);

            //finished interaction, clear flag
            if (interactionFlags != CouplerInteractionType.Start)
                couplerInteraction = null;

            return;
        }
        //Multiplayer.LogDebug(() => $"9 Client_CouplerStateChange({state}) trainCar: [{TrainCar?.ID}, {NetId}]");
    }

    private void Client_ControlGrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"Client_ControlGrabbed() Control {control.name}, car: {CurrentID}");
        if (!controlToPortNetId.TryGetValue(control, out var portNetId))
        {
            Multiplayer.LogWarning($"Control \"{control.name}\" grabbed but netId not found on TrainCar \"{CurrentID}\", hash: {control.GetHashCode()}, instance: {control.GetInstanceID()}");
            return;
        }

        if (portNetIdToBlockState.TryGetValue(portNetId, out var isBlocked) && isBlocked)
        {
            Multiplayer.LogDebug(() => $"Client_ControlGrabbed() Control [{control.name}, {portNetId}] is blocked on car {CurrentID}, ending interaction");
            control.ForceEndInteraction();
        }
        else
        {
            Multiplayer.LogDebug(() => $"Client_ControlGrabbed() Control [{control.name}, {portNetId}] is not blocked on car {CurrentID}, requesting authority");
            NetworkLifecycle.Instance.Client?.SendTrainControlAuthorityRequest(NetId, portNetId, true);
        }
    }

    private void Client_ControlScrolled(InteriorControlsManager.ControlReference controlRef)
    {
        if (controlRef.controlImplBase.IsHoverScrolled())
        {
            // Currently scrolling
            Multiplayer.LogDebug(() => $"Client_ControlScrolled() Control {controlRef.overridableBaseControl.ControlType} on car {CurrentID} was scrolled while hovered, requesting authority");

            if (!scrollingControls.ContainsKey(controlRef.scrollable))
            {
                scrollingControls[controlRef.scrollable] = StartCoroutine(Client_CheckScrolling(controlRef));
                Client_ControlGrabbed(controlRef.controlImplBase);
            }
        }
    }

    private IEnumerator Client_CheckScrolling(InteriorControlsManager.ControlReference controlRef)
    {
        // Wait for player to stop scrolling
        while (controlRef.controlImplBase.IsHoverScrolled())
            yield return new WaitForSecondsRealtime(0.5f);

        Multiplayer.LogDebug(() => $"Client_CheckScrolling() Control {controlRef.overridableBaseControl.ControlType} on car {CurrentID} scrolling complete");
        scrollingControls.Remove(controlRef.scrollable);
        Client_ControlUngrabbed(controlRef.controlImplBase);
    }

    private void Client_ControlUngrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"Client_ControlUngrabbed() Control {control.name}, car: {CurrentID}");
        if (!controlToPortNetId.TryGetValue(control, out var portNetId))
        {
            Multiplayer.LogWarning($"Control \"{control.name}\" ungrabbed but netId not found on TrainCar \"{CurrentID}\"");
            return;
        }

        if (!portNetIdToBlockState.ContainsKey(portNetId))
            portNetIdToBlockState[portNetId] = false;

        if (portNetIdToBlockState.TryGetValue(portNetId, out var isBlocked) && !isBlocked)
        {
            var hinge = control.GetComponent<HingeJoint>();
            var cj = control.GetComponent<ConfigurableJoint>();
            if (hinge != null && hinge.useSpring || cj != null)
            {
                StartCoroutine(WaitForControlToSettle(control, portNetId));
                return;
            }
            Multiplayer.LogDebug(() => $"Client_ControlUngrabbed() Control [{control.name}, {portNetId}] not blocked, releasing authority for car {CurrentID}");
            NetworkLifecycle.Instance.Client?.SendTrainControlAuthorityRequest(NetId, portNetId, false);
        }
    }

    private IEnumerator WaitForControlToSettle(ControlImplBase control, uint portNetId)
    {
        var time = Time.time;

        var oldValue = float.MaxValue;

        try
        {
            while (!UnloadWatcher.isUnloading && !Mathf.Approximately(control.Value, oldValue))
            {
                oldValue = control.Value;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
        finally
        {
            Multiplayer.LogDebug(() => $"WaitForControlToSettle() Control [{control?.name}, {portNetId}], releasing authority for car {CurrentID} after {Time.time - time}");
            NetworkLifecycle.Instance.Client?.SendTrainControlAuthorityRequest(NetId, portNetId, false);
        }
    }

    public void Client_ReceiveAuthorityUpdate(uint portNetId, ControlAuthorityState state)
    {
        bool shouldBlock = state == ControlAuthorityState.Blocked || state == ControlAuthorityState.Denied;
        bool shouldEnable = !shouldBlock;
        portNetIdToBlockState[portNetId] = shouldBlock;

        Multiplayer.LogDebug(() => $"Client_ReceiveAuthorityUpdate({portNetId}, {state}) for [{CurrentID}, {NetId}]");

        if (!portNetIdToControl.TryGetValue(portNetId, out var control) || control == null)
            return;

        var whistleRopeController = control?.transform.parent.GetComponentInChildren<WhistleRopeController>();
        var handCarBarController = control?.transform.parent.GetComponentInChildren<HandcarBarController>();
        var hinge = control.GetComponent<HingeJoint>();
        Multiplayer.LogDebug(() => $"Client_ReceiveAuthorityUpdate() Control [{control.name}, {portNetId}] for car [{CurrentID}, {NetId}], shouldBlock: {shouldBlock}, isHinge: {hinge != null}, isWhistle: {whistleRopeController != null}");

        if (shouldBlock)
        {
            control.ForceEndInteraction();

            if ((control is IScrollable scrollable) && scrollingControls.TryGetValue(scrollable, out var isScrolling) && isScrolling != null)
            {
                StopCoroutine(isScrolling);
                scrollingControls.Remove(scrollable);
            }
        }

        control.BlockControl(shouldBlock);
        control.InteractionAllowed = shouldEnable;

        if (hinge != null)
            control.enabled = shouldEnable;

        if (whistleRopeController != null)
            whistleRopeController.enabled = shouldEnable;

        if (handCarBarController != null)
            handCarBarController.enabled = shouldEnable;
    }

    public void Client_PlayerDisconnected(NetworkedPlayer player)
    {
        Multiplayer.LogDebug(() => $"Client_PlayerDisconnected({player?.Username}) car [{CurrentID}, {NetId}]");
        networkedPlayersInCar.Remove(player);
        TrainCar?.visitChecker?.OnPlayerCarChanged(TrainCar);
    }

    public void Client_PlayerOnCar(NetworkedPlayer player)
    {
        Multiplayer.LogDebug(() => $"Client_PlayerOnCar({player?.Username}) car [{CurrentID}, {NetId}], OccupiedCar: {player?.OccupiedCar?.CurrentID}");
        if (player == null || player.OccupiedCar == TrainCar)
            return;

        player?.OccupiedCar?.Client_RemovePlayer(player);

        networkedPlayersInCar.Add(player);
        TrainCar?.visitChecker?.OnPlayerCarChanged(TrainCar);
    }

    public void Client_RemovePlayer(NetworkedPlayer player)
    {
        Multiplayer.LogDebug(() => $"Client_RemovePlayer({player?.Username}) car [{CurrentID}, {NetId}]");
        networkedPlayersInCar.Remove(player);
        TrainCar?.visitChecker?.OnPlayerCarChanged(TrainCar);
    }
    #endregion
}
