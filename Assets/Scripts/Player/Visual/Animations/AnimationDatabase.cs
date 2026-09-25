using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(
        fileName = "AnimationDatabase",
        menuName = "Game/Animation/Database")]
    public sealed class AnimationDatabase : ScriptableObject
    {
        public AnimationDefinition[] Animations;
    }
}