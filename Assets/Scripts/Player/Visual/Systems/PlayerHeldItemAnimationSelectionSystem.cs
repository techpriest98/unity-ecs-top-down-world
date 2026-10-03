using Game.Items;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerArmAnimationSelectionSystem))]
    [UpdateBefore(typeof(PlayerAnimationSystem))]
    public partial struct PlayerHeldItemAnimationSelectionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemLibrary>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ItemLibrary library = SystemAPI.GetSingleton<ItemLibrary>();

            if (!library.Value.IsCreated)
                return;

            ref ItemLibraryBlob items = ref library.Value.Value;

            state.EntityManager
                .CompleteDependencyBeforeRO<PlayerEquipmentSlot>();

            var equipmentLookup =
                SystemAPI.GetBufferLookup<PlayerEquipmentSlot>(true);

            foreach (var (moveInput, parts, entity) in
                     SystemAPI.Query<
                             RefRO<PlayerMoveInput>,
                             DynamicBuffer<PlayerVisualPart>>()
                         .WithAll<PlayerTag>()
                         .WithEntityAccess())
            {
                var writableParts = parts;

                bool isMoving =
                    math.lengthsq(moveInput.ValueRO.Value) > 0.0001f;

                ItemID leftItem = ItemID.None;
                ItemID rightItem = ItemID.None;

                if (equipmentLookup.HasBuffer(entity))
                {
                    DynamicBuffer<PlayerEquipmentSlot> slots =
                        equipmentLookup[entity];

                    leftItem = PlayerEquipmentUtility.GetItem(
                        slots,
                        EquipmentSlot.LeftHand);

                    rightItem = PlayerEquipmentUtility.GetItem(
                        slots,
                        EquipmentSlot.RightHand);
                }

                bool hasLeftVisual = TryGetVisual(
                    ref items,
                    leftItem,
                    true,
                    out ItemHandAnimationData leftHand);

                bool hasRightVisual = TryGetVisual(
                    ref items,
                    rightItem,
                    false,
                    out ItemHandAnimationData rightHand);

                for (int i = 0; i < parts.Length; i++)
                {
                    PlayerVisualPart part = parts[i];

                    if (!IsHeldItemPart(ref items, part.Part))
                        continue;

                    bool visible = false;
                    AnimationID animationId = AnimationID.Idle;

                    if (hasLeftVisual && leftHand.VisualPart == part.Part)
                    {
                        visible = true;
                        animationId = isMoving
                            ? leftHand.ItemWalk
                            : leftHand.ItemIdle;
                    }
                    else if (hasRightVisual && rightHand.VisualPart == part.Part)
                    {
                        visible = true;
                        animationId = isMoving
                            ? rightHand.ItemWalk
                            : rightHand.ItemIdle;
                    }

                    if (part.AnimationId == animationId && part.IsVisible == visible)
                    {
                        continue;
                    }

                    part.AnimationId = animationId;
                    part.IsVisible = visible;
                    writableParts[i] = part;
                }
            }
        }

        private static bool IsHeldItemPart(
            ref ItemLibraryBlob library,
            CharacterPart part)
        {
            // These parts are controlled by their own systems.
            if (part == CharacterPart.Body ||
                part == CharacterPart.LeftArm ||
                part == CharacterPart.RightArm)
            {
                return false;
            }

            for (int i = 0; i < library.Items.Length; i++)
            {
                ref ItemData item = ref library.Items[i];

                if (!item.HasAnimation)
                    continue;

                if (item.LeftHand.HasVisual && item.LeftHand.VisualPart == part)
                {
                    return true;
                }

                if (item.RightHand.HasVisual && item.RightHand.VisualPart == part)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetVisual(
            ref ItemLibraryBlob library,
            ItemID itemId,
            bool isLeftHand,
            out ItemHandAnimationData hand)
        {
            hand = default;

            if (itemId == ItemID.None)
                return false;

            for (int i = 0; i < library.Items.Length; i++)
            {
                ref ItemData item = ref library.Items[i];

                if (item.Id != itemId)
                    continue;

                if (!item.HasAnimation)
                    return false;

                hand = isLeftHand
                    ? item.LeftHand
                    : item.RightHand;

                return hand.HasVisual;
            }

            return false;
        }
    }
}