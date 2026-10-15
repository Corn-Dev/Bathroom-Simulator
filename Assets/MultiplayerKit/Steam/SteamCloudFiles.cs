// Thin Steam Remote Storage wrapper for the world save store. Design: 04 §2.4 (cloud usable rule, quota, listing),
// 06 S11; call style from the input SaveManager (FileWrite, FileExists, GetFileSize, FileRead).
// Every call is guarded by SteamPlatform.IsInitialized and wrapped in try/catch: a Steamworks.NET exception is
// logged at Warning and reported as failure, so SteamWorldSaveStore can always fall back to local files.
// File names are flat per app and account (Steam Cloud already separates accounts).

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Steamworks;

namespace MultiplayerKit.Steam
{
    internal sealed class SteamCloudFiles
    {
        /// <summary>Steam initialized and cloud enabled for both the account and the app (SaveManager rule, 04 §2.4).</summary>
        public bool IsUsable
        {
            get
            {
                if (!SteamPlatform.IsInitialized) return false;
                try
                {
                    return SteamRemoteStorage.IsCloudEnabledForAccount() && SteamRemoteStorage.IsCloudEnabledForApp();
                }
                catch (Exception e)
                {
                    Warn("cloud state", e);
                    return false;
                }
            }
        }

        /// <summary>False when GetQuota fails or fewer than bytesNeeded bytes are available.</summary>
        public bool HasQuota(long bytesNeeded)
        {
            if (!SteamPlatform.IsInitialized) return false;
            try
            {
                if (!SteamRemoteStorage.GetQuota(out _, out ulong available)) return false;
                return bytesNeeded <= 0 || available >= (ulong)bytesNeeded;
            }
            catch (Exception e)
            {
                Warn("GetQuota", e);
                return false;
            }
        }

        public bool TryWrite(string name, byte[] data)
        {
            if (!SteamPlatform.IsInitialized || string.IsNullOrEmpty(name) || data == null) return false;
            try
            {
                return SteamRemoteStorage.FileWrite(name, data, data.Length);
            }
            catch (Exception e)
            {
                Warn("FileWrite " + name, e);
                return false;
            }
        }

        /// <summary>
        /// Whole file. SaveNotFound when missing, SaveCorrupt when empty or larger than maxBytes (nothing is read),
        /// SaveIoFailed on a short read or a Steam exception.
        /// </summary>
        public KitResult<byte[]> TryRead(string name, long maxBytes)
        {
            if (!SteamPlatform.IsInitialized || string.IsNullOrEmpty(name))
            {
                return KitResult<byte[]>.Fail(KitError.SaveIoFailed, "Steam Cloud unavailable");
            }
            try
            {
                if (!SteamRemoteStorage.FileExists(name)) return KitResult<byte[]>.Fail(KitError.SaveNotFound, "not in Steam Cloud");
                int size = SteamRemoteStorage.GetFileSize(name);
                if (size <= 0 || size > maxBytes)
                {
                    return KitResult<byte[]>.Fail(KitError.SaveCorrupt, "cloud file size " + size + " out of range");
                }
                var bytes = new byte[size];
                int read = SteamRemoteStorage.FileRead(name, bytes, size);
                if (read != size) return KitResult<byte[]>.Fail(KitError.SaveIoFailed, "short cloud read " + read + "/" + size);
                return KitResult<byte[]>.From(bytes);
            }
            catch (Exception e)
            {
                Warn("FileRead " + name, e);
                return KitResult<byte[]>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
        }

        public bool Exists(string name)
        {
            if (!SteamPlatform.IsInitialized || string.IsNullOrEmpty(name)) return false;
            try
            {
                return SteamRemoteStorage.FileExists(name);
            }
            catch (Exception e)
            {
                Warn("FileExists " + name, e);
                return false;
            }
        }

        /// <summary>FileDelete removes the local cache copy and propagates the delete to the cloud.</summary>
        public bool Delete(string name)
        {
            if (!SteamPlatform.IsInitialized || string.IsNullOrEmpty(name)) return false;
            try
            {
                return SteamRemoteStorage.FileDelete(name);
            }
            catch (Exception e)
            {
                Warn("FileDelete " + name, e);
                return false;
            }
        }

        /// <summary>Names of every cloud file matching pattern; what was read before an exception is kept.</summary>
        public List<string> ListNames(Regex pattern)
        {
            var names = new List<string>();
            if (!SteamPlatform.IsInitialized || pattern == null) return names;
            try
            {
                int count = SteamRemoteStorage.GetFileCount();
                for (int i = 0; i < count; i++)
                {
                    string name = SteamRemoteStorage.GetFileNameAndSize(i, out _);
                    if (!string.IsNullOrEmpty(name) && pattern.IsMatch(name)) names.Add(name);
                }
            }
            catch (Exception e)
            {
                Warn("file listing", e);
            }
            return names;
        }

        private static void Warn(string what, Exception e)
        {
            KitLog.Warning("[WorldSave] Steam Cloud " + what + " failed: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
