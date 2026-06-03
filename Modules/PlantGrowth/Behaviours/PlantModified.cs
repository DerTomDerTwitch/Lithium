using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Behaviours
{
    // Marker component attached to a plant once its harvest yield has been rolled, so Plant.GrowthDone
    // (see PlantGrowthDonePatch) doesn't roll it again. Carries no state — its presence is the signal.
    public class PlantModified : MonoBehaviour
    {
    }
}
