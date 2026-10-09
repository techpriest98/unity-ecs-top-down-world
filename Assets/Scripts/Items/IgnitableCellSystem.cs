using Game.Player;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    // Transient effect in the air cell above a supporting Top face, not terrain data.
    [InternalBufferCapacity(0)]
    public struct IgnitableCell : IBufferElementData
    {
        public int3 Position;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerEquipmentDurabilitySystem))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateBefore(typeof(PlayerSkillInputSystem))]
    public partial class IgnitableCellSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<ItemLibrary>();

        protected override void OnUpdate()
        {
            var entity = SystemAPI.GetSingletonEntity<ItemLibrary>();
            var library = EntityManager.GetComponentData<ItemLibrary>(entity);
            if (!EntityManager.HasBuffer<IgnitableCell>(entity)) EntityManager.AddBuffer<IgnitableCell>(entity);
            var cells = EntityManager.GetBuffer<IgnitableCell>(entity);
            cells.Clear();
            if (!library.Value.IsCreated) return;
            foreach (var item in SystemAPI.Query<RefRO<WorldItem>>())
            {
                var value = item.ValueRO;
                if (!value.Grounded || !IsSource(ref library.Value.Value, value.Instance)) continue;
                var surface = value.Position - new float3(0, value.CollisionRadius, 0);
                AddCell(cells, new int3((int)math.floor(surface.x), (int)math.round(surface.y), (int)math.floor(surface.z)));
            }
            foreach (var (position, slots) in SystemAPI.Query<RefRO<PlayerWorldPosition>, DynamicBuffer<PlayerEquipmentSlot>>()
                         .WithAll<PlayerTag>())
            {
                bool source = false;
                foreach (var slot in slots)
                    if (IsSource(ref library.Value.Value, slot.Instance)) { source = true; break; }
                if (source) AddCell(cells, (int3)math.floor(position.ValueRO.Value + new float3(0, 0.01f, 0)));
            }
        }

        private static bool IsSource(ref ItemLibraryBlob library, ItemInstance instance)
        {
            if (instance.DefinitionId == 0 || instance.Durability <= 0) return false;
            int index = ItemRuntimeUtility.Find(ref library, instance.DefinitionId);
            return index >= 0 && library.Items[index].IgnitionSource;
        }

        private static void AddCell(DynamicBuffer<IgnitableCell> cells, int3 position)
        {
            foreach (var cell in cells) if (math.all(cell.Position == position)) return;
            cells.Add(new IgnitableCell { Position = position });
        }
    }
}
