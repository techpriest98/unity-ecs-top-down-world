using Game.Items;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(PlayerLightDebugSystem))]
    [UpdateAfter(typeof(PlayerBodyAnimationSelectionSystem))]
    [UpdateBefore(typeof(PlayerAnimationSystem))]
    public partial struct PlayerArmAnimationSelectionSystem : ISystem
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

                AnimationID leftAnimation = SelectAnimation(
                    ref items,
                    leftItem,
                    true,
                    isMoving);

                AnimationID rightAnimation = SelectAnimation(
                    ref items,
                    rightItem,
                    false,
                    isMoving);

                for (int i = 0; i < parts.Length; i++)
                {
                    PlayerVisualPart part = parts[i];

                    AnimationID animationId;

                    switch (part.Part)
                    {
                        case CharacterPart.LeftArm:
                            animationId = leftAnimation;
                            break;

                        case CharacterPart.RightArm:
                            animationId = rightAnimation;
                            break;

                        default:
                            continue;
                    }

                    if (part.AnimationId != animationId || !part.IsVisible)
                    {
                        part.AnimationId = animationId;
                        part.IsVisible = true;
                        writableParts[i] = part;
                    }
                }
            }
        }

        private static AnimationID SelectAnimation(
            ref ItemLibraryBlob library,
            ItemID itemId,
            bool isLeftHand,
            bool isMoving)
        {
            AnimationID defaultAnimation = isMoving
                ? AnimationID.Walk
                : AnimationID.Idle;

            if (itemId == ItemID.None)
                return defaultAnimation;

            for (int i = 0; i < library.Items.Length; i++)
            {
                ref ItemData item = ref library.Items[i];

                if (item.Id != itemId)
                    continue;

                if (!item.HasAnimation)
                    return defaultAnimation;

                ItemHandAnimationData hand = isLeftHand
                    ? item.LeftHand
                    : item.RightHand;

                return isMoving
                    ? hand.ArmWalk
                    : hand.ArmIdle;
            }

            return defaultAnimation;
        }
    }
}