using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Networking;

namespace EarthUnscripted
{
    // Main-thread requests use the current capability even after worker recovery.
    public static class WorkerHttp
    {
        static readonly Dictionary<int, string> Sessions = new Dictionary<int, string>();
        public static bool ValidToken(string token)
        {
            if (token == null || token.Length != 64) return false;
            foreach (char c in token) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
        public static void Register(int port, string token)
        {
            if (port < 1 || port > 65535 || !ValidToken(token)) throw new InvalidDataException("Invalid worker session");
            Sessions[port] = token;
        }
        public static void Forget(int port) => Sessions.Remove(port);
        public static UnityWebRequest Authorize(UnityWebRequest request)
        {
            if (!Uri.TryCreate(request.url, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
                uri.Host != "127.0.0.1" || uri.UserInfo.Length != 0 || !Sessions.TryGetValue(uri.Port, out var token))
            {
                request.Dispose();
                throw new InvalidDataException("Owned world worker session required");
            }
            request.redirectLimit = 0;
            request.SetRequestHeader("X-Earth-Session", token);
            return request;
        }
    }
}
