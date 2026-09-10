using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.World.Saving
{
    public static class WorldStateStorage
    {
        public static bool TryRead(string id, uint seed, out WorldStateData data, out string error)
        {
            data = null;
            error = string.Empty;

            try
            {
                string path = GetPath(id);
                if (!File.Exists(path))
                    return true;

                data = new WorldStateData
                {
                    X = float.NaN, Y = float.NaN, Z = float.NaN,
                    Facing = -1, View = -1, Hour = -1, UpdateTimer = float.NaN,
                    VerticalVelocity = float.NaN
                };

                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), data);
                Validate(data, id, seed);
                return true;
            }
            catch (Exception exception)
            {
                data = null;
                error = "Could not read world state. Check the Console.";
                Debug.LogException(exception);

                return false;
            }
        }

        public static bool TryWrite(WorldStateData data, out string error)
        {
            error = string.Empty;

            try
            {
                Validate(data, data.WorldId, data.Seed);
                string path = GetPath(data.WorldId);

                if (!File.Exists(Path.Combine(Path.GetDirectoryName(path), "world.json")))
                    throw new IOException("World metadata is missing.");

                string temporary = path + ".tmp";
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);

                return true;
            }
            catch (Exception exception)
            {
                error = "Could not save world state. Please try again.";
                Debug.LogException(exception);

                return false;
            }
        }

        private static string GetPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Invalid world ID.");

            return Path.Combine(WorldMetadataStorage.RootPath, id, "state.json");
        }

        private static void Validate(WorldStateData data, string id, uint seed)
        {
            if (
                data == null ||
                data.Version != 1 ||
                data.WorldId != id ||
                data.Seed != seed ||
                !Finite(data.X) ||
                !Finite(data.Y) ||
                !Finite(data.Z) ||
                !Finite(data.VerticalVelocity) ||
                !Finite(data.UpdateTimer) ||
                data.UpdateTimer < 0 ||
                data.Hour < 0 || data.Hour > 23 ||
                data.Facing < 0 || data.Facing > 3 ||
                data.View < 0 || data.View > 3)
                throw new InvalidDataException("Invalid or incompatible world state.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
