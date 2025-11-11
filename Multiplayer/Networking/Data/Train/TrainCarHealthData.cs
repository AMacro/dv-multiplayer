using DV.Damage;
using LiteNetLib.Utils;
using Multiplayer.Utils;
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
        //Multiplayer.LogDebug(() => $"TrainCarHealthData.LoadTo([{trainCar.ID}, {trainCar.GetNetId()}])");
        var dmgCtrl = trainCar.GetComponent<DamageController>();
        if (dmgCtrl != null)
        {
            //Multiplayer.LogDebug(() => $"TrainCarHealthData.LoadTo([{trainCar.ID}, {trainCar.GetNetId()}])\r\n" +
            //$"Damage Controller: Body: {dmgCtrl?.bodyDamage?.HealthPercentage}, " +
            //$"Wheels: {dmgCtrl?.wheels?.HealthPercentage}, " +
            //$"Mechanical: {dmgCtrl?.mechanicalPT?.HealthPercentage}, " +
            //$"Electrical: {dmgCtrl?.electricalPT?.HealthPercentage}, " +
            //$"Windows: {dmgCtrl?.windows?.windowsBroken}");

            dmgCtrl.bodyDamage.LoadCarDamageState(BodyHP);
            dmgCtrl.wheels?.SetCurrentHealthPercentage(WheelsHP);
            dmgCtrl.mechanicalPT?.SetCurrentHealthPercentage(MechanicalPT);
            dmgCtrl.electricalPT?.SetCurrentHealthPercentage(ElectricalPT);

            if (dmgCtrl.windows != null)
                dmgCtrl.windows.windowsBroken = WindowsBroken;

            return;
        }

        var dmgModel = trainCar.GetComponent<CarDamageModel>();
        //Multiplayer.LogDebug(() => $"TrainCarHealthData.LoadTo([{trainCar.ID}, {trainCar.GetNetId()}]) Using CarDamageModel: {dmgModel !=null}");
        dmgModel?.SetHealth(BodyHP);
    }

    public static TrainCarHealthData From(TrainCar trainCar)
    {
        var dmgCtrl = trainCar.GetComponent<DamageController>();

        if (dmgCtrl == null)
        {
            //freight cars don't have damage controller, so we need to check if they have a damage model
            var dmgModel = trainCar.GetComponent<CarDamageModel>();
            if (dmgModel != null)
                return new TrainCarHealthData(dmgModel.currentHealth, 0,0,0,false);
            else
                return new TrainCarHealthData();
        }
        
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
