using UnityEngine;

namespace Game.World.Trees
{
    // Prototype controls. Population is deterministic; saved-world integration is separate.
    public sealed class PineTreeTest : MonoBehaviour
    {
        public static PineTreeTest Active { get; private set; }
        public PineSettings Shape = new PineSettings();
        public bool PopulateTurf = true;
        [Tooltip("Tree shadows: detailed proxies near the player and cached coarse sun shadows on distant terrain/water.")]
        public bool DetailedShadows = true;
        [Range(0, 1)] public float TurfDensity = .65f;
        [Range(0, 3)] public int QuarterTurn;
        [Range(.5f, 1.5f)] public float Scale = 1;
        private void OnEnable() => Active = this;
        private void OnDisable() { if (Active == this) Active = null; }
    }
}
