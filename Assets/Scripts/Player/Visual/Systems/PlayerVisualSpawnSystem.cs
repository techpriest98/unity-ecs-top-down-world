using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Game.Player
{
    // Initialization runs before animation, equipment lighting and transform propagation.
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PlayerVisualSpawnSystem : SystemBase
    {
        private EntityQuery pending;

        protected override void OnCreate()
        {
            pending = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerVisualPrefab>(), ComponentType.ReadWrite<PlayerVisualPart>());
        }

        protected override void OnUpdate()
        {
            using var owners = pending.ToEntityArray(Allocator.Temp);
            foreach (Entity owner in owners)
            {
                // Structural changes invalidate DynamicBuffers: copy the requests first,
                // and reacquire owner buffers after every instantiate/parent change.
                using var requests = EntityManager.GetBuffer<PlayerVisualPrefab>(owner, true).ToNativeArray(Allocator.Temp);
                if (requests.Length == 0)
                {
                    EntityManager.RemoveComponent<PlayerVisualPrefab>(owner);
                    continue;
                }
                if (!EntityManager.HasBuffer<LinkedEntityGroup>(owner))
                {
                    EntityManager.AddBuffer<LinkedEntityGroup>(owner);
                    EntityManager.GetBuffer<LinkedEntityGroup>(owner).Add(new LinkedEntityGroup { Value = owner });
                }
                foreach (var request in requests)
                {
                    Entity visual = EntityManager.Instantiate(request.Prefab);
                    var parent = new Parent { Value = owner };
                    if (EntityManager.HasComponent<Parent>(visual)) EntityManager.SetComponentData(visual, parent);
                    else EntityManager.AddComponentData(visual, parent);

                    var linked = EntityManager.GetBuffer<LinkedEntityGroup>(owner);
                    if (EntityManager.HasBuffer<LinkedEntityGroup>(visual))
                    {
                        var children = EntityManager.GetBuffer<LinkedEntityGroup>(visual, true);
                        foreach (var child in children) linked.Add(child);
                    }
                    else linked.Add(new LinkedEntityGroup { Value = visual });

                    EntityManager.GetBuffer<PlayerVisualPart>(owner).Add(new PlayerVisualPart
                    {
                        Part = request.Part, VisualEntity = visual, AnimationId = AnimationID.Idle,
                        IsVisible = request.Part == CharacterPart.Body || request.Part == CharacterPart.LeftArm ||
                            request.Part == CharacterPart.RightArm
                    });
                }
                // Consuming the request buffer prevents duplicates on subsequent frames.
                EntityManager.RemoveComponent<PlayerVisualPrefab>(owner);
            }
        }
    }
}
