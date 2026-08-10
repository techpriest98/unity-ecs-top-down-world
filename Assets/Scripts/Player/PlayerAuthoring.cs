using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAuthoring :
        MonoBehaviour
    {
        [SerializeField]
        [Min(0f)]
        private float moveSpeed = 5f;

        private sealed class Baker :
            Baker<PlayerAuthoring>
        {
            public override void Bake(
                PlayerAuthoring authoring)
            {
                Entity entity =
                    GetEntity(
                        TransformUsageFlags.Dynamic);

                float3 initialPosition =
                    authoring.transform.position;

                AddComponent<PlayerTag>(
                    entity);

                AddComponent(
                    entity,
                    new PlayerMoveInput
                    {
                        Value =
                            float2.zero
                    });

                AddComponent(
                    entity,
                    new PlayerMoveSpeed
                    {
                        Value =
                            authoring.moveSpeed
                    });

                AddComponent(
                    entity,
                    new PlayerWorldPosition
                    {
                        Value =
                            initialPosition
                    });
            }
        }
    }
}