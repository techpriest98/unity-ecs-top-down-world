using Game.World.Interaction;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        private float moveSpeed = 5f;

        [SerializeField, Min(0f)]
        private float jumpSpeed = 9f;

        [Header("Collision")]
        [SerializeField, Min(0.01f)]
        private float collisionWidth = 0.6f;

        [SerializeField, Min(0.01f)]
        private float collisionHeight = 2f;

        [Header("Visual")]
        [SerializeField]
        private PlayerVisualAuthoring[] visualParts;

        private sealed class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                if (authoring.visualParts != null)
                {
                    for (int i = 0; i < authoring.visualParts.Length; i++)
                    {
                        PlayerVisualAuthoring part = authoring.visualParts[i];

                        if (part == null)
                        {
                            Debug.LogError(
                                $"Visual Parts: element {i} is not assigned.",
                                authoring);
                            return;
                        }

                        DependsOn(part);

                        if (part.transform == authoring.transform ||
                            !part.transform.IsChildOf(authoring.transform))
                        {
                            Debug.LogError(
                                $"{part.name} must be a child of the player.",
                                authoring);
                            return;
                        }

                        for (int j = 0; j < i; j++)
                        {
                            if (authoring.visualParts[j].Part == part.Part)
                            {
                                Debug.LogError(
                                    $"Duplicate player visual part: {part.Part}.",
                                    authoring);
                                return;
                            }
                        }
                    }
                }

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                float3 initialPosition = authoring.transform.position;
                float halfWidth = authoring.collisionWidth * 0.5f;

                AddComponent<PlayerTag>(entity);

                AddComponent(entity, new PlayerMoveInput
                {
                    Value = float2.zero
                });

                AddComponent(entity, new PlayerBuildInput
                {
                    PointerScreenPosition = float2.zero,
                    IsBuildMode = false,
                    RemovePressed = false,
                    PlacePressed = false
                });

                AddComponent(entity, new PlayerJumpInput
                {
                    IsPressed = false
                });

                AddComponent(entity, new PlayerMoveSpeed
                {
                    Value = authoring.moveSpeed
                });

                AddComponent(entity, new PlayerJumpSpeed
                {
                    Value = authoring.jumpSpeed
                });

                AddComponent(entity, new PlayerVerticalVelocity
                {
                    Value = 0f
                });

                AddComponent(entity, new PlayerWorldPosition
                {
                    Value = initialPosition
                });

                AddComponent(entity, new PlayerCollisionShape
                {
                    HalfExtentsXZ = new float2(halfWidth, halfWidth),
                    Height = authoring.collisionHeight
                });

                AddComponent(entity, new SelectedProjectedCell
                {
                    ChunkCoordinate = int2.zero,
                    ProjectionPosition = 0,
                    BlockData = 0,
                    SourceAirIndex = 0,
                    FaceType = default,
                    IsValid = false
                });

                AddComponent(entity, new PlayerFacing
                {
                    Value = PlayerFacingDirection.PositiveZ
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

                DynamicBuffer<PlayerVisualPart> parts =
                    AddBuffer<PlayerVisualPart>(entity);

                if (authoring.visualParts == null)
                    return;

                foreach (PlayerVisualAuthoring part in authoring.visualParts)
                {
                    parts.Add(new PlayerVisualPart
                    {
                        Part = part.Part,
                        VisualEntity = GetEntity(
                            part.gameObject,
                            TransformUsageFlags.Dynamic)
                    });
                }
            }
        }
    }
}