# Game-world item synchronization plan

## Scope and investigation method

This plan treats an **item** as a grabbable `DV.CabControls.ItemBase` object in the world or inventory. Fixed world controls (junction levers, turntables, cash registers, beds, pit-stop plugs, train controls, couplers, hoses, and MU cables) were inspected as interaction dependencies but are not duplicated here because they already have dedicated network components/packets.

The inventory was derived from:

- `ItemBase` and its generic use/grab events;
- every `IItemUse`/`IItemUseAnimated` implementation;
- every behavior subscribing to `ItemBase.Used`;
- every item behavior persisting through `ItemSaveData`;
- item containers/magazines and gadget placement/wiring code.

Prefab-only inert props cannot be named reliably from decompiled C# because `ItemsConfig.items` is populated from Unity assets. They need no special adapter: the generic `NetworkedItem` path applies to every prefab with an `ItemBase`.

## What is already synchronized

### Generic item lifecycle

`ItemBasePatch` attaches `NetworkedItem` to every item. `NetworkedItem` and `NetworkedItemManager` already synchronize:

- stable per-session item ID and prefab name;
- creation, relevance-based replication, cache reuse, and destruction;
- player ownership and hand/inventory transitions;
- dropped and thrown position/rotation plus throw impulse;
- coupler snap-point attachment;
- host validation of pickup/drop actions;
- arbitrary primitive item state via `RegisterTrackedValue`.

`GrabHandlerItem`, `RespawnOnDropPatch`, `StorageControllerPatch`, and the remote-player inventory methods bridge the game interaction/inventory APIs rather than recreating them.

### Existing item-specific coverage

| Item/behavior | Existing synchronization |
| --- | --- |
| Flashlight | Tracked light intensity/color, beam color, battery power, and button state. |
| Lighter | Tracked lid and flame state; received values call existing lid/flame functions. |
| Shovel | Tracked coal capacity and loaded coal mass. |
| Locomotive remote | Existing remote-controller patch reuses coupler packets; locomotive controls are already train-state synchronized. |
| Money | Generic destruction plus server-authoritative cash-register/money packets. |
| Keys/padlocks | Generic item destruction and existing key/cash/job gameplay patches; no durable mutable state remains on a used key. |
| Job booklets/reports | Job creation/update/validation packets synchronize the authoritative job effect. |
| Paint cans/sprayer | Train paint changes already flow through `NetworkedTrainCar` and `CommonPaintThemePacket`. |
| Pluggable item tools/props | `NetworkedPluggableObject`, `PluggableObjectPatch`, and `PropHosePatch`. |
| Comms-radio car spawn/delete/rerail/crew modes | Existing comms patches route their authoritative effects through train spawn/delete/rerail/work-train packets. |

The current `LanternPatch` is commented out and therefore provides no coverage.

## Missing-item plans

Each plan is intentionally an additive adapter around the existing generic item snapshots. Shared mechanics are split into reusable helpers; item-specific setters continue to call game functions so visuals, audio, storage, and simulation side effects remain owned by the game.

### 1. Containers and magazines

Affected items include backpacks/toolboxes and every `ItemMagazine` host: boombox cassettes, paint sprayer cans, and soldering-tool spools.

Plan:

1. Extend `ItemState` with `InContainer` and append `ContainerNetId`/`ContainerSlot` to the existing item snapshot.
2. Detect `ItemBase.InContainer`, resolve the top-level item container's `NetworkedItem`, and serialize its ID/slot.
3. On receive, call the existing `ItemContainer.RemoveItem`/`AddItem` methods. Do not manually reparent or rewrite magazine behavior.
4. Retry application when the containing item has not been created yet.

### 2. Lantern and EOT lantern

`Lantern` persists wick size and flame state; `EOTLantern` inherits the same behavior.

Plan:

1. Restore the existing commented patch as live code.
2. Track wick value and `Ignited`.
3. Apply wick through `UpdateWickRelatedLogic` and flame state through `Ignite`/`OnFlameExtinguished`.

### 3. Books, booklets, reports, maps, and catalogs

All `PageBook` items persist a current page but the generic item layer does not synchronize it.

Plan:

1. Add one patch on `PageBook`, covering all derived/prefab variants.
2. Track `PageNum` and apply through `ForceCurrentPage` after pages are generated.
3. Keep job identity/effects in the existing job networking code.

### 4. Labelable items

This covers labeled cassettes and any label-enabled item/gadget.

Plan:

1. Track `LabelableItem.Text` as a string.
2. Apply with `UpdateText` so meshes/localization refresh normally.

### 5. Boombox and cassette

Mutable boombox state includes power, radio/cassette mode, volume, antenna, radio station, door/playback state, and inserted cassette. Cassettes also retain their last playlist entry.

Plan:

1. Let container sync handle insertion/removal through the boombox's existing `ItemMagazine`.
2. Track boombox power, mode, volume, antenna, station index, door state, playback state, and cassette track index.
3. Apply through `SetPower`, `SetMode`, `SetVolume`, `SetAntenna`, `OverrideLastPlayedStationIndex`, and existing cassette controls.
4. Track `Cassette.lastPlayedPlaylistEntry` for a cassette outside a boombox and reconcile it with the boombox controller after insertion.

### 6. Analog alarm clock

Plan:

1. Track alarm minutes/offset and armed state.
2. Add a small public-patch helper that assigns the fields and calls the existing handle/time update functions; do not duplicate alarm calculations.

### 7. Brick handheld console

Plan:

