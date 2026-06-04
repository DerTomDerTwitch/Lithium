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
            BaseWaterDrainPerHour = pot._moistureDrainPerHour;
            BaseGrowSpeedMultiplier = pot.GrowSpeedMultiplier;
        }
    }
}
