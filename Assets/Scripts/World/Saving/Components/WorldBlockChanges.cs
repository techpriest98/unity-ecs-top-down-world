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
        public bool HasContents;
        public CellContents Contents;
    }
    
    public struct WorldBlockChanges : IComponentData
    {
        public NativeParallelMultiHashMap<int2, ChangedBlock> Values;
        public bool IsReady;

        public void Record(int2 coordinate, int index, BlockData value)
        {
            if (SplitCellUtility.IsSplit(value))
                throw new System.ArgumentException("Save cell contents, not a chunk-local reference.");
            var updated = new ChangedBlock { Index = index, Value = value };
            RecordChange(coordinate, updated);
        }

        public void Record(int2 coordinate, int index, CellContents contents)
        {
            contents = new CellContents(contents.Lower, contents.Upper);
            if (contents.IsUniform)
            {
                Record(coordinate, index, new BlockData(contents.Lower.Id, contents.Lower.Durability));
                return;
            }
            RecordChange(coordinate, new ChangedBlock { Index = index, HasContents = true, Contents = contents });
        }

        private void RecordChange(int2 coordinate, ChangedBlock updated)
        {
            if (Values.TryGetFirstValue(coordinate, out ChangedBlock existing, out var iterator))
            {
                do
                {
                    if (existing.Index != updated.Index) continue;
                    Values.SetValue(updated, iterator);
                    return;
                }
                while (Values.TryGetNextValue(out existing, ref iterator));
            }

            if (Values.Count() >= Values.Capacity)
                Values.Capacity = math.max(Values.Capacity * 2, 64);
            Values.Add(coordinate, updated);
        }

        public void Apply(int2 coordinate, DynamicBuffer<BlockData> blocks, DynamicBuffer<SplitCellData> splits)
        {
            if (!Values.TryGetFirstValue(coordinate, out ChangedBlock change, out var iterator)) return;
            do
            {
                if (change.HasContents) SplitCellUtility.Set(blocks, splits, change.Index, change.Contents);
                else SplitCellUtility.SetLegacy(blocks, splits, change.Index, change.Value);
            }
            while (Values.TryGetNextValue(out change, ref iterator));
        }
    }
}
