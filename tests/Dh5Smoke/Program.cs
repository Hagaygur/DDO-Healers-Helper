using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

internal static class Program
{
    private const string ExpectedPluginId = "54b48548-05ff-48cf-bce1-c6270e3d4a3f";
    private const string ExpectedPluginName = "Healers Helper";
    private const string ExpectedAuthor = "Warned";
    private const string ExpectedAssemblyName = "VoK.HealersHelper";
    private const uint LifeStatePropertyId = 0x10000E0Cu;

    // Official DH 5 Issuer public keys (DungeonHelper.Common.Trust.IssuerKeys.All) for standalone verification.
    private static readonly string[] Dh5IssuerPublicKeyPems =
    [
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA/NSKszqyT4VuQ2Wqdv7m
        a/3uzuiWAksb4a4YdGpGY51ERz/nLq6/d9OkPSWgd/EFtuAbrthlTMacX4LvBgW1
        tjMgUnzoa/rV1kz1uYF/vUXGwmaQIdlcGwJ3etM509/0y0PrMYmy4bN6OGo2sFdl
        ERtzKOKIun3c9duqYfSyTCeWZycKPLNbHsMO2eCPc50Gt2clN7h4Tl8kseT9I0cw
        7nNJBtAPOKhAG9CeiPCM6nDaK/h3CVhTy/ATZpScwbI1B8PoU5NwTs2rciM/AHSI
        x98US3nO0A5/rFValMn1WAYEme0PGe9UGY06B1fc9H36c4BmSIvGhmbURLLn2dqI
        0Iv8BpKpqo0rARi2x+8JbIgp46DutuL7GBfluLTGRmAAT+qujlLSgmb2BS95iaxU
        /E6JGpIBtxFsLAJBq1EEuVECz6s6pYwtU4X0Xc+0/SkOB9K959P9ggjx+oTvkzYY
        +pZct8QJ29z36TEkJo2pRNMT5KATrKU//rVvHm68rmmVztp58RcVFVJBe0MjQE85
        WimZMhzimOCpBi8Pe145n9nV3EOeC5wRzX0EE19bEYhpIBOkQnXkw8pp+NJaQmRi
        g+btOCM2BnMlJGvb4zqtqwNfBFrYp75iDNOcF9KY4qYh1OuBZ6i2sckQ4zFpdwda
        Mh0xljf1LIvwH0E14M/zXCcCAwEAAQ==
        -----END PUBLIC KEY-----
        """,
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA7NRxqo5aL2dvgiA8EHO7
        ebwFWFmng3XxwkKyDqROB23bQZ8fB+MQgp83r5GRQ+wMG+BAXzfIsVnbXRbQu2xr
        Of0jph3f7VYk/bUIXqumCRgQvpKMNkzTFG8tST3kodIib0oL/RyLxbxZHULy7rLx
        NXMpKPaDprLMdPX4yab3pXVUuG6+Ku10BmJ5ZpEYpTRJdlX3cqWv113BBKh/cmqU
        KEh31Td5bJSTopnTu54I5Yblihr47DgqXCg67krz4v1DxmsZjdU7eREBNvu8u7RE
        HZ7D6FHNXUtAPwVIqUZwrk09rPggWFBjP0/kyY/OcfjEEGRCaPKPFpEit0N+4eRe
        wzXP5cJfZnQMN9JBA0QSqx8jx46MbYRgQAHHxgEYtoGWECG6+bLDPR5rYyuy8vR6
        m2E8S5h9e7NPdA8s0wpo2r4lG25aHzTD2i99gwYpBaiqzXeBMZMz3GEGn8Lfx1fZ
        l+jjKKXrbLPrPhhxi6WgIx7TpeVllReLF8aK1lVWhF+e35VB4lBnFbfTCJXjdtdE
        JQCmESR3yOuCCrckPtyutIZDpTJJEAb5X2cgazDOYiyClZR7xa9BhP1Qtr/rmPFc
        inkikyNrtgzP6hRgD9EsvNmXhrQoSsXJV9oQxhpleCVbDB6E5ZZWc/THZHTm0jjf
        XfR9M5d/QeDiu0A75hOwG/cCAwEAAQ==
        -----END PUBLIC KEY-----
        """,
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEArHeOe3w5hbuuW2uJUDXx
        8ks9q7GeWRqW56+C84TS8CUFrS7YkI9LUmkyl5ezb6CqWOVmZuuus+X+loKJb6z6
        EPs/19g7Kyoxhhnt0YJ5UOfBiPW9xcR0hUYs7zS3JncFqayHs67QZAvxPg7Qu3QK
        X5w8dWYXvGDYyYc7q0+QKid4AuS3ihGKpIJGBn/gpYcZR8+yLQ+iTP3gZrzeTjuM
        BrFMWCB4NazkBl6Q6b5HyhxDhJTVONfDcLfKg/Vw/Rz9x6Xh22D8rCfyvfR8jlDk
        4VcXS3U25PaYrRBkD6Ucju8LUiO32sZJSXLGhn7BMoRG34nDvrMVlUGyHfAbUQpD
        zHG4+E7tip7Bkq9bHuJvmfFJBrPx+0qrml8ZFlEOuLCuRDJIcgmXa+BznBWgyFLl
        vkJYzrmqExQcySN4ckbXHEeiv/3SpTIqGVWuG/hqhdYdYQvwPv8XY8aGzbAs/8kM
        Evq7Ey3jioSqRB+WakesXeC8TpFM5sNbQxtS3aLo7ScZhNj4Npl2UEzs7tibczmZ
        5UqSUTDH9Qx1cLvVD7LN2DuN7knibwaH42rHU/jfOfg/ZjTRFNqhxvwqDOEI0ynb
        tK1cCjiotJVzuLbIYQFn742Q7KAU96UqQ1ziFYXCpZYBxX2xOeu/6G33VU2M9216
        tnVLJz9H+7Bva0hYkDNjDMMCAwEAAQ==
        -----END PUBLIC KEY-----
        """,
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAsxBADWLHQH/j38K79WFi
        3HQlyU73gF9iM433UewsK3zoRXMpsRg83n4Ts34TtTne/MobuCWpK78imw/DEZF8
        RZD8zBwe2uTg7whqBST/fX1xc8xofv4NwxXvMm4DivnNsZq1Zk3GiBGxxKjIqntk
        Fw1U1ekpFOdBBuGi+UousOqVDdObAcKdDX1srCUqXOV3uYKNx2btSbVUfXY4Yaha
        Ynnct/aEGWlLQushMfrWx5vihY/150KI36k9RV4SeTdfsfANnAOrn5k/jTUpRMNi
        zyaTCz7LyyaPbeDAOQxCEz/ZL6Csszc4nClvqOaOejKuMlSJ2xbfj0U3GKb8EcRt
        sLC4/XmHcaZ/5i7H02NvGXPribBI/bb7sYssoYpyYuk/Pp4+J8NxiRqjbUCfdGOU
        jUUnnIFQQdiOpu23X2Mp2Dy7DT8e65+tB043k2tTKwbHfHk18vCfTbN+rDjBIPEu
        YurlaAgje3CN4EJrIXt6nE+skeStkVkB7GSbYUbiyZP6hITtPfVUR3LoMLO4NUYx
        tBgvQ13bTdL2J8kMove79BOPZTii9qfzfbmoiM2y6G+QEvHgZylVjrQQ1pwehSbt
        Mw0wrVxFKUO8FFvd2xz4VrzsuRRMXTvSSNGTAHnZIhY1DeWcdXv4HpCwkvPLQoLE
        rftxfNSLLIyzuHvQ1DFmxxECAwEAAQ==
        -----END PUBLIC KEY-----
        """
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: Dh5Smoke <dh5-plugin-zip-or-dll> <dh5-sdk-dir> [--require-signed]");
            return 2;
        }

