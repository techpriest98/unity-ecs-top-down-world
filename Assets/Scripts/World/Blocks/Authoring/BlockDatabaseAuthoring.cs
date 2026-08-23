using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Blocks
{
    public sealed class BlockDatabaseAuthoring : MonoBehaviour
    {
        [SerializeField]
        private BlockDatabase blockDatabase;

        private sealed class Baker : Baker<BlockDatabaseAuthoring>
        {
            public override void Bake(BlockDatabaseAuthoring authoring)
            {
                if (authoring.blockDatabase == null)
                {
                    Debug.LogError("Block Database is not assigned.", authoring);
                    return;
                }

                DependsOn(authoring.blockDatabase);

                BlockDefinition[] definitions = authoring.blockDatabase.Blocks;
                int maximumBlockId = 0;

                for (int i = 0; i < definitions.Length; i++)
                    maximumBlockId = math.max(maximumBlockId, (int)definitions[i].BlockId);

                var builder = new BlobBuilder(Allocator.Temp);
                ref BlockDatabaseBlob root = ref builder.ConstructRoot<BlockDatabaseBlob>();

                BlobBuilderArray<byte> durability =
                    builder.Allocate(ref root.MaxDurability, maximumBlockId + 1);

                for (int i = 0; i < durability.Length; i++)
                    durability[i] = 0;

                for (int i = 0; i < definitions.Length; i++)
                {
                    BlockDefinition definition = definitions[i];
                    int blockId = (int)definition.BlockId;

                    if (blockId < 0 || blockId >= durability.Length)
                        continue;

                    durability[blockId] = (byte)math.clamp(
                        definition.maxDurability,
                        byte.MinValue,
                        byte.MaxValue);
                }

                BlobAssetReference<BlockDatabaseBlob> blob =
                    builder.CreateBlobAssetReference<BlockDatabaseBlob>(Allocator.Persistent);

                AddBlobAsset(ref blob, out _);

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new BlockDatabaseComponent
                {
                    Value = blob
                });

                builder.Dispose();
            }
        }
    }
}