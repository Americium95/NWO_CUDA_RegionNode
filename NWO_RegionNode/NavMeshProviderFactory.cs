using System;
using System.IO;
using System.Text.Json;

namespace NWO_RegionNode.PathValidation
{
    public class NavMeshProviderResolution
    {
        public IAuthoritativeNavMeshProvider Provider;
        public string Source;
        public bool FailClosed;
        public string Detail;
    }

    public static class NavMeshProviderFactory
    {
        public const string CONFIG_FILE_NAME = "path_validation.json";
        public const string NAVMESH_FILE_NAME = "server-navmesh.json";
        public const string AUTHORITATIVE_KIND = "authoritative-json";
        public const string DENY_ALL_KIND = "deny-all";

        public static NavMeshProviderResolution Resolve(string baseDirectory)
        {
            string path;
            try
            {
                path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, CONFIG_FILE_NAME);
            }
            catch (Exception ex)
            {
                return FailClosed("default", $"config path unusable: {ex.Message}");
            }

            if (!File.Exists(path))
            {
                return FailClosed("default", $"{CONFIG_FILE_NAME} not found");
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;

                string kind = ReadString(root, "providerKind");
                string version = ReadString(root, "navMeshVersion");

                if (string.Equals(kind, AUTHORITATIVE_KIND, StringComparison.OrdinalIgnoreCase))
                {
                    string expectedVersion = ReadString(root, "navMeshVersion");
                    if (!string.Equals(expectedVersion, AuthoritativeNavMeshProvider.TrustedVersion, StringComparison.Ordinal))
                        return FailClosed("config", "configured navMeshVersion does not match trusted mesh");
                    string assetPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "data", NAVMESH_FILE_NAME);
                    var provider = AuthoritativeNavMeshProvider.Load(assetPath);
                    return new NavMeshProviderResolution { Provider = provider, Source = "packaged-navmesh", FailClosed = false,
                        Detail = $"sha256={AuthoritativeNavMeshProvider.TrustedSha256}, polygons={AuthoritativeNavMeshProvider.ExpectedPolygonCount}, portals={AuthoritativeNavMeshProvider.ExpectedPortalCount}" };
                }
                if (string.Equals(kind, DENY_ALL_KIND, StringComparison.OrdinalIgnoreCase))
                    return new NavMeshProviderResolution { Provider = new DefaultNavMeshProvider(version), Source = "config", FailClosed = true,
                        Detail = $"providerKind={DENY_ALL_KIND}, every plan is rejected" };
                return FailClosed("config", $"providerKind '{kind}' is not implemented");
            }
            catch (Exception ex)
            {
                return FailClosed("config", $"config unreadable: {ex.Message}");
            }
        }

        private static string ReadString(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }
            return value.GetString() ?? string.Empty;
        }

        private static NavMeshProviderResolution FailClosed(string source, string detail)
        {
            return new NavMeshProviderResolution
            {
                Provider = new DefaultNavMeshProvider(),
                Source = source,
                FailClosed = true,
                Detail = detail
            };
        }
    }
}
