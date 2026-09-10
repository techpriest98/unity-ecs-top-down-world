using Game.World.Blocks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Saving
{
    public struct ChangedBlock
    {
        public int Index;
        public BlockData Value;
    }
    
    public struct WorldBlockChanges : IComponentData
    {
        public NativeParallelMultiHashMap<int2, ChangedBlock> Values;
        public bool IsReady;

        public void Record(int2 coordinate, int index, BlockData value)
        {
            var updated = new ChangedBlock { Index = index, Value = value };
            if (Values.TryGetFirstValue(coordinate, out ChangedBlock existing, out var iterator))
            {
                do
                {
                    if (existing.Index != index) continue;
                    Values.SetValue(updated, iterator);
                    return;
                }
                while (Values.TryGetNextValue(out existing, ref iterator));
            }

            if (Values.Count() >= Values.Capacity)
                Values.Capacity = math.max(Values.Capacity * 2, 64);
            Values.Add(coordinate, updated);
        }

        public void Apply(int2 coordinate, DynamicBuffer<BlockData> blocks)
        {
            if (!Values.TryGetFirstValue(coordinate, out ChangedBlock change, out var iterator)) return;
            do
            {
                blocks[change.Index] = change.Value;
            }
            while (Values.TryGetNextValue(out change, ref iterator));
        }
    }
}
