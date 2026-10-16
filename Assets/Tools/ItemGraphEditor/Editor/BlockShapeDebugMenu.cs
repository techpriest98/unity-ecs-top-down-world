using Game.Player;
using Game.Items.Skills;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// Projection-stage diagnostic only. It does not change generation or the staff skills.
public static class BlockShapeDebugMenu
{
    [InitializeOnLoadMethod]
    private static void ScheduleShapeSkillRepair() => EditorApplication.delayCall += RepairShapeSkill;

    [MenuItem("Tools/World/Repair Change Shape skill")]
    private static void RepairShapeSkill()
    {
        const string actionPath = "Assets/Configs/Items/WorldShaper/Skills/TogglePlacementShape.asset";
        const string nodePath = "Assets/Configs/Items/WorldShaper/Skills/ShapeChainAction.asset";
        // A new script may have been unavailable during the asset's first import.
        // Reimport only this known asset and repair only a missing reference.
        var node = AssetDatabase.LoadAssetAtPath<SkillChainNode>(nodePath);
        if (node == null || node.Action != null) return;
        AssetDatabase.ImportAsset(actionPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var action = AssetDatabase.LoadAssetAtPath<TogglePlacementShapeAction>(actionPath);
        if (action == null)
        {
            Debug.LogError("Change Shape action could not be loaded. Check TogglePlacementShapeAction.cs import errors.");
            return;
        }
        node.Action = action;
        EditorUtility.SetDirty(node);
        AssetDatabase.SaveAssetIfDirty(node);
        Debug.Log("Restored Change Shape action reference. No cursor position is required.");
    }

    [MenuItem("Tools/World/Selected block/Lower half %&#h")]
    private static void LowerHalf() => SetShape(BlockShape.LowerHalf);

    [MenuItem("Tools/World/Selected block/Full %&#f")]
    private static void Full() => SetShape(BlockShape.Full);

    [MenuItem("Tools/World/Selected block/Halves/Stone below - Water above")]
    private static void StoneWater() => SetHalves(BlockId.Stone, BlockId.OceanWater);

    [MenuItem("Tools/World/Selected block/Halves/Sand below - Stone above")]
    private static void SandStone() => SetHalves(BlockId.Sand, BlockId.Stone);

    [MenuItem("Tools/World/Selected block/Halves/Keep lower - Water above %&#w")]
    private static void AddWater() => SetShape(BlockShape.Full, waterAbove: true);

    private static void SetHalves(BlockId lower, BlockId upper) => SetShape(BlockShape.Full,
        new CellContents(new BlockPart(lower, 255), new BlockPart(upper, 255)));

    private static void SetShape(BlockShape shape, CellContents? halves = null, bool waterAbove = false)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (!Application.isPlaying || world == null || !world.IsCreated) return;
        var manager = world.EntityManager;
        using var players = manager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(),
            ComponentType.ReadOnly<Game.World.Interaction.SelectedProjectedCell>());
        using var queues = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>(),
            ComponentType.ReadWrite<BlockModificationRequest>());
        if (players.CalculateEntityCount() != 1 || queues.CalculateEntityCount() != 1) return;
        if (!CursorWorldUtility.TryGetPosition(manager, players.GetSingletonEntity(), CursorWorldPosition.Behind, out var position))
            return;
        int2 coordinate = (int2)math.floor(new float2(position.x, position.z) /
            new float2(ChunkSettings.SizeX, ChunkSettings.SizeZ));
        using var chunks = manager.CreateEntityQuery(ComponentType.ReadOnly<ChunkComponent>(),
            ComponentType.ReadOnly<ChunkGenerated>(), ComponentType.ReadOnly<BlockData>());
        using var entities = chunks.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            if (math.any(manager.GetComponentData<ChunkComponent>(entity).Coordinate != coordinate)) continue;
            if (position.y < 0 || position.y >= ChunkSettings.SizeY) return;
            int index = ChunkUtility.ToIndex(position.x - coordinate.x * ChunkSettings.SizeX, position.y,
                position.z - coordinate.y * ChunkSettings.SizeZ);
            var blocks = manager.GetBuffer<BlockData>(entity, true);
            CellContents current = SplitCellUtility.Get(blocks, manager.GetBuffer<SplitCellData>(entity, true), index);
            BlockModificationRequest request;
            if (waterAbove) halves = new CellContents(current.Lower, new BlockPart(BlockId.OceanWater, 255));
            if (halves.HasValue) request = new BlockModificationRequest(position, halves.Value);
            else
            {
                BlockPart part = current.Lower.Id != BlockId.Air ? current.Lower : current.Upper;
                if (part.Id == BlockId.Air) return;
                request = new BlockModificationRequest(position, new BlockData(part.Id, part.Durability, shape));
            }
            manager.GetBuffer<BlockModificationRequest>(queues.GetSingletonEntity()).Add(request);
            Debug.Log($"Block at {position}: {(halves.HasValue ? "split materials" : shape.ToString())}. Rendering diagnostic; per-half gameplay is not implemented yet.");
            return;
        }
    }
}
