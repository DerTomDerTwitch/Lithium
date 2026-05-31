using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Behaviours
{
    public class PotBaseValues : MonoBehaviour
    {
        public float BaseWaterDrainPerHour;
        public float BaseGrowSpeedMultiplier;

        public void Init(Pot pot)
        {
            // The water system was reworked into "moisture": Pot.WaterDrainPerHour is now
            // GrowContainer._moistureDrainPerHour (Pot derives from GrowContainer).
            BaseWaterDrainPerHour = pot._moistureDrainPerHour;
            BaseGrowSpeedMultiplier = pot.GrowSpeedMultiplier;
        }
    }
}
