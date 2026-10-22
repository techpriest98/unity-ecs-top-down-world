using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(
        fileName = "AnimationDatabase",
        menuName = "Game/Animation/Database")]
    public sealed class AnimationDatabase : ScriptableObject
    {
        public AnimationDefinition[] Animations;
        [Tooltip("Optional material to receive both arrays after a successful build.")]
        public Material TargetMaterial;
    }
}
