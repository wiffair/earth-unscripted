using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoadRush.Packs
{
    public enum PackValidationState
    {
        Unsigned, SignatureInvalid, UntrustedIssuer, OtherGameBuild, OtherPlatform, OtherSubset,
        VerifiedPartialEvidence, ValidatedForThisBuild
    }

    /// <summary>
    /// Signed test attestation, separate from engine compatibility and whole-pack integrity.
    /// Public keys may be distributed; private keys and signing authority never belong in a content pack.
    /// Evidence says what a trusted issuer tested; it is not a guarantee against every gameplay defect.
    /// </summary>
    public sealed class WorldPackValidation
    {
        public const int MaximumRecordBytes = 16384;
        public readonly string ManifestSha256, SubsetSha256, EngineVersion, GameBuildSha256, Platform, ReportSha256, IssuedUtc, PublicKey, Signature;
        public readonly IReadOnlyList<string> Checks;
        public string IssuerFingerprint => WorldPackPolicy.Hash(Encoding.ASCII.GetBytes(PublicKey));
        static readonly string[] RequiredNativeChecks = { "native-load", "native-drive", "native-render" };
        static readonly HashSet<string> KnownChecks = new HashSet<string>(new[] { "manifest", "archive", "managed", "native-load", "native-drive", "native-render", "lan", "endurance" }, StringComparer.Ordinal);
        WorldPackValidation(JObject json)
        {
            string[] keys = { "format", "manifestSha256", "subsetSha256", "engineVersion", "gameBuildSha256", "platform", "reportSha256", "issuedUtc", "publicKey", "signature", "checks" };
            WorldPackPolicy.Require(json.Properties().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(keys.OrderBy(p => p, StringComparer.Ordinal)), "validation record fields");
            WorldPackPolicy.Require(Text(json, "format", 64) == "RoadRushPackValidation1", "validation format");
            ManifestSha256 = Digest(json, "manifestSha256"); SubsetSha256 = Digest(json, "subsetSha256"); GameBuildSha256 = Digest(json, "gameBuildSha256"); ReportSha256 = Digest(json, "reportSha256");
            EngineVersion = Text(json, "engineVersion", 32); WorldPackPolicy.EngineVersion(EngineVersion);
            Platform = Text(json, "platform", 40); WorldPackPolicy.Require(WorldPackPolicy.SafeId(Platform), "validation platform");
            IssuedUtc = Text(json, "issuedUtc", 20); WorldPackPolicy.Require(DateTimeOffset.TryParseExact(IssuedUtc, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _), "validation timestamp");
            PublicKey = Text(json, "publicKey", 2048); Signature = Text(json, "signature", 1024);
            var scopes = json["checks"] as JArray; WorldPackPolicy.Require(scopes != null && scopes.Count > 0 && scopes.Count <= KnownChecks.Count && scopes.All(t => t.Type == JTokenType.String), "validation scopes");
            var checks = scopes.Select(t => (string)t).ToList(); WorldPackPolicy.Require(checks.All(KnownChecks.Contains) && checks.Distinct(StringComparer.Ordinal).Count() == checks.Count, "validation scope identity"); Checks = checks.AsReadOnly();
        }
        static string Text(JObject json, string key, int maximum)
        {
            WorldPackPolicy.Require(json[key]?.Type == JTokenType.String, "validation text " + key); string value = (string)json[key];
            WorldPackPolicy.Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum, "validation text length " + key); return value;
        }
        static string Digest(JObject json, string key)
        { string value = Text(json, key, 64); WorldPackPolicy.Require(Regex.IsMatch(value, "\\A[a-f0-9]{64}\\z"), "validation digest " + key); return value; }
        public static WorldPackValidation Parse(byte[] bytes)
        {
            WorldPackPolicy.Require(bytes != null && bytes.Length > 0 && bytes.Length <= MaximumRecordBytes, "validation byte budget");
            using (var input = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
            {
                var json = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(!input.Read(), "trailing validation content"); return new WorldPackValidation(json);
            }
        }
        public byte[] CanonicalBytes()
        {
            // Fixed algorithm and ordered base64 fields avoid ambiguous concatenation or unsigned claims.
            var values = new[] { "RoadRushPackValidation1", ManifestSha256, SubsetSha256, EngineVersion, GameBuildSha256, Platform, ReportSha256, IssuedUtc, PublicKey, string.Join(",", Checks.OrderBy(c => c, StringComparer.Ordinal)) };
            return Encoding.ASCII.GetBytes(string.Join("|", values.Select(v => Convert.ToBase64String(Encoding.UTF8.GetBytes(v)))));
        }
        public bool SignatureIsValid()
        {
            try
            {
                string[] parts = PublicKey.Split('.'); if (parts.Length != 2) return false;
                byte[] modulus = Convert.FromBase64String(parts[0]), exponent = Convert.FromBase64String(parts[1]);
                if (modulus.Length < 256 || modulus.Length > 512 || exponent.Length < 1 || exponent.Length > 8) return false;
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
                    return rsa.VerifyData(CanonicalBytes(), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception error) when (error is FormatException || error is CryptographicException || error is ArgumentException) { return false; }
        }

        /// <summary>
        /// Compare a certificate with a just-verified content identity and resolved subset. The caller MUST verify
        /// installed file bytes before supplying these identities; this method checks an attestation, not the filesystem.
        /// A compatible new engine or partial test suite never inherits a full native-test badge.
        /// </summary>
        public PackValidationState Evaluate(string manifestHash, string subsetHash, string currentEngineVersion,
            string currentGameBuildHash, string platform, ISet<string> trustedIssuerFingerprints)
        {
            if (ManifestSha256 != manifestHash || !SignatureIsValid()) return PackValidationState.SignatureInvalid;
            if (trustedIssuerFingerprints == null || !trustedIssuerFingerprints.Contains(IssuerFingerprint)) return PackValidationState.UntrustedIssuer;
            if (Platform != platform) return PackValidationState.OtherPlatform;
            if (GameBuildSha256 != currentGameBuildHash || EngineVersion != currentEngineVersion) return PackValidationState.OtherGameBuild;
            if (SubsetSha256 != subsetHash) return PackValidationState.OtherSubset;
            return RequiredNativeChecks.All(Checks.Contains) ? PackValidationState.ValidatedForThisBuild : PackValidationState.VerifiedPartialEvidence;
        }
    }
}
