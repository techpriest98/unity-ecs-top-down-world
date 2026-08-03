using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public struct ProjectionOccupancy
    {
        private NativeParallelHashMap<int2, ProjectedFaceType> _occupied;

        public ProjectionOccupancy(int capacity, Allocator allocator)
        {
            _occupied = new NativeParallelHashMap<int2, ProjectedFaceType>(
                capacity,
                allocator);
        }

        public bool IsOccupied(int x, int y)
        {
            return _occupied.ContainsKey(new int2(x, y));
        }

        public bool TryOccupy(int x, int y, ProjectedFaceType faceType)
        {
            var position = new int2(x, y);

            if (_occupied.ContainsKey(position))
                return false;

            _occupied.Add(position, faceType);
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