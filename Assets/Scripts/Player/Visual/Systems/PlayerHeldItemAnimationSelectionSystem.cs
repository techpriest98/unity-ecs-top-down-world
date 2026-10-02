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

                    bool visible;
                    ItemHandAnimationData hand;

                    switch (part.Part)
                    {
                        case CharacterPart.LeftHandTorch:
                            hand = leftHand;
                            visible = hasLeftVisual &&
                                      hand.VisualPart == part.Part;
                            break;

                        case CharacterPart.RightHandTorch:
                            hand = rightHand;
                            visible = hasRightVisual &&
                                      hand.VisualPart == part.Part;
                            break;

                        default:
                            continue;
                    }

                    AnimationID animationId = visible
                        ? (isMoving ? hand.ItemWalk : hand.ItemIdle)
                        : AnimationID.Idle;

                    if (part.AnimationId != animationId ||
                        part.IsVisible != visible)
                    {
                        part.AnimationId = animationId;
                        part.IsVisible = visible;
                        writableParts[i] = part;
                    }
                }
            }
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