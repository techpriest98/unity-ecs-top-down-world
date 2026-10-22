using Game.Items;
using Unity.Mathematics;

namespace Game.Player
{
    // Approximate held emitter (torch flame / staff head), in world units.
    // The sprite convention has the right hand on screen-left when facing +Z.
    public static class PlayerHeldLightPositionUtility
    {
        public const float HandSideOffset = .65f;
        public const float HandForwardOffset = .35f;
        public const float EmitterHeightFraction = .75f;

        public static float3 RuntimeAnchor(float3 anchor, PlayerSpriteDirection direction)
        {
            // All sprite directions share +Z toward camera, -Z away.
            return anchor;
        }

        public static float3 FromSpriteAnchor(float3 feet, float height, float3 anchor,
            Game.World.Rendering.ViewDirection view)
        {
            float pixelScale = math.max(0f, height) / 98f;
            int2 right = Game.World.Rendering.ViewDirectionUtility.Right(view);
            int2 toward = Game.World.Rendering.ViewDirectionUtility.Forward(view);
            float x = (anchor.x - 32f) * pixelScale;
            return feet + new float3(right.x * x + toward.x * anchor.z,
                (anchor.y - 25f) * pixelScale, right.y * x + toward.y * anchor.z);
        }

        public static float3 Position(float3 feet, float height,
            PlayerFacingDirection facing, EquipmentSlot slot,
            Game.World.Rendering.ViewDirection view = Game.World.Rendering.ViewDirection.Front)
        {
            var direction = PlayerSpriteDirectionUtility.WorldToScreen(facing, view);
            float3 offset = SpriteOffset(direction, slot, height);
            int2 right = Game.World.Rendering.ViewDirectionUtility.Right(view);
            int2 toward = Game.World.Rendering.ViewDirectionUtility.Forward(view);
            return feet + new float3(right.x * offset.x + toward.x * offset.z,
                offset.y, right.y * offset.x + toward.y * offset.z);
        }

        public static float3 SpriteOffset(PlayerSpriteDirection direction, EquipmentSlot slot, float height)
        {
            float side = slot == EquipmentSlot.LeftHand ? 1f :
                slot == EquipmentSlot.RightHand ? -1f : 0f;
            // Match the authored sprite convention: facing right, left hand is near;
            // facing left, right hand is near. Depth is positive toward the camera.
            float3 offset = direction switch
            {
                PlayerSpriteDirection.Right => new float3(HandForwardOffset, 0, side * HandSideOffset),
                PlayerSpriteDirection.Left => new float3(-HandForwardOffset, 0, -side * HandSideOffset),
                PlayerSpriteDirection.Up => new float3(-side * HandSideOffset, 0, -HandForwardOffset),
                _ => new float3(side * HandSideOffset, 0, HandForwardOffset)
            };
            offset.y = math.max(0f, height) * EmitterHeightFraction;
            return offset;
        }
    }
}