1. Track power state and apply through the existing `SetPower` function.
2. Replicate discrete button actions by tracking a monotonically increasing action sequence and last action, rather than synchronizing the ROM's frame-by-frame state.
3. Let each peer execute `ExecuteInputAction`; deterministic built-in ROMs then advance locally.

### 8. Comms radio local presentation

The radio's authoritative world effects are already packetized, but its selected mode is item-local and should travel with the item.

Plan:

1. Track `activeModeIndex`.
2. Add a small setter helper that selects the existing indexed mode and uses `SetMode`; do not duplicate mode implementations.

### 9. Locomotive remote local state

The remote's train control effects already flow through train synchronization, but power, battery, pairing, and selected-coupler presentation are item-local.

Plan:

1. Track power, battery charge, paired locomotive GUID, and selected coupler.
2. Resolve pairing through `TrainCarRegistry` and call the existing `Pair`/`Unpair` methods.
3. Apply battery deltas through `Battery.Charge`/`Drain` and power through `TogglePower`.

### 10. Consumable ammo/resources

`MagazineAmmo.isSpent` applies to paint cans, soldering resources, and cassettes; soldering tools and duct tape also persist remaining units/uses.

Plan:

1. Add a base `MagazineAmmo` adapter for `isSpent`.
2. Track soldering-tool `remainingUnits` and duct-tape `usesLeft`.
3. Apply through their existing refresh/update helpers where available; container sync handles loaded resources.

### 11. Proximity sensor

Plan:

1. Track its channel and range rotary values.
2. Apply with the existing `ControlImplBase.SetValue` path so `ChannelChanged`, `RangeChanged`, and `ProximitySensorNetwork` continue to own derived state.

### 12. Mounted gadgets

A `GadgetItem` becomes an installed `GadgetBase` linked to a `Customization`; its item is retained in installed-gadget storage. Placement, removal, wiring, and component settings are currently absent from item snapshots.

Plan:

1. Track placement as one atomic string containing the existing customization identification key plus local position/rotation.
2. Add an `InstalledGadget` item state so the installing player retains ownership/authority while the source item represents its linked gadget.
3. Apply placement with `Customization.TryGetFromIdentificationKey` and `GadgetItem.Place`; apply removal with `GadgetBase.Remove`.
4. Track the existing `GadgetBase.SaveDataRequested` JSON as a compact string and apply with `SaveDataLoaded`/`AfterSaveDataLoaded`, reusing every gadget implementation's serializer rather than adding a patch per gadget class.
5. Add a comparison cache so gadget JSON is only rebuilt at a modest interval, not for every item on every network tick.
6. Send placement/configuration through the existing item-update path so the host relays the accepted state. The manager's ownership-validation code is currently globally bypassed by a pre-existing early return; enabling that policy is a separate hardening task.

## Interactions that need no additional item state

| Interaction | Reason |
| --- | --- |
| Compass, map marker, shop scanner, manual oiler, drill, wiring tool, gadget remover, generic gadget spawner | Tool itself has no durable state beyond state listed above; its target/effect is authoritative elsewhere or transient/local presentation. |
| Money use, key use, ignitable use, job validation | The durable result is item destruction or a cash/job/fire/world-system change, not hidden state on the held item. |
| Paint application | Train paint theme events are already synchronized. Loaded-can membership and spent state are covered by container/ammo plans. |
| Radio hover/prompts, flashlight flicker, particles, sounds, highlighting, scrolling previews | Derived presentation can run locally from synchronized durable state. |

## Implementation order and verification

1. Make tracked registration composable/idempotent and retain pending snapshots until all `Start` patches have registered.
2. Implement container membership because boombox, paint, and soldering depend on it.
3. Add small state adapters (lantern, pages, labels, clock, boombox/cassette, Brick, radio, consumables).
4. Add gadget placement/configuration using existing save/load methods.
5. Build `Multiplayer/Multiplayer.csproj`, inspect Harmony targets against the decompiled signatures, and test item wire-format round trips where feasible.

Runtime acceptance checks in-game should cover host-to-client and client-to-host actions, late join, drop/pickup transfer, moving an item into/out of a container, and reconnect/full-sync for every stateful category.

## Implementation result

All plans above are implemented. The implementation extends the existing item-update protocol and tracked-value mechanism rather than introducing a parallel synchronization system:

- tracked-value registration now accepts independent item patches and retains values received before those patches initialize;
- item snapshots carry container identity/slot and installed-gadget state, with bounded retries for late-created dependencies;
- additive Harmony adapters cover lanterns, books, labels, alarms, boomboxes/cassettes, locomotive remotes, proximity sensors, comms radios, Brick consoles, ammunition/resources, and gadget tools;
- mounted gadgets reuse `GadgetItem.Place`, `GadgetBase.Remove`, and the existing gadget save/load callbacks for placement and configuration;
- boombox playback and remote/gadget/container references tolerate snapshot ordering during late join.

Verification completed:

- `Multiplayer/Multiplayer.csproj` builds with 0 errors;
- all 15 added/restored Harmony item patch classes resolve their target types and methods against the supplied game assemblies;
- `git diff --check` passes.

The remaining acceptance work requires two running game clients and is intentionally retained as the runtime matrix above. The build still reports existing environment/project warnings (`xmldoc2md`/missing docs directory, pre-existing C# warnings, and the post-build copy collision). The aggregate solution also has the pre-existing `MultiplayerAPI Tests` reference failure; neither is introduced by item synchronization.