        string targetPath = Path.GetFullPath(args[0]);
        string sdkDir = Path.GetFullPath(args[1]);
        bool requireSigned = args.Any(a => a.Equals("--require-signed", StringComparison.OrdinalIgnoreCase));
        string sdkDllPath = Path.Combine(sdkDir, "VoK.Sdk.dll");
        if (!File.Exists(sdkDllPath))
            throw new FileNotFoundException("VoK.Sdk.dll not found in DH 5 SDK directory.", sdkDllPath);

        string tempWork = Path.Combine(Path.GetTempPath(), "hh-dh5-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWork);

        try
        {
            string dllPath;
            if (targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                dllPath = VerifyAndExtractZip(targetPath, tempWork, requireSigned);
            }
            else
            {
                if (requireSigned)
                    throw new Exception("--require-signed requires a packaged DH 5 plugin .zip archive");
                dllPath = targetPath;
                Console.WriteLine($"PASS Direct DLL smoke target: {Path.GetFileName(dllPath)} (unsigned mode)");
            }

            VerifyPeReferencesAndTokens(dllPath, sdkDllPath);
            await RunFunctionalRuntimeSmokeAsync(dllPath, sdkDllPath, tempWork);
            Console.WriteLine("ALL DH 5 SMOKE CHECKS PASSED.");
            return 0;
        }
        finally
        {
            try { Directory.Delete(tempWork, true); } catch { }
        }
    }

    private static string VerifyAndExtractZip(string zipPath, string extractDir, bool requireSigned)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(e => e.FullName).ToList();

        foreach (string name in entries)
        {
            if (name.Contains('\\') || name.StartsWith("./", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal))
                throw new Exception($"Non-canonical archive entry path: '{name}'");
            if (name.Contains('/'))
                throw new Exception($"DH 5 plugin ZIP must place files at archive root (no folder prefix): '{name}'");
            string lower = name.ToLowerInvariant();
            if (lower is "vok.sdk.dll" || lower.EndsWith(".pdb", StringComparison.Ordinal) || lower.EndsWith(".key", StringComparison.Ordinal) || lower.EndsWith(".pfx", StringComparison.Ordinal))
                throw new Exception($"Forbidden file in DH 5 plugin ZIP: '{name}'");
        }

        foreach (var entry in archive.Entries)
        {
            string dest = Path.Combine(extractDir, entry.FullName);
            entry.ExtractToFile(dest, overwrite: true);
        }

        string metaPath = Path.Combine(extractDir, "plugin.metadata");
        if (!File.Exists(metaPath))
            throw new Exception("Missing plugin.metadata at archive root");

