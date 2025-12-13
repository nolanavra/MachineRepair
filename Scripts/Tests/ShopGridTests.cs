using System.Collections.Generic;
using System.Reflection;
using MachineRepair.Grid;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MachineRepair.Tests
{
    public class ShopGridTests
    {
        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                {
                    Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void SwitchComponentsAreAttachedDuringTypeBinding()
        {
            var componentObject = new GameObject("SwitchComponentHost");
            createdObjects.Add(componentObject);
            componentObject.AddComponent<MachineComponent>();

            var switchDef = ScriptableObject.CreateInstance<ThingDef>();
            switchDef.displayName = "Switch";
            switchDef.type = ComponentType.Switch;
            switchDef.footprint = new FootprintMask
            {
                width = 1,
                height = 1,
                origin = Vector2Int.zero,
                occupied = new[] { true },
                display = new[] { false }
            };
            createdObjects.Add(switchDef);

            MethodInfo attachMethod = typeof(GridManager).GetMethod(
                "AttachTypeComponents",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ThingDef), typeof(GameObject) },
                modifiers: null);

            Assert.NotNull(attachMethod, "AttachTypeComponents should be available for tests.");

            attachMethod.Invoke(null, new object[] { switchDef, componentObject });

            Assert.IsNotNull(
                componentObject.GetComponent<SwitchComponent>(),
                "Switch components should instantiate with a SwitchComponent attached.");
        }
    }
}
