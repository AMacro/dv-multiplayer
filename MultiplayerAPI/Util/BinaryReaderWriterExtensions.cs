using System.IO;
using UnityEngine;

namespace MPAPI.Util;

/// <summary>
/// Provides extension methods for <see cref="BinaryWriter"/> and <see cref="BinaryReader"/> to handle arrays and Unity types.
/// </summary>
public static class BinaryReaderWriterExtensions
{
    /// <summary>
    /// Serialises a <see cref="ushort"/> array.
    /// </summary>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write to.</param>
    /// <param name="array">The <see cref="ushort"/> array to write. If null, writes 0 as the length.</param>
    public static void WriteUShortArray(this BinaryWriter writer, ushort[] array)
    {
        if (array == null)
        {
            writer.Write(0);
            return;
        }
        writer.Write(array.Length);
        foreach (ushort value in array)
        {
            writer.Write(value);
        }
    }

    /// <summary>
    /// Serialises an <see cref="int"/> array.
    /// </summary>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write to.</param>
    /// <param name="array">The <see cref="int"/> array to serialise. If null, serialises 0 as the length.</param>
    public static void WriteInt32Array(this BinaryWriter writer, int[] array)
    {
        if (array == null)
        {
            writer.Write(0);
            return;
        }
        writer.Write(array.Length);
        foreach (int value in array)
        {
            writer.Write(value);
        }
    }

    /// <summary>
    /// Serialises a <see cref="UnityEngine.Vector3"/>.
    /// </summary>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write to.</param>
    /// <param name="vector">The <see cref="UnityEngine.Vector3"/> to serialise.</param>
    public static void WriteVector3(this BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.x);
        writer.Write(vector.y);
        writer.Write(vector.z);
    }

    /// <summary>
    /// Serialises a <see cref="UnityEngine.Quaternion"/>.
    /// </summary>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write to.</param>
    /// <param name="quaternion">The <see cref="UnityEngine.Quaternion"/> to serialise.</param>
    public static void WriteQuaternion(this BinaryWriter writer, Quaternion quaternion)
    {
        writer.Write(quaternion.w);
        writer.Write(quaternion.x);
        writer.Write(quaternion.y);
        writer.Write(quaternion.z);
    }

    /// <summary>
    /// Deserialises a <see cref="ushort"/> array.
    /// </summary>
    /// <param name="reader">The <see cref="BinaryReader"/> to deserialise from.</param>
    /// <returns>The deserialised <see cref="ushort"/> array.</returns>
    public static ushort[] ReadUShortArray(this BinaryReader reader)
    {
        var length = reader.ReadInt32();

        var ret = new ushort[length];
        for (int i = 0; i < length; i++)
        {
            ret[i] = reader.ReadUInt16();
        }
        return ret;
    }

    /// <summary>
    /// Deserialises an <see cref="int"/> array.
    /// </summary>
    /// <param name="reader">The <see cref="BinaryReader"/> to deserialise from.</param>
    /// <returns>The deserialised <see cref="int"/> array.</returns>
    public static int[] ReadInt32Array(this BinaryReader reader)
    {
        var length = reader.ReadInt32();

        var ret = new int[length];
        for (int i = 0; i < length; i++)
        {
            ret[i] = reader.ReadInt32();
        }
        return ret;
    }

    /// <summary>
    /// Deserialises a <see cref="Vector3"/>.
    /// </summary>
    /// <param name="reader">The <see cref="BinaryReader"/> to deserialise from.</param>
    /// <returns>The deserialised <see cref="Vector3"/>.</returns>
    public static Vector3 ReadVector3(this BinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Deserialises a <see cref="Quaternion"/>.
    /// </summary>
    /// <param name="reader">The <see cref="BinaryReader"/> to deserialise from.</param>
    /// <returns>The deserialised <see cref="Quaternion"/>.</returns>

    public static Quaternion ReadQuaternion(this BinaryReader reader)
    {
        float w = reader.ReadSingle();
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();

        return new Quaternion(x, y, z, w);
    }
}