        using (var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath)))
        {
            var root = metaDoc.RootElement;
            string pluginName = root.GetProperty("PluginName").GetString() ?? "";
            string curVer = root.GetProperty("CurrentVersion").GetString() ?? "";
            if (pluginName != ExpectedPluginName)
                throw new Exception($"plugin.metadata PluginName mismatch: expected '{ExpectedPluginName}', got '{pluginName}'");
            if (string.IsNullOrWhiteSpace(curVer))
                throw new Exception("plugin.metadata CurrentVersion is empty");
            bool allowedHasCurrent = root.GetProperty("AllowedVersions").EnumerateArray()
                .Any(v => v.GetProperty("Version").GetString() == curVer);
            if (!allowedHasCurrent)
                throw new Exception($"plugin.metadata AllowedVersions does not include CurrentVersion '{curVer}'");
        }

        string dllPath = Path.Combine(extractDir, ExpectedAssemblyName + ".dll");
        if (!File.Exists(dllPath) || !File.Exists(Path.Combine(extractDir, ExpectedAssemblyName + ".deps.json")))
            throw new Exception("Missing VoK.HealersHelper.dll or VoK.HealersHelper.deps.json at archive root");

        string manifestPath = Path.Combine(extractDir, ExpectedPluginName + ".manifest");
        bool isSigned = File.Exists(manifestPath);
        if (requireSigned && !isSigned)
            throw new Exception("Expected signed DH 5 bundle (--require-signed), but manifest is missing");

        if (isSigned)
        {
            var expected8 = new[]
            {
                $"{ExpectedPluginName}.entitlement",
                $"{ExpectedPluginName}.entitlement.sig",
                $"{ExpectedPluginName}.manifest",
                $"{ExpectedPluginName}.manifest.sig",
                $"{ExpectedPluginName}.pem",
                $"{ExpectedAssemblyName}.deps.json",
                $"{ExpectedAssemblyName}.dll",
                "plugin.metadata"
            };
            var actualSorted = entries.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var expectedSorted = expected8.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (!actualSorted.SequenceEqual(expectedSorted))
                throw new Exception($"Signed DH 5 ZIP entries mismatch.\nExpected: {string.Join(", ", expectedSorted)}\nActual  : {string.Join(", ", actualSorted)}");

            VerifyCryptographicTrustGate(extractDir, dllPath);
            Console.WriteLine("PASS DH 5 signed ZIP archive layout, entitlement issuer sig, SPKI fingerprint, manifest bidirectional SHA-256, and Authenticode verification");
        }
        else
        {
            Console.WriteLine("PASS DH 5 unsigned ZIP archive layout and plugin.metadata verification");
        }

        return dllPath;
    }

    private static void VerifyCryptographicTrustGate(string dir, string dllPath)
    {
        byte[] entBytes = File.ReadAllBytes(Path.Combine(dir, $"{ExpectedPluginName}.entitlement"));
        byte[] entSig = File.ReadAllBytes(Path.Combine(dir, $"{ExpectedPluginName}.entitlement.sig"));
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(dir, $"{ExpectedPluginName}.manifest"));
        byte[] manifestSig = File.ReadAllBytes(Path.Combine(dir, $"{ExpectedPluginName}.manifest.sig"));
        string pemText = File.ReadAllText(Path.Combine(dir, $"{ExpectedPluginName}.pem"));

        // 1. Verify entitlement signature against DH 5 IssuerKeys.All.
        bool issuerVerified = false;
        foreach (string issuerPem in Dh5IssuerPublicKeyPems)
        {
            using var issuerRsa = RSA.Create();
            issuerRsa.ImportFromPem(issuerPem);
            if (issuerRsa.VerifyData(entBytes, entSig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                issuerVerified = true;
                break;
            }
        }
        if (!issuerVerified)
            throw new Exception("Entitlement signature failed verification against DH 5 IssuerKeys.All");

        // 2. Parse entitlement & compare authorKeyFingerprint to .pem SPKI SHA-256.
        string expectedFp;
        using (var entDoc = JsonDocument.Parse(entBytes))
        {
            var root = entDoc.RootElement;
            if (!string.Equals(root.GetProperty("pluginId").GetString(), ExpectedPluginId, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Entitlement pluginId mismatch");
            if (root.GetProperty("pluginName").GetString() != ExpectedPluginName)
                throw new Exception("Entitlement pluginName mismatch");
            if (root.GetProperty("authorName").GetString() != ExpectedAuthor)
                throw new Exception("Entitlement authorName mismatch");
            expectedFp = (root.GetProperty("authorKeyFingerprint").GetString() ?? "").Trim().ToUpperInvariant();
        }

        using var authorRsa = RSA.Create();
        authorRsa.ImportFromPem(pemText);
        string actualPemFp = Convert.ToHexString(SHA256.HashData(authorRsa.ExportSubjectPublicKeyInfo()));
        if (!string.Equals(actualPemFp, expectedFp, StringComparison.OrdinalIgnoreCase))
            throw new Exception($".pem SPKI SHA-256 '{actualPemFp}' != entitlement authorKeyFingerprint '{expectedFp}'");

        // 3. Verify manifest signature with .pem and bidirectional SHA-256 over every file.
        if (!authorRsa.VerifyData(manifestBytes, manifestSig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new Exception("Manifest RSA-SHA256 signature failed verification against author .pem");

        var manifestMap = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var manDoc = JsonDocument.Parse(manifestBytes))
        {
            var root = manDoc.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != 1)
                throw new Exception("Manifest formatVersion must be 1");
            if (root.GetProperty("pluginName").GetString() != ExpectedPluginName)
                throw new Exception("Manifest pluginName mismatch");
            foreach (var item in root.GetProperty("entries").EnumerateArray())
            {
                string path = item.GetProperty("path").GetString()!;
                string sha = item.GetProperty("sha256").GetString()!.ToUpperInvariant();
                manifestMap[path] = sha;
            }
        }

        var diskFiles = Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n != $"{ExpectedPluginName}.manifest" && n != $"{ExpectedPluginName}.manifest.sig")
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        if (!diskFiles.SetEquals(manifestMap.Keys))
            throw new Exception($"Manifest entry set mismatch.\nManifest: {string.Join(", ", manifestMap.Keys.Order())}\nDisk    : {string.Join(", ", diskFiles.Order())}");

        foreach (var (rel, expectedSha) in manifestMap)
        {
            string actualSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, rel))));
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Manifest SHA-256 mismatch for '{rel}': expected {expectedSha}, got {actualSha}");
        }

        // 4. On Windows: verify Authenticode signature integrity + signer cert SPKI == authorKeyFingerprint.
        if (OperatingSystem.IsWindows())
        {
            VerifyWindowsAuthenticode(dllPath, expectedFp);
        }
    }

    private static void VerifyWindowsAuthenticode(string dllPath, string expectedSpkiFp)
    {
        byte[] pkcs7;
        using (var fs = File.OpenRead(dllPath))
        using (var pe = new PEReader(fs))
        {
            var dir = pe.PEHeaders.PEHeader?.CertificateTableDirectory ?? default;
            if (dir.RelativeVirtualAddress <= 0 || dir.Size <= 8)
                throw new Exception($"DLL '{dllPath}' does not contain a PE Authenticode certificate table");
            fs.Position = dir.RelativeVirtualAddress;
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            int dwLength = br.ReadInt32();
            _ = br.ReadUInt16(); // wRevision
            ushort wCertType = br.ReadUInt16();
            if (wCertType != 0x0002 || dwLength <= 8)
                throw new Exception($"Unexpected WIN_CERTIFICATE type 0x{wCertType:X4} or length {dwLength}");
            pkcs7 = br.ReadBytes(dwLength - 8);
        }

        var root = new AsnReader(pkcs7, AsnEncodingRules.BER).ReadSequence();
        _ = root.ReadObjectIdentifier();
        var signedData = root.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        _ = signedData.ReadInteger();
        _ = signedData.ReadSetOf();
        _ = signedData.ReadSequence();
        var certsSet = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        bool matchedSigner = false;
        while (certsSet.HasData)
        {
            ReadOnlyMemory<byte> certDer = certsSet.ReadEncodedValue();
            using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(certDer.Span);
            using RSA? certRsa = cert.GetRSAPublicKey();
            if (certRsa is null) continue;
            string certFp = Convert.ToHexString(SHA256.HashData(certRsa.ExportSubjectPublicKeyInfo()));
            if (string.Equals(certFp, expectedSpkiFp, StringComparison.OrdinalIgnoreCase))
            {
                matchedSigner = true;
                break;
            }
        }
        if (!matchedSigner)
            throw new Exception($"Authenticode PKCS#7 signer cert SPKI did not match authorKeyFingerprint '{expectedSpkiFp}'");

        // Verify PE digest integrity via WinVerifyTrust (allowing self-signed untrusted root, matching DH 5's AuthenticodeVerifier).
        int trustStatus = WinVerifyTrustFile(dllPath);
        const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
        const int CertEUntrustedRoot = unchecked((int)0x800B0109);
        const int CertEChaining = unchecked((int)0x800B010A);
        if (trustStatus != 0 && trustStatus != CertEUntrustedRoot && trustStatus != CertEChaining && trustStatus != TrustESubjectNotTrusted)
            throw new Exception($"WinVerifyTrust failed on '{dllPath}' with HRESULT 0x{trustStatus:X8}");
    }

    private static void VerifyPeReferencesAndTokens(string dllPath, string sdkDllPath)
    {
        var forbiddenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Windows.Forms",
            "System.Media",
            "System.Windows.Extensions",
            "System.Drawing.Common"
        };

        var typeRefTokens = new List<int>();
        var memberRefTokens = new List<int>();

        using (var fs = File.OpenRead(dllPath))
        using (var pe = new PEReader(fs))
        {
            MetadataReader mr = pe.GetMetadataReader();
            foreach (AssemblyReferenceHandle handle in mr.AssemblyReferences)
            {
                string refName = mr.GetString(mr.GetAssemblyReference(handle).Name);
                if (forbiddenRefs.Contains(refName))
                    throw new Exception($"Forbidden DH 4 desktop assembly reference in DH 5 plugin: '{refName}'");
            }

            foreach (TypeReferenceHandle tr in mr.TypeReferences)
                typeRefTokens.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(tr));

            foreach (MemberReferenceHandle mem in mr.MemberReferences)
                memberRefTokens.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mem));
        }

        var alc = new IsolatedPluginAlc(sdkDllPath);
        try
        {
            Assembly asm = alc.LoadFromAssemblyPath(dllPath);
            Module mod = asm.ManifestModule;

            int resolvedTypes = 0;
            foreach (int token in typeRefTokens)
            {
                _ = mod.ResolveType(token) ?? throw new Exception($"Unresolved TypeRef token 0x{token:X8}");
                resolvedTypes++;
            }

            int resolvedMembers = 0;
            Type[] structArgs = [typeof(int), typeof(int), typeof(int), typeof(int)];
            Type[] refArgs = [typeof(string), typeof(string), typeof(string), typeof(string)];
            foreach (int token in memberRefTokens)
            {
                MemberInfo? resolved;
                try
                {
                    resolved = mod.ResolveMember(token);
                }
                catch (ArgumentException ex) when (ex.Message.Contains("generic context", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        resolved = mod.ResolveMember(token, structArgs, structArgs);
                    }
                    catch (ArgumentException)
                    {
                        resolved = mod.ResolveMember(token, refArgs, refArgs);
                    }
                }
                if (resolved is null)
                    throw new Exception($"Unresolved MemberRef token 0x{token:X8}");
                resolvedMembers++;
            }

            Console.WriteLine($"PASS Isolated ALC PE inspection: 0 WinForms/System.Media refs, {resolvedTypes} TypeRefs + {resolvedMembers} MemberRefs resolved (0 unresolved)");
        }
        finally
        {
            alc.Unload();
        }
    }

    private static async Task RunFunctionalRuntimeSmokeAsync(string dllPath, string sdkDllPath, string tempWork)
    {
        var alc = new IsolatedPluginAlc(sdkDllPath, isCollectible: false);
        Assembly sdk = alc.LoadFromAssemblyPath(sdkDllPath);
        Assembly pluginAsm = alc.LoadFromAssemblyPath(dllPath);

            // 1. Assembly & Plugin Attribute Identity + 36x36 PNG Icon check
            string? company = pluginAsm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            if (company != ExpectedAuthor)
                throw new Exception($"Expected [AssemblyCompany(\"{ExpectedAuthor}\")], got '{company}'");

            Type iPluginType = sdk.GetType("VoK.Sdk.IPlugin")!;
            Type pluginIdAttrType = sdk.GetType("VoK.Sdk.PluginIdAttribute")!;
            Type pluginNameAttrType = sdk.GetType("VoK.Sdk.PluginNameAttribute")!;
            Type pluginIconAttrType = sdk.GetType("VoK.Sdk.PluginIconAttribute")!;

            Type pluginType = pluginAsm.GetTypes().Single(t => t.IsPublic && !t.IsAbstract && iPluginType.IsAssignableFrom(t));

            object idAttr = pluginType.GetCustomAttribute(pluginIdAttrType) ?? throw new Exception("Missing [PluginId] attribute");
            Guid actualId = (Guid)pluginIdAttrType.GetProperty("Id")!.GetValue(idAttr)!;
            if (actualId != new Guid(ExpectedPluginId))
                throw new Exception($"PluginId mismatch: {actualId}");

            object nameAttr = pluginType.GetCustomAttribute(pluginNameAttrType) ?? throw new Exception("Missing [PluginName] attribute");
            string actualName = (string)pluginNameAttrType.GetProperty("Name")!.GetValue(nameAttr)!;
            if (actualName != ExpectedPluginName)
                throw new Exception($"PluginName mismatch: '{actualName}'");

            object iconAttr = pluginType.GetCustomAttribute(pluginIconAttrType) ?? throw new Exception("Missing [PluginIcon] attribute");
            string iconSuffix = (string)pluginIconAttrType.GetProperty("ResourceName")!.GetValue(iconAttr)!;
            string iconResName = pluginAsm.GetManifestResourceNames()
                .Single(n => n.EndsWith(iconSuffix, StringComparison.OrdinalIgnoreCase));
            using (Stream rs = pluginAsm.GetManifestResourceStream(iconResName)!)
            using (var ms = new MemoryStream())
            {
                rs.CopyTo(ms);
                byte[] png = ms.ToArray();
                if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
                    throw new Exception("Embedded Icon.png is not a valid PNG");
                int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                if (width != 36 || height != 36)
                    throw new Exception($"Embedded Icon.png must be 36x36, got {width}x{height}");
            }

            Console.WriteLine("PASS [PluginId], [PluginName], [AssemblyCompany], and 36x36 PNG [PluginIcon(\"Icon.png\")] verified");

            // 2. Instantiate plugin & verify WindowVisible round-trip (false -> true -> false)
            object plugin = Activator.CreateInstance(pluginType)!;
            PropertyInfo windowVisibleProp = pluginType.GetProperty("WindowVisible")!;
            if ((bool)windowVisibleProp.GetValue(plugin)! != false)
                throw new Exception("WindowVisible must default to false");
            windowVisibleProp.SetValue(plugin, true);
            if ((bool)windowVisibleProp.GetValue(plugin)! != true)
                throw new Exception("WindowVisible did not accept true");
            windowVisibleProp.SetValue(plugin, false);
            if ((bool)windowVisibleProp.GetValue(plugin)! != false)
                throw new Exception("WindowVisible did not round-trip back to false");
            Console.WriteLine("PASS IPlugin.WindowVisible round-trip (false -> true -> false)");

            // 3. Wire dynamic mock DH 5 SDK providers & test async LifeState 0x10000E0C hydration,
            //    Party.OnMemberConnectionChanged, Targetless InvokeHotbar, /hh commands + 3 themes, and Dispose!
            var mock = new Dh5MockHarness(sdk);
            string pluginFolder = Path.Combine(tempWork, "plugin-data");
            Directory.CreateDirectory(pluginFolder);

            pluginType.GetMethod("Initialize")!.Invoke(plugin, new[] { mock.GameDataProxy, mock.EventProviderProxy, pluginFolder });
            object engine = pluginType.GetProperty("Engine", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(plugin)!;
            Type engineType = engine.GetType();
            MethodInfo tickOnce = engineType.GetMethod("RunTickOnceForTestAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            MethodInfo viewMethod = engineType.GetMethod("View")!;
            MethodInfo getSettingsMethod = engineType.GetMethod("GetSettings")!;
            MethodInfo saveSettingsMethod = engineType.GetMethod("SaveSettings")!;

            // Verify /hh chat command handler was registered on OnChatCommand
            if (!mock.ChatCommandHandlers.TryGetValue("hh", out var hhHandler))
                throw new Exception("Dh5HealerEngine did not register 'hh' on eventProvider.OnChatCommand");

            // Scenario A: Self healthy, Tank Incapacitated (-4 HP, LifeState=Incapacitated), Wizard Dead Soulstone (-15 HP, LifeState=Dead)
            await (Task)tickOnce.Invoke(engine, null)!;
            if (mock.LifeStateHydrationCount < 3)
                throw new Exception($"Expected >= 3 UpdateProperties([0x10000E0C]) hydration calls, observed {mock.LifeStateHydrationCount}");

            object view1 = viewMethod.Invoke(engine, null)!;
            string primary1 = (string)view1.GetType().GetProperty("PrimaryText")!.GetValue(view1)!;
            bool isSoul1 = (bool)view1.GetType().GetProperty("IsSoulstone")!.GetValue(view1)!;
            if (primary1 != "DOWN: Tank" || isSoul1)
                throw new Exception($"Expected Incapacitated Tank prioritized over Dead Soulstone, got '{primary1}' (IsSoulstone={isSoul1})");

            // Heal Tank -> Wizard Dead Soulstone should now surface with IsSoulstone=true!
            mock.Tank.HitPoints = 200;
            mock.Tank.LifeStateValue = mock.LifeStateAlive;
            await (Task)tickOnce.Invoke(engine, null)!;
            object view2 = viewMethod.Invoke(engine, null)!;
            string primary2 = (string)view2.GetType().GetProperty("PrimaryText")!.GetValue(view2)!;
            bool isSoul2 = (bool)view2.GetType().GetProperty("IsSoulstone")!.GetValue(view2)!;
            if (primary2 != "DEAD — RAISE Wizard" || !isSoul2)
                throw new Exception($"Expected Dead Soulstone Wizard after Tank healed, got '{primary2}' (IsSoulstone={isSoul2})");

            Console.WriteLine($"PASS Async TickAsync LifeState 0x10000E0C hydration ({mock.LifeStateHydrationCount} calls) & 3-state triage priority");

            // Scenario B: Party.OnMemberConnectionChanged disconnects Wizard -> alert clears!
            await mock.RaisePartyConnectionChangedAsync(mock.Wizard.Id, false);
            await (Task)tickOnce.Invoke(engine, null)!;
            object view3 = viewMethod.Invoke(engine, null)!;
            string sev3 = view3.GetType().GetProperty("Severity")!.GetValue(view3)!.ToString()!;
            if (sev3 != "None")
                throw new Exception($"Disconnected member via Party.OnMemberConnectionChanged must suppress alert, got Severity={sev3}");
            Console.WriteLine("PASS eventProvider.Party.OnMemberConnectionChanged connection tracking");

            // Scenario C: Targetless InvokeHotbar Quick-Heal via /hh heal without calling SelectObject!
            mock.Tank.HitPoints = 60; // 30% low HP
            await (Task)tickOnce.Invoke(engine, null)!;
            object settingsObj = getSettingsMethod.Invoke(engine, null)!;
            settingsObj.GetType().GetProperty("QuickHealHotbar")!.SetValue(settingsObj, 2);
            settingsObj.GetType().GetProperty("QuickHealSlot")!.SetValue(settingsObj, 4);
            saveSettingsMethod.Invoke(engine, new[] { settingsObj });

            int selectBefore = mock.SelectObjectCalls;
            await mock.InvokeChatCommandAsync("hh", "heal");
            if (mock.InvokeHotbarCalls.Count != 1
                || mock.InvokeHotbarCalls[0].Bar != 1u
                || mock.InvokeHotbarCalls[0].Slot != 3u
                || mock.InvokeHotbarCalls[0].TargetId != mock.Tank.Id
                || mock.SelectObjectCalls != selectBefore)
            {
                throw new Exception($"Expected targetless InvokeHotbar(1, 3, {mock.Tank.Id}) with 0 SelectObject calls");
            }
            Console.WriteLine("PASS Targetless InvokeHotbar Quick-Heal (zero target loss)");

            // Scenario D: /hh commands (hp, theme cannith/feywild/eberron, raid, mute, score, status) + JSON persistence
            await mock.InvokeChatCommandAsync("hh", "hp 65");
            await mock.InvokeChatCommandAsync("hh", "theme cannith");
            await mock.InvokeChatCommandAsync("hh", "theme feywild");
            await mock.InvokeChatCommandAsync("hh", "raid");
            await mock.InvokeChatCommandAsync("hh", "mute Tank");
            await mock.InvokeChatCommandAsync("hh", "status");
            await mock.InvokeChatCommandAsync("hh", "score");
            if (mock.PrintedChatLines.Count < 7)
                throw new Exception($"Expected >= 7 Print() chat replies from /hh commands, got {mock.PrintedChatLines.Count}");

            string persistedJson = File.ReadAllText(Path.Combine(pluginFolder, "healers-helper.json"));
            using (var doc = JsonDocument.Parse(persistedJson))
            {
                if (doc.RootElement.GetProperty("HpThresholdPercent").GetInt32() != 65
                    || doc.RootElement.GetProperty("Theme").GetString() != "FeywildTwilight"
                    || !doc.RootElement.GetProperty("RaidHudEnabled").GetBoolean())
                {
                    throw new Exception("DH 5 settings persistence via /hh commands did not write expected values");
                }
            }
            Console.WriteLine("PASS /hh chat commands, all 3 UiTheme switches, and healers-helper.json persistence");

            // Scenario E: SetHotkeyCaptureActive(true) suppresses live hotkey dispatch + deleted entity hydration guard
            MethodInfo setCaptureMethod = engineType.GetMethod("SetHotkeyCaptureActive", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            setCaptureMethod.Invoke(engine, new object[] { true });
            if (OperatingSystem.IsWindows())
            {
                await mock.RaiseSdkKeyDownAsync(0x4B, ctrl: true, shift: true, alt: false); // Ctrl+Shift+K (TogglePlugin)
                await (Task)tickOnce.Invoke(engine, null)!;
                bool stillEnabledAfterCapture = (bool)getSettingsMethod.Invoke(engine, null)!.GetType().GetProperty("PluginEnabled")!.GetValue(getSettingsMethod.Invoke(engine, null)!)!;
                if (!stillEnabledAfterCapture)
                    throw new Exception("SetHotkeyCaptureActive(true) failed to suppress live TogglePlugin hotkey during recording");
            }
            setCaptureMethod.Invoke(engine, new object[] { false });

            // Deleted entity must skip UpdateProperties hydration cleanly without crashing.
            int hydrationsBeforeDeleted = mock.LifeStateHydrationCount;
            mock.Tank.IsDeleted = true;
            await (Task)tickOnce.Invoke(engine, null)!;
            mock.Tank.IsDeleted = false;
            if (mock.LifeStateHydrationCount - hydrationsBeforeDeleted > 2)
                throw new Exception($"Expected deleted Tank entity to skip UpdateProperties hydration (delta={mock.LifeStateHydrationCount - hydrationsBeforeDeleted})");
            Console.WriteLine("PASS SetHotkeyCaptureActive(true) suppression and deleted-entity UpdateProperties guard");

            // Scenario F: ResetUi + Dispose cleanly unregisters /hh
            bool resetOk = (bool)pluginType.GetMethod("ResetUi")!.Invoke(plugin, null)!;
            if (!resetOk)
                throw new Exception("ResetUi() returned false");

            pluginType.GetMethod("Dispose")!.Invoke(plugin, null);
            if (mock.ChatCommandHandlers.ContainsKey("hh"))
                throw new Exception("Dispose() did not remove 'hh' chat command handler");
            Console.WriteLine("PASS ResetUi() and Dispose() cleanup");
    }

    private sealed class IsolatedPluginAlc : AssemblyLoadContext
    {
        private readonly string _sdkPath;

        public IsolatedPluginAlc(string sdkPath, bool isCollectible = true) : base(isCollectible: isCollectible)
        {
            _sdkPath = sdkPath;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "VoK.Sdk", StringComparison.OrdinalIgnoreCase))
                return LoadFromAssemblyPath(_sdkPath);
            return null;
        }
    }

    private sealed class MockActor
    {
        public ulong Id { get; init; }
        public string Name { get; init; } = "";
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public int TempHitPoints { get; set; }
        public int SpellPoints { get; set; }
        public int MaxSpellPoints { get; set; }
        public bool IsDeleted { get; set; }
        public object? LifeStateValue { get; set; }
    }

    private sealed class Dh5MockHarness
    {
        public Assembly Sdk { get; }
        public object LifeStateAlive { get; }
        public object LifeStateIncap { get; }
        public object LifeStateDead { get; }

        public MockActor Self { get; }
        public MockActor Tank { get; }
        public MockActor Wizard { get; }

        public int LifeStateHydrationCount;
        public int SelectObjectCalls;
        public List<(uint Bar, uint Slot, ulong? TargetId)> InvokeHotbarCalls { get; } = new();
        public List<string> PrintedChatLines { get; } = new();
        public Dictionary<string, Delegate> ChatCommandHandlers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Func<ulong, bool, Task>> PartyConnectionHandlers { get; } = new();
        public List<Delegate> KeyDownHandlers { get; } = new();

        public object GameDataProxy { get; }
        public object EventProviderProxy { get; }

        private readonly object _partyProxy;
        private readonly Dictionary<ulong, object> _entityProxies = new();
        private readonly object _partyMembersList;

        public Dh5MockHarness(Assembly sdk)
        {
            Sdk = sdk;
            Type lifeEnum = sdk.GetType("VoK.Sdk.Ddo.Enums.LifeState")!;
            LifeStateAlive = Enum.Parse(lifeEnum, "Alive");
            LifeStateIncap = Enum.Parse(lifeEnum, "Incapacitated");
            LifeStateDead = Enum.Parse(lifeEnum, "Dead");

            // Use valid player IIDs (MSB 0x02 for DDO players so EntityExtensions.IsPlayer is true)
            Self = new MockActor { Id = 0x0200000000000001UL, Name = "HealerSelf", HitPoints = 400, MaxHitPoints = 400, SpellPoints = 1800, MaxSpellPoints = 2000, LifeStateValue = LifeStateAlive };
            Tank = new MockActor { Id = 0x0200000000000002UL, Name = "Tank", HitPoints = -4, MaxHitPoints = 200, TempHitPoints = 150, SpellPoints = 0, MaxSpellPoints = 0, LifeStateValue = LifeStateIncap };
            Wizard = new MockActor { Id = 0x0200000000000003UL, Name = "Wizard", HitPoints = -15, MaxHitPoints = 200, SpellPoints = 400, MaxSpellPoints = 1500, LifeStateValue = LifeStateDead };

            _entityProxies[Self.Id] = CreateEntityProxy(Self);
            _entityProxies[Tank.Id] = CreateEntityProxy(Tank);
            _entityProxies[Wizard.Id] = CreateEntityProxy(Wizard);

            Type partyMemberType = sdk.GetType("VoK.Sdk.Ddo.IPartyMember")!;
            var listType = typeof(List<>).MakeGenericType(partyMemberType);
            var list = Activator.CreateInstance(listType)!;
            var addMethod = listType.GetMethod("Add")!;
            addMethod.Invoke(list, new[] { CreatePartyMemberProxy(Tank) });
            addMethod.Invoke(list, new[] { CreatePartyMemberProxy(Wizard) });
            _partyMembersList = list;

            _partyProxy = CreateProxy(sdk.GetType("VoK.Sdk.Ddo.IPartyEventProvider")!, (m, _) =>
            {
                if (m.Name == "get_OnMemberConnectionChanged")
                    return CreateProxy(m.ReturnType, (rm, rargs) =>
                    {
                        if (rm.Name == "AddHandler" && rargs?[0] is Func<ulong, bool, Task> fn)
                            PartyConnectionHandlers.Add(fn);
                        return null;
                    });
                return CreateNoOpRegistrar(m.ReturnType);
            });

            EventProviderProxy = CreateProxy(sdk.GetType("VoK.Sdk.Ddo.IDdoEventProvider")!, (m, _) =>
            {
                if (m.Name == "get_Party")
                    return _partyProxy;
                if (m.Name == "get_KeyDown")
                    return CreateProxy(m.ReturnType, (rm, rargs) =>
                    {
                        if (rm.Name == "AddHandler" && rargs is { Length: 1 } && rargs[0] is Delegate del)
                            KeyDownHandlers.Add(del);
                        return null;
                    });
                if (m.Name == "get_OnChatCommand")
                    return CreateProxy(m.ReturnType, (rm, rargs) =>
                    {
                        if (rm.Name == "AddHandler" && rargs is { Length: 2 } && rargs[0] is string tok && rargs[1] is Delegate del)
                        {
                            ChatCommandHandlers[tok] = del;
                            return true;
                        }
                        if (rm.Name == "RemoveHandler" && rargs is { Length: 1 } && rargs[0] is string remTok)
                        {
                            ChatCommandHandlers.Remove(remTok);
                        }
                        return null;
                    });
                return CreateNoOpRegistrar(m.ReturnType);
            });

            GameDataProxy = CreateProxy(sdk.GetType("VoK.Sdk.Ddo.IDdoGameDataProvider")!, (m, args) =>
            {
                switch (m.Name)
                {
                    case "GetCurrentCharacterId":
                        return (ulong?)Self.Id;
                    case "GetCurrentCharacterAsync":
                        return FromResultTyped(sdk.GetType("VoK.Sdk.IEntity")!, _entityProxies[Self.Id]);
                    case "GetCurrentCharacterNameAsync":
                        return Task.FromResult(Self.Name);
                    case "InTownAsync":
                        return Task.FromResult<bool?>(false);
                    case "get_Party":
                        return CreateProxy(sdk.GetType("VoK.Sdk.Ddo.IPartyProvider")!, (pm, _) => pm.Name switch
                        {
                            "GetMembers" => _partyMembersList,
                            _ => DefaultFor(pm.ReturnType)
                        });
                    case "GetPartyMembers":
                        return _partyMembersList;
                    case "GetEntityAsync":
                        ulong eid = (ulong)args![0]!;
                        _entityProxies.TryGetValue(eid, out object? found);
                        return FromResultTyped(sdk.GetType("VoK.Sdk.IEntity")!, found);
                    case "GetSelectedObjectId":
                        return (ulong?)0x0300000000000999UL; // Hard-targeted raid boss
                    case "SelectObject":
                        SelectObjectCalls++;
                        return null;
                    case "InvokeHotbar":
                        InvokeHotbarCalls.Add(((uint)args![0]!, (uint)args[1]!, (ulong?)args[2]));
                        return true;
                    case "Print":
                        PrintedChatLines.Add((string)(args![0] ?? ""));
                        return true;
                    case "GetGameWindowHandle":
                        return IntPtr.Zero;
                    case "get_GameProcess":
                    case "get_Keymap":
                        return null;
                    case "GetCharacterNameFromId":
                        ulong cid = (ulong)args![0]!;
                        return cid == Tank.Id ? Tank.Name : cid == Wizard.Id ? Wizard.Name : Self.Name;
                    default:
                        return DefaultFor(m.ReturnType);
                }
            });
        }

        public async Task RaisePartyConnectionChangedAsync(ulong id, bool connected)
        {
            foreach (var h in PartyConnectionHandlers)
                await h(id, connected);
        }

        public async Task RaiseSdkKeyDownAsync(int vk, bool ctrl, bool shift, bool alt)
        {
            Type hkType = Sdk.GetType("VoK.Sdk.Events.HotKeyEventArgs")!;
            MethodInfo fromKeys = hkType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .First(mi => mi.Name == "FromKeys" && mi.GetParameters().Length == 6);
            object args = fromKeys.Invoke(null, new object[] { vk, ctrl, shift, alt, false, true })!;
            foreach (var h in KeyDownHandlers)
            {
                await (Task)h.DynamicInvoke(args)!;
            }
        }

        public async Task InvokeChatCommandAsync(string token, string rest)
        {
            if (!ChatCommandHandlers.TryGetValue(token, out var del))
                throw new Exception($"No handler registered for /{token}");
            Type cmdType = Sdk.GetType("VoK.Sdk.Ddo.IChatCommand")!;
            object cmdProxy = CreateProxy(cmdType, (m, _) => m.Name switch
            {
                "get_Token" => token,
                "get_Rest" => rest,
                _ => null
            });
            var task = (Task)del.DynamicInvoke(cmdProxy)!;
            await task;
        }

        private object CreatePartyMemberProxy(MockActor actor)
        {
            return CreateProxy(Sdk.GetType("VoK.Sdk.Ddo.IPartyMember")!, (m, _) => m.Name switch
            {
                "get_Id" => actor.Id,
                "get_Name" => actor.Name,
                "get_HitPoints" => actor.HitPoints,
                "get_MaxHitPoints" => actor.MaxHitPoints,
                "get_SpellPoints" => actor.SpellPoints,
                "get_MaxSpellPoints" => actor.MaxSpellPoints,
                "get_IsLeader" => false,
                _ => DefaultFor(m.ReturnType)
            });
        }

        private object CreateEntityProxy(MockActor actor)
        {
            Type propCollType = Sdk.GetType("VoK.Sdk.Properties.IPropertyCollection")!;
            Type enumPropType = Sdk.GetType("VoK.Sdk.Properties.IEnumProperty")!;
            Type iPropType = Sdk.GetType("VoK.Sdk.Properties.IProperty")!;

            object MakeLifeProp() => CreateProxy(enumPropType, (pm, _) => pm.Name switch
            {
                "get_PropertyId" => LifeStatePropertyId,
                "get_UInt32Value" => Convert.ToUInt32(actor.LifeStateValue!),
                "get_RawValue" => actor.LifeStateValue,
                "get_Value" => actor.LifeStateValue,
                _ => DefaultFor(pm.ReturnType)
            });

            object propColl = CreateProxy(propCollType, (cm, cargs) =>
            {
                if (cm.Name == "GetProperty" && cargs is { Length: 1 } && (uint)cargs[0]! == LifeStatePropertyId)
                    return MakeLifeProp();
                if (cm.Name == "get_Properties")
                {
                    var listType = typeof(List<>).MakeGenericType(iPropType);
                    var list = Activator.CreateInstance(listType)!;
                    listType.GetMethod("Add")!.Invoke(list, new[] { MakeLifeProp() });
                    return list;
                }
                return DefaultFor(cm.ReturnType);
            });

            return CreateProxy(Sdk.GetType("VoK.Sdk.IEntity")!, (m, args) => m.Name switch
            {
                "get_InstanceId" => actor.Id,
                "get_Name" => actor.Name,
                "get_IsDeleted" => actor.IsDeleted,
                "get_HitPoints_Current" => (int?)actor.HitPoints,
                "get_HitPoints_Max" => (int?)actor.MaxHitPoints,
                "get_HitPoints_Temp" => (int?)actor.TempHitPoints,
                "get_SpellPoints_Current" => (int?)actor.SpellPoints,
                "get_SpellPoints_Max" => (int?)actor.MaxSpellPoints,
                "get_InstanceProperties" => propColl,
                "GetProperty" when args is { Length: 1 } && (uint)args[0]! == LifeStatePropertyId => MakeLifeProp(),
                "UpdateProperties" => HandleUpdateProperties(args),
                _ => DefaultFor(m.ReturnType)
            });
        }

        private Task HandleUpdateProperties(object?[]? args)
        {
            if (args is { Length: 1 } && args[0] is IEnumerable<uint> ids && ids.Contains(LifeStatePropertyId))
                LifeStateHydrationCount++;
            return Task.CompletedTask;
        }

        private static object CreateNoOpRegistrar(Type registrarType) =>
            CreateProxy(registrarType, (m, _) => DefaultFor(m.ReturnType));

        private static object FromResultTyped(Type resultType, object? value)
        {
            MethodInfo mi = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType);
            return mi.Invoke(null, new[] { value })!;
        }

        private static object? DefaultFor(Type t)
        {
            if (t == typeof(void)) return null;
            if (t == typeof(Task)) return Task.CompletedTask;
            if (t.IsValueType) return Activator.CreateInstance(t);
            return null;
        }

        private static object CreateProxy(Type interfaceType, Func<MethodInfo, object?[]?, object?> handler)
        {
            MethodInfo create = typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), Type.EmptyTypes)!
                .MakeGenericMethod(interfaceType, typeof(DynamicMockProxy));
            object proxy = create.Invoke(null, null)!;
            ((DynamicMockProxy)proxy).Handler = handler;
            return proxy;
        }
    }

    public class DynamicMockProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || Handler is null) return null;
            return Handler(targetMethod, args);
        }
    }

    // Minimal Windows WinVerifyTrust P/Invoke (only executed when OperatingSystem.IsWindows()).
    private static int WinVerifyTrustFile(string filePath)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        IntPtr filePtr = Marshal.StringToCoTaskMemUni(filePath);
        IntPtr fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x00001000 // WTD_CACHE_ONLY_URL_RETRIEVAL
            };

            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfoPtr);
            Marshal.FreeCoTaskMem(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid pgActionID, [In, Out] ref WinTrustData pWVTData);
}
