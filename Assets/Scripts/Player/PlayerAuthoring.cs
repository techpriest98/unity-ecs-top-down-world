using Game.World.Interaction;
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

        [Header("Visual")]
        [SerializeField]
        private GameObject visual;

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
                    new PlayerBuildInput
                    {
                        PointerScreenPosition = float2.zero,
                        IsBuildMode = false,
                        RemovePressed = false,
                        PlacePressed = false
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

                AddComponent(
                    entity,
                    new SelectedProjectedCell
                    {
                        ChunkCoordinate = int2.zero,
                        ProjectionPosition = 0,
                        BlockData = 0,
                        SourceAirIndex = 0,
                        FaceType = default,
                        IsValid = false
                    });

                if (authoring.visual == null)
                {
                    return;
                }
                
                Entity visualEntity = GetEntity(
                    authoring.visual,
                    TransformUsageFlags.Renderable);

                AddComponent(entity, new PlayerVisualEntity
                {
                    Entity = visualEntity
                });

                AddComponent(entity, new PlayerFacing
                {
                    Value = PlayerFacingDirection.NegativeZ
                });

                AddComponent(entity, new PlayerAnimationData
                {
                    State = PlayerAnimationState.Idle,
                    Frame = 0,
                    ElapsedTime = 0f
                });

                AddComponent(entity, new PlayerLightColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}