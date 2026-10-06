using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerVisualAuthoring : MonoBehaviour
    {
        [SerializeField]
        private AnimationDatabase animationDatabase;

        [SerializeField]
        private AnimationID idleAnimation = AnimationID.Idle;

        [SerializeField]
        private AnimationID walkAnimation = AnimationID.Walk;

        private sealed class Baker : Baker<PlayerVisualAuthoring>
        {
            public override void Bake(PlayerVisualAuthoring authoring)
            {
                AnimationDatabase database = authoring.animationDatabase;

                if (database == null)
                {
                    Debug.LogError("Assign Animation Database.", authoring);
                    return;
                }

                DependsOn(database);

                MeshRenderer renderer = GetComponent<MeshRenderer>();
                MeshFilter filter = GetComponent<MeshFilter>();

                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial == null ||
                    filter == null ||
                    filter.sharedMesh == null)
                {
                    Debug.LogError(
                        "Player visual requires a mesh and one material slot.",
                        authoring);
                    return;
                }

                Material template = renderer.sharedMaterial;
                Mesh mesh = filter.sharedMesh;

                DependsOn(template);
                DependsOn(mesh);

                if (mesh.subMeshCount != 1)
                {
                    Debug.LogError(
                        "Player visual requires a single-submesh mesh.",
                        authoring);
                    return;
                }

                if (!template.HasProperty("_BaseMap") ||
                    !template.HasProperty("_UvScaleOffset") ||
                    !template.HasProperty("_PlayerLightColor"))
                {
                    Debug.LogError(
                        "Assign the PlayerSprite material to MeshRenderer.",
                        authoring);
                    return;
                }

                int capacity = 0;

                foreach (AnimationID id in Enum.GetValues(typeof(AnimationID)))
                {
                    capacity = Math.Max(capacity, (int)id + 1);
                }

                var clips = new PlayerAnimationClip[capacity];

                if (database.Animations == null ||
                    database.Animations.Length == 0)
                {
                    Debug.LogError(
                        "Animation Database is empty.",
                        database);
                    return;
                }

                foreach (AnimationDefinition definition in database.Animations)
                {
                    if (definition == null)
                    {
                        Debug.LogError(
                            "Animation Database contains an empty entry.",
                            database);
                        return;
                    }

                    DependsOn(definition);

                    if (!Enum.IsDefined(typeof(AnimationID), definition.Id))
                    {
                        Debug.LogError(
                            $"Unknown AnimationID: {definition.Id}.",
                            definition);
                        return;
                    }

                    int index = (int)definition.Id;

                    if (clips[index].FrameCount != 0)
                    {
                        Debug.LogError(
                            $"Duplicate AnimationID: {definition.Id}.",
                            database);
                        return;
                    }

                    Texture2D spriteSheet = definition.SpriteSheet;

                    if (spriteSheet == null)
                    {
                        Debug.LogError(
                            $"{definition.name}: assign SpriteSheet.",
                            definition);
                        return;
                    }

                    DependsOn(spriteSheet);

                    if (definition.FrameCount < 1 ||
                        !math.isfinite(definition.FrameDuration) ||
                        definition.FrameDuration < 0.01f)
                    {
                        Debug.LogError(
                            $"{definition.name}: invalid frame count or duration.",
                            definition);
                        return;
                    }

                    if (spriteSheet.width % definition.FrameCount != 0 ||
                        spriteSheet.height % 4 != 0)
                    {
                        Debug.LogError(
                            $"{definition.name}: SpriteSheet must contain " +
                            "FrameCount columns and four equal rows.",
                            definition);
                        return;
                    }

                    clips[index] = new PlayerAnimationClip
                    {
                        SpriteSheet = spriteSheet,
                        FrameCount = definition.FrameCount,
                        FrameDuration = definition.FrameDuration,
                        Loop = definition.Loop
                    };
                }

                int idleIndex = (int)authoring.idleAnimation;
                int walkIndex = (int)authoring.walkAnimation;

                if ((uint)idleIndex >= (uint)clips.Length ||
                    (uint)walkIndex >= (uint)clips.Length ||
                    clips[idleIndex].FrameCount == 0 ||
                    clips[walkIndex].FrameCount == 0)
                {
                    Debug.LogError(
                        "Selected Idle and Walk must exist in Animation Database.",
                        authoring);
                    return;
                }

                Entity entity = GetEntity(TransformUsageFlags.Renderable);

                DynamicBuffer<PlayerAnimationClip> buffer =
                    AddBuffer<PlayerAnimationClip>(entity);

                foreach (PlayerAnimationClip clip in clips)
                {
                    buffer.Add(clip);
                }

                AddComponent(entity, new PlayerAnimationMaterialTemplate
                {
                    Value = template
                });

                AddComponent(entity, new PlayerAnimationSelection
                {
                    IdleId = authoring.idleAnimation,
                    WalkId = authoring.walkAnimation,
                    OverrideEnabled = false,
                    OverrideId = authoring.idleAnimation
                });

                AddComponent(entity, new PlayerAnimationPlayback
                {
                    CurrentId = authoring.idleAnimation,
                    Initialized = false
                });

                AddComponent(entity, new PlayerSpriteUv
                {
                    Value = new float4(
                        1f / clips[idleIndex].FrameCount,
                        0.25f,
                        0f,
                        0.75f)
                });

                AddComponent(entity, new PlayerLightColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}