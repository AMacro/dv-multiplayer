using DV.Customization;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Customization;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public static class CustomizationRef
{
    public static bool TryCreate(Customization customization, out CustomizationRefData reference)
    {
        reference = default;
        if (customization == null)
            return false;

        if (customization is TrainCarCustomization trainCustomization)
        {
            if (!NetworkedTrainCar.TryGetNetId(trainCustomization.TrainCar, out ushort trainCarNetId))
                return false;

            reference.Kind = CustomizationTargetKind.TrainCar;
            reference.TrainCarNetId = trainCarNetId;
            return true;
        }

        reference.Kind = customization switch
        {
            WorldCustomization => CustomizationTargetKind.World,
            StorageShedCustomization => CustomizationTargetKind.Storage,
            PlayerHouseCustomization => CustomizationTargetKind.PlayerHouse,
            PaintStationCustomization => CustomizationTargetKind.PaintStation,
            _ => reference.Kind,
        };

        return customization is WorldCustomization or StorageShedCustomization or
            PlayerHouseCustomization or PaintStationCustomization;
    }

    public static bool TryResolve(CustomizationRefData reference, out Customization customization)
    {
        customization = null;

        if (reference.Kind == CustomizationTargetKind.TrainCar)
        {
            if (!NetworkedTrainCar.TryGet(reference.TrainCarNetId, out TrainCar trainCar))
                return false;

            customization = trainCar.GetComponent<TrainCarCustomization>();
            return customization != null;
        }

        customization = Resources.FindObjectsOfTypeAll<Customization>()
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.scene.IsValid() && Matches(reference.Kind, candidate));
        return customization != null;
    }

    private static bool Matches(CustomizationTargetKind kind, Customization customization)
    {
        return kind switch
        {
            CustomizationTargetKind.World => customization is WorldCustomization,
            CustomizationTargetKind.Storage => customization is StorageShedCustomization,
            CustomizationTargetKind.PlayerHouse => customization is PlayerHouseCustomization,
            CustomizationTargetKind.PaintStation => customization is PaintStationCustomization,
            _ => false,
        };
    }
}
