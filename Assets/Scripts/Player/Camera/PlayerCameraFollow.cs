using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        [Header("Mouse wheel zoom")]
        [SerializeField, Min(.1f)] private float minimumSize = 2f;
        [SerializeField, Min(.1f)] private float maximumSize = 10f;
        [SerializeField, Min(.05f)] private float zoomStep = .5f;
        [SerializeField, Min(.1f)] private float zoomResponse = 14f;
        private Camera cameraComponent;
        private float targetSize;

        private EcsWorld ecsWorld;
        private EntityManager entityManager;

        private EntityQuery playerQuery;
        private EntityQuery viewDirectionQuery;

        private bool queriesInitialized;

        private void Awake()
        {
            cameraComponent =
                GetComponent<Camera>();

            cameraComponent.orthographic =
                true;
            ValidateZoom();
            targetSize = Mathf.Clamp(cameraComponent.orthographicSize, minimumSize, maximumSize);
            cameraComponent.orthographicSize = targetSize;

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
            UpdateZoom();

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

        private void OnValidate() => ValidateZoom();

        private void ValidateZoom()
        {
            minimumSize = Mathf.Max(.1f, minimumSize);
            maximumSize = Mathf.Max(minimumSize, maximumSize);
            zoomStep = Mathf.Max(.05f, zoomStep);
            zoomResponse = Mathf.Max(.1f, zoomResponse);
        }

        private void UpdateZoom()
        {
            var simulation = ecsWorld.GetExistingSystemManaged<SimulationSystemGroup>();
            if (simulation == null || !simulation.Enabled || Time.timeScale <= 0f ||
                Game.UI.EquipmentHud.IsBlockPickerOpen) return;

            var mouse = Mouse.current;
            if (mouse != null && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f)
                    targetSize = Mathf.Clamp(targetSize - Mathf.Sign(scroll) * zoomStep, minimumSize, maximumSize);
            }
            cameraComponent.orthographicSize = Mathf.Lerp(cameraComponent.orthographicSize,
                targetSize, 1f - Mathf.Exp(-zoomResponse * Time.unscaledDeltaTime));
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
