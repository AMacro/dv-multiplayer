using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data.Items;

public class CustomizationHoleData
{
    public Vector3 Position;
    public Vector3 Rotation;

    public static void Serialize(NetDataWriter writer, CustomizationHoleData data)
    {
        Vector3Serializer.Serialize(writer, data.Position);
        Vector3Serializer.Serialize(writer, data.Rotation);
    }

    public static CustomizationHoleData Deserialize(NetDataReader reader)
    {
        return new CustomizationHoleData
        {
            Position = Vector3Serializer.Deserialize(reader),
            Rotation = Vector3Serializer.Deserialize(reader),
        };
    }

    public static CustomizationHoleData[] FromHoles(IEnumerable<Collider> holes)
    {
        List<CustomizationHoleData> holeDataList = [];

        if (holes == null)
            return holeDataList.ToArray();

        foreach (Collider hole in holes)
        {
            if (hole == null)
                continue;

            holeDataList.Add(FromHole(hole));
        }

        return holeDataList.ToArray();
    }

    public static CustomizationHoleData FromHole(Collider hole)
    {
        return new CustomizationHoleData
        {
            Position = hole.transform.localPosition,
            Rotation = hole.transform.localRotation * Vector3.forward
        };
    }
}
