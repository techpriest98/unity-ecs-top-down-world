using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.World.Saving
{
    public enum WorldCreateStatus
    {
        Success,
        InvalidName,
        NameAlreadyExists,
        StorageError
    }

    public static class WorldMetadataStorage
    {
        public const int CurrentVersion = 1;
        private static readonly object Sync = new object();
        public static string RootPath => Path.Combine(Application.persistentDataPath, "Worlds");

        public static string NormalizeName(string name) =>
            (name ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);

        public static bool TryList(out List<WorldMetadata> worlds, out string error)
        {
            lock (Sync)
            {
                worlds = new List<WorldMetadata>();
                error = string.Empty;
                try
                {
                    if (!Directory.Exists(RootPath))
                        return true;

                    foreach (string directory in Directory.GetDirectories(RootPath))
                    {
                        string path = Path.Combine(directory, "world.json");
                        // An interrupted first write may leave only a temporary file.
                        if (!File.Exists(path))
                            continue;

                        WorldMetadata world = JsonUtility.FromJson<WorldMetadata>(File.ReadAllText(path));
                        if (world == null || world.SaveVersion != CurrentVersion ||
                            !Guid.TryParseExact(world.Id, "N", out _) ||
                            !string.Equals(world.Id, Path.GetFileName(directory), StringComparison.Ordinal) ||
                            string.IsNullOrWhiteSpace(world.Name))
                        {
                            throw new InvalidDataException("Invalid or unsupported metadata: " + path);
                        }
                        worlds.Add(world);
                    }
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    worlds.Clear();
                    error = "Could not read saved worlds. Check the Console.";
                    return false;
                }
            }
        }

        public static bool TryDelete(string worldId, out string error)
        {
            lock (Sync)
            {
                error = string.Empty;
                if (!Guid.TryParseExact(worldId, "N", out _))
                {
                    error = "Invalid world ID.";
                    return false;
                }

                try
                {
                    string directory = Path.Combine(RootPath, worldId);
                    // A world already removed is also a successful deletion.
                    if (!Directory.Exists(directory)) return true;
                    Directory.Delete(directory, true);
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    error = "Could not delete the world. Please try again.";
                    return false;
                }
            }
        }

        public static WorldCreateStatus Create(string name, uint seed,
            out WorldMetadata world, out string error)
        {
            lock (Sync)
            {
                world = null;
                error = string.Empty;
                string directory = null;
                bool ownsDirectory = false;
                try
                {
                    name = NormalizeName(name);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "Enter a world name.";
                        return WorldCreateStatus.InvalidName;
                    }
                    if (!TryList(out List<WorldMetadata> worlds, out error))
                        return WorldCreateStatus.StorageError;
                    foreach (WorldMetadata existing in worlds)
                    {
                        if (string.Equals(NormalizeName(existing.Name), name, StringComparison.OrdinalIgnoreCase))
                        {
                            error = "A world with this name already exists.";
                            return WorldCreateStatus.NameAlreadyExists;
                        }
                    }

                    string id = Guid.NewGuid().ToString("N");
                    directory = Path.Combine(RootPath, id);
                    if (Directory.Exists(directory))
                        throw new IOException("World directory already exists.");
                    Directory.CreateDirectory(directory);
                    ownsDirectory = true;
                    WorldMetadata metadata = new WorldMetadata
                    {
                        SaveVersion = CurrentVersion,
                        Id = id,
                        Name = name,
                        Seed = seed,
                        CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        // Empty until a successful game launch is recorded in a later step.
                        LastPlayedAtUtc = string.Empty
                    };
                    string temporaryPath = Path.Combine(directory, "world.json.tmp");
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(metadata, true));
                    using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    File.Move(temporaryPath, Path.Combine(directory, "world.json"));
                    world = metadata;
                    return WorldCreateStatus.Success;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    error = "Could not save the world. Check the Console.";
                    if (ownsDirectory)
                    {
                        try
                        {
                            string temporaryPath = Path.Combine(directory, "world.json.tmp");
                            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                            if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
                        }
                        catch (Exception cleanupException) { Debug.LogException(cleanupException); }
                    }
                    
                    return WorldCreateStatus.StorageError;
                }
            }
        }
    }
}
