using Unity.Entities;
using UnityEngine;

namespace Game.World.Rendering
{
    public sealed class ViewDirectionAuthoring : MonoBehaviour
    {
        [SerializeField]
        private ViewDirection initialDirection =
            ViewDirection.Front;

        private sealed class Baker : Baker<ViewDirectionAuthoring>
        {
            public override void Bake(
                ViewDirectionAuthoring authoring)
            {
                Entity entity =
                    GetEntity(
                        TransformUsageFlags.None);

                AddComponent(
                    entity,
                    new ViewDirectionComponent
                    {
                        Value = authoring.initialDirection
                    });
            }
        }
    }
}