using Game.Items;
using Game.Items.Skills;
using Game.Player;
using Game.World.Rendering;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.UI
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(WorldItemRenderSystem))]
    public partial class RefuelTargetDurabilitySystem : SystemBase
    {
        private EntityQuery libraryQuery;
        private GameObject canvasObject;
        private TextMeshProUGUI label;
        private int shownCurrent = -1, shownMaximum = -1;
        private static readonly int DepthOrigin = Shader.PropertyToID("_TerrainDepthOrigin");

        protected override void OnCreate()
        {
            libraryQuery = GetEntityQuery(ComponentType.ReadOnly<ItemDefinitionReference>());
            RequireForUpdate(libraryQuery);
            RequireForUpdate<PlayerAvailableSkill>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        // Inspect the Refuel path without actions or another target search.
        // The label may ignore range; the execution graph still enforces it.
        internal static RefuelWorldItemAction FindAvailableRefuel(ItemSkillDefinition skill,
            EntityManager manager, Entity player, float3 position, bool checkDistance = true)
        {
            if (skill == null || !skill.UseChain) return null;
            int count = 0;
            for (var node = skill.FirstNode; node != null && ++count <= 64; node = node.Next)
            {
                if (node.Kind == SkillChainNodeKind.WithinDistance)
                {
                    if (checkDistance && (node.Distance == null || !math.isfinite(node.Distance.MaxDistance) || node.Distance.MaxDistance < 0 ||
                        !node.Distance.Allows(manager, player, position))) return null;
                }
                else if (node.Kind == SkillChainNodeKind.Action) return node.Action as RefuelWorldItemAction;
                else if (node.Kind != SkillChainNodeKind.VisibilityData) return null;
            }
            return null;
        }

        internal static Vector3 LabelAnchor(float3 surface, ViewDirection direction, float spriteHeight, float depthOrigin)
        {
            float2 projected = WorldPositionProjectionUtility.Project(surface, direction);
            float baseY = projected.y + .25f;
            return new Vector3(projected.x, baseY + spriteHeight + .12f,
                2f * (baseY - surface.y - 9f) - depthOrigin - .002f);
        }

        protected override void OnUpdate()
        {
            var camera = Camera.main;
            if (UnityEngine.Time.timeScale <= 0f || camera == null || libraryQuery.CalculateEntityCount() != 1) { Hide(); return; }
            Entity libraryEntity = libraryQuery.GetSingletonEntity();
            var references = EntityManager.GetBuffer<ItemDefinitionReference>(libraryEntity, true);
            var direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            foreach (var (skills, slots, activeHand, player) in SystemAPI.Query<DynamicBuffer<PlayerAvailableSkill>,
                DynamicBuffer<PlayerEquipmentSlot>, RefRO<PlayerActiveHand>>().WithAll<PlayerTag>().WithEntityAccess())
            {
                foreach (var entry in skills)
                {
                    if (entry.SkillIndex < 0 || entry.SourceSlot != activeHand.ValueRO.Slot ||
                        PlayerEquipmentUtility.GetItem(slots, entry.SourceSlot) != entry.SourceItem ||
                        !EntityManager.Exists(entry.Target) || !EntityManager.HasComponent<WorldItem>(entry.Target)) continue;
                    var producer = SkillProducerUtility.GetSkills(EntityManager, libraryEntity, references, entry.SourceItem);
                    if (producer == null || entry.SkillIndex >= producer.Length) continue;
                    var action = FindAvailableRefuel(producer[entry.SkillIndex], EntityManager, player, entry.TargetPosition, checkDistance: false);
                    if (action == null || !action.CanInspect(EntityManager, player, entry.SourceSlot, entry.SourceItem, entry.Target, entry.TargetPosition)) continue;
                    var world = EntityManager.GetComponentData<WorldItem>(entry.Target);
                    if (!ItemDefinitionReference.TryGet(references, world.Instance.DefinitionId, out var definition) || definition.WorldSprite == null) continue;
                    var anchor = LabelAnchor(entry.TargetPosition, direction, definition.WorldSprite.rect.height / 32f, Shader.GetGlobalFloat(DepthOrigin));
                    var screen = camera.WorldToScreenPoint(anchor);
                    if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
                    if (canvasObject == null) CreateLabel();
                    WorldLabelStyle.EnsureApplied(label);
                    int current = Mathf.CeilToInt(world.Instance.Durability), maximum = Mathf.CeilToInt(world.Instance.MaxDurability);
                    if (current != shownCurrent || maximum != shownMaximum)
                    {
                        label.SetText("{0} / {1}", (float)current, (float)maximum);
                        shownCurrent = current; shownMaximum = maximum;
                    }
                    label.rectTransform.position = new Vector3(screen.x, screen.y, 0);
                    if (!canvasObject.activeSelf) canvasObject.SetActive(true);
                    return;
                }
            }
            Hide();
        }

        private void CreateLabel()
        {
            canvasObject = new GameObject("Refuel target durability", typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var prefab = Resources.Load<GameObject>("PlayerSkillMessage");
            label = prefab != null ? Object.Instantiate(prefab).GetComponent<TextMeshProUGUI>()
                : new GameObject("Durability", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(canvasObject.transform, false);
            label.rectTransform.sizeDelta = new Vector2(220, 44);
            label.alignment = TextAlignmentOptions.Center;
            WorldLabelStyle.Apply(label);
            label.richText = false;
        }

        private void Hide() { if (canvasObject != null && canvasObject.activeSelf) canvasObject.SetActive(false); }
        protected override void OnStopRunning() => Hide();
        protected override void OnDestroy() { if (canvasObject != null) Object.Destroy(canvasObject); }
    }
}
