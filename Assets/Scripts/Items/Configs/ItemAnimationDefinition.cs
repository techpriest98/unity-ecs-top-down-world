using System;
using Game.Player;
using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "ItemAnimation",
        menuName = "Game/Items/Animation Definition")]
    public sealed class ItemAnimationDefinition : ScriptableObject
    {
        public HandAnimations LeftHand = new HandAnimations();
        public HandAnimations RightHand = new HandAnimations();

        [Serializable]
        public sealed class HandAnimations
        {
            [Header("Arm")]
            public AnimationID ArmIdle = AnimationID.Idle;
            public AnimationID ArmWalk = AnimationID.Walk;

            [Header("Item")]
            public bool HasVisual;
            public CharacterPart VisualPart;
            public AnimationID ItemIdle = AnimationID.Idle;
            public AnimationID ItemWalk = AnimationID.Walk;
        }
    }
}