using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public struct ProjectionOccupancy
    {
        // Face type: bits 0-7; source index: 8-23; submerged lower Top: 24.
        private const uint LowerTopFlag = 1u << 24;
        private NativeParallelHashMap<int2, uint> _occupied;

        public ProjectionOccupancy(int capacity, Allocator allocator)
        {
            _occupied = new NativeParallelHashMap<int2, uint>(
                capacity,
                allocator);
        }

        public bool IsOccupied(int x, int y)
        {
            return _occupied.ContainsKey(new int2(x, y));
        }

        public bool IsLowerTop(int x, int y, ushort source) =>
            _occupied.TryGetValue(new int2(x, y), out uint face) &&
            (face & LowerTopFlag) != 0 && ((face >> 8) & 0xFFFFu) == source;

        public bool TryOccupy(int x, int y, ProjectedFaceType faceType, ushort source = 0, bool lowerTop = false)
        {
            var position = new int2(x, y);

            if (_occupied.ContainsKey(position))
                return false;

            _occupied.Add(position, (uint)faceType | ((uint)source << 8) | (lowerTop ? LowerTopFlag : 0u));
            return true;
        }

        public void Clear()
        {
            _occupied.Clear();
        }

        public void Dispose()
        {
            if (_occupied.IsCreated)
                _occupied.Dispose();
        }
    }
}
