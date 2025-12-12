using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MachineRepair.Tests
{
    public class SwitchComponentTests
    {
        private readonly System.Collections.Generic.List<GameObject> createdObjects = new();

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
        public void Switch_RespectsConfiguredDefaultState()
        {
            var (component, switchComponent) = CreateSwitch();
            switchComponent.DefaultClosedState = false;
            switchComponent.ApplyConfiguredState();

            Assert.IsFalse(switchComponent.IsClosed, "Switch should honor serialized default state.");
        }

        [Test]
        public void Switch_ToggleBlocksAndRestoresConnectivity()
        {
            var (component, switchComponent) = CreateSwitch();
            var ports = component.portDef.ports;

            switchComponent.ApplyConfiguredState();
            Assert.IsTrue(switchComponent.AllowsConnection(ports[0], 0, ports[1], 1));

            switchComponent.SetState(false);
            Assert.IsFalse(switchComponent.AllowsConnection(ports[0], 0, ports[1], 1));

            switchComponent.Toggle();
            Assert.IsTrue(switchComponent.AllowsConnection(ports[0], 0, ports[1], 1));
        }

        private (MachineComponent component, SwitchComponent switchComponent) CreateSwitch()
        {
            var go = new GameObject("SwitchTest");
            createdObjects.Add(go);

            var machineComponent = go.AddComponent<MachineComponent>();
            machineComponent.portDef = CreatePortDef();

            var switchComponent = go.AddComponent<SwitchComponent>();
            return (machineComponent, switchComponent);
        }

        private static PortDef CreatePortDef()
        {
            var portDef = ScriptableObject.CreateInstance<PortDef>();
            portDef.ports = new[]
            {
                new PortLocal { cell = Vector2Int.zero, port = PortType.Power, isInput = true },
                new PortLocal { cell = new Vector2Int(1, 0), port = PortType.Power, isInput = false }
            };

            return portDef;
        }
    }
}

