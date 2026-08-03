using Game.World.Blocks;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ProjectedCellGpuBuilder
    {
        public static NativeArray<ProjectedCellGpuData> Build(
            DynamicBuffer<ProjectedCellData> projectedCells,
            DynamicBuffer<BlockData> blocks,
            Allocator allocator)
        {
            var result = new NativeArray<ProjectedCellGpuData>(
                projectedCells.Length,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < projectedCells.Length; i++)
            {
                ProjectedCellData projectedCell = projectedCells[i];
                BlockData block = blocks[projectedCell.BlockIndex];

                result[i] = new ProjectedCellGpuData(
                    blockId: block.BlockId,
                    faceType: projectedCell.FaceType,
                    durability: block.Durability,
                    x: projectedCell.X,
                    y: projectedCell.Y);
            }

            return result;
        }
    }
}