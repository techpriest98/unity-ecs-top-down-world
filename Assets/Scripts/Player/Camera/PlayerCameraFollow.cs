using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

using EcsWorld = Unity.Entities.World;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraFollow :
        MonoBehaviour
    {
        [SerializeField]
        private Vector2 screenOffset = new Vector2(0f, -2f);

        [SerializeField]
        private float cameraDepth;

        private EcsWorld ecsWorld;
        private EntityManager entityManager;

        private EntityQuery playerQuery;
        private EntityQuery viewDirectionQuery;

        private bool queriesInitialized;

        private void Awake()
        {
            Camera cameraComponent =
                GetComponent<Camera>();

            cameraComponent.orthographic =
                true;

            cameraDepth =
                transform.position.z;
        }

        private void LateUpdate()
        {
            if (!EnsureQueries())
            {
                return;
            }

            if (playerQuery.CalculateEntityCount() != 1 ||
                viewDirectionQuery.CalculateEntityCount() != 1)
            {
                return;
            }

            Entity playerEntity =
                playerQuery.GetSingletonEntity();

            Entity viewDirectionEntity =
                viewDirectionQuery.GetSingletonEntity();

            PlayerWorldPosition playerPosition =
                entityManager.GetComponentData<
                    PlayerWorldPosition>(
                    playerEntity);

            ViewDirectionComponent viewDirection =
                entityManager.GetComponentData<
                    ViewDirectionComponent>(
                    viewDirectionEntity);

            float2 projectedPosition =
                WorldPositionProjectionUtility.Project(
                    playerPosition.Value,
                    viewDirection.Value);

            transform.position =
                new Vector3(
                    projectedPosition.x +
                    screenOffset.x,

                    projectedPosition.y +
                    screenOffset.y,

                    cameraDepth);
        }

        private bool EnsureQueries()
        {
            if (queriesInitialized)
            {
                if (ecsWorld != null &&
                    ecsWorld.IsCreated)
                {
                    return true;
                }

                queriesInitialized =
                    false;
            }

            ecsWorld =
                EcsWorld.DefaultGameObjectInjectionWorld;

            if (ecsWorld == null ||
                !ecsWorld.IsCreated)
            {
                return false;
            }

            entityManager =
                ecsWorld.EntityManager;

            playerQuery =
                entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<
                        PlayerTag>(),

                    ComponentType.ReadOnly<
                        PlayerWorldPosition>());

            viewDirectionQuery =
                entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<
                        ViewDirectionComponent>());

            queriesInitialized =
                true;

            return true;
        }
    }
}