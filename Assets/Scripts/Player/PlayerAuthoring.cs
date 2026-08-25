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

        [SerializeField]
        [Min(0f)]
        private float jumpSpeed = 9f;

        [Header("Collision")]
        [SerializeField]
        [Min(0.01f)]
        private float collisionWidth = 0.6f;

        [SerializeField]
        [Min(0.01f)]
        private float collisionHeight = 2f;

        private sealed class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(
                PlayerAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                float3 initialPosition = authoring.transform.position;
                float halfWidth = authoring.collisionWidth * 0.5f;

                AddComponent<PlayerTag>(entity);

                AddComponent(
                    entity,
                    new PlayerMoveInput
                    {
                        Value = float2.zero
                    });

                AddComponent(
                    entity,
                    new PlayerJumpInput
                    {
                        IsPressed = false
                    });

                AddComponent(
                    entity,
                    new PlayerMoveSpeed
                    {
                        Value = authoring.moveSpeed
                    });

                AddComponent(
                    entity,
                    new PlayerJumpSpeed
                    {
                        Value = authoring.jumpSpeed
                    });

                AddComponent(
                    entity,
                    new PlayerVerticalVelocity
                    {
                        Value = 0f
                    });

                AddComponent(
                    entity,
                    new PlayerWorldPosition
                    {
                        Value = initialPosition
                    });

                AddComponent(
                    entity,
                    new PlayerCollisionShape
                    {
                        HalfExtentsXZ = new float2(halfWidth, halfWidth),
                        Height = authoring.collisionHeight
                    });
            }
        }
    }
}