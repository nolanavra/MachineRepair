
using System.Collections.Generic;
using UnityEngine;

namespace Espresso
{
    [CreateAssetMenu(menuName = "Espresso/Flavor/Bank", fileName = "FlavorBank")]
    public class FlavorBank : ScriptableObject
    {
        public List<FlavorLine> lines = new();
    }
}
