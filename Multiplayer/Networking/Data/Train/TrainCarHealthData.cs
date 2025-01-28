using DV.Damage;
using LiteNetLib.Utils;
using System;

namespace Multiplayer.Networking.Data.Train;

public readonly struct TrainCarHealthData
{
    public readonly float BodyHP;
    public readonly float WheelsHP;
    public readonly float MechanicalPT;
    public readonly float ElectricalPT;
    public readonly bool WindowsBroken;

    private TrainCarHealthData(float bodyHP, float wheelsHP, float mechanicalPT, float electricalPT, bool windowsBroken)
    {
        BodyHP = bodyHP;
        WheelsHP = wheelsHP;
        MechanicalPT = mechanicalPT;
        ElectricalPT = electricalPT;
        WindowsBroken = windowsBroken;
    }
    public void LoadTo(TrainCar trainCar)
    {
        var dmgCtrl = trainCar.GetComponent<DamageController>();
        if (dmgCtrl != null)
        {
            dmgCtrl.bodyDamage.LoadCarDamageState(BodyHP);
            dmgCtrl.wheels?.SetCurrentHealthPercentage(WheelsHP);
            dmgCtrl.mechanicalPT?.SetCurrentHealthPercentage(MechanicalPT);
            dmgCtrl.electricalPT?.SetCurrentHealthPercentage(ElectricalPT);

            if (dmgCtrl.windows != null)
                dmgCtrl.windows.windowsBroken = WindowsBroken;
        }
    }

    public static TrainCarHealthData From(TrainCar car)
    {
        var dmgCtrl = car.GetComponent<DamageController>();

        if (dmgCtrl == null )
            return new TrainCarHealthData();

        float bodyHP = dmgCtrl?.bodyDamage?.HealthPercentage ?? 0;
        float wheelsHP = dmgCtrl?.wheels?.HealthPercentage ?? 0;
        float mechanicalPT = dmgCtrl?.mechanicalPT?.HealthPercentage ?? 0;
        float electricalPT = dmgCtrl?.electricalPT?.HealthPercentage ?? 0;
        bool brokenWindows = dmgCtrl?.windows?.windowsBroken ?? true;

        return new TrainCarHealthData(bodyHP, wheelsHP, mechanicalPT, electricalPT, brokenWindows);
    }

    public static void Serialize(NetDataWriter writer, TrainCarHealthData data)
    {
        writer.Put(data.BodyHP);
        writer.Put(data.WheelsHP);
        writer.Put(data.MechanicalPT);
        writer.Put(data.ElectricalPT);
        writer.Put(data.WindowsBroken);
    }

    public static TrainCarHealthData Deserialize(NetDataReader reader)
    {
        float bodyHP = reader.GetFloat();
        float wheelsHP = reader.GetFloat();
        float mechanicalPT = reader.GetFloat();
        float electricalPT = reader.GetFloat();
        bool brokenWindows = reader.GetBool();

        return new TrainCarHealthData(bodyHP, wheelsHP, mechanicalPT, electricalPT, brokenWindows);
    }
}
