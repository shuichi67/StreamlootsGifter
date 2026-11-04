using System.Security.Cryptography;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: UpdateSigningTool <package.zip> <private-key.pem> <pinned-public-key.pem>");
    return 2;
}

var archivePath = Path.GetFullPath(args[0]);
var privateKeyPath = Path.GetFullPath(args[1]);
var pinnedPublicKeyPath = Path.GetFullPath(args[2]);
if (!File.Exists(archivePath) || !File.Exists(privateKeyPath) || !File.Exists(pinnedPublicKeyPath))
{
    Console.Error.WriteLine("The update archive or signing key file was not found.");
    return 2;
}

using var signer = RSA.Create();
using var pinnedKey = RSA.Create();
try
{
    signer.ImportFromPem(File.ReadAllText(privateKeyPath));
    pinnedKey.ImportFromPem(File.ReadAllText(pinnedPublicKeyPath));
}
catch (CryptographicException ex)
{
    Console.Error.WriteLine($"Could not read the update signing key: {ex.Message}");
    return 2;
}

if (signer.KeySize != 3072)
{
    Console.Error.WriteLine("The update signing key must be 3072-bit RSA.");
    return 2;
}

var signerPublicKey = signer.ExportSubjectPublicKeyInfo();
var pinnedPublicKey = pinnedKey.ExportSubjectPublicKeyInfo();
if (signerPublicKey.Length != pinnedPublicKey.Length ||
    !CryptographicOperations.FixedTimeEquals(signerPublicKey, pinnedPublicKey))
{
    Console.Error.WriteLine("The private signing key does not match the public key pinned in the app. No release signature was created.");
    return 1;
}

await using var archive = File.OpenRead(archivePath);
var archiveHash = await SHA256.HashDataAsync(archive);
var signature = signer.SignHash(archiveHash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
await File.WriteAllTextAsync(archivePath + ".sig", Convert.ToBase64String(signature), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Private key matches the app's pinned public key; package signature created for {Path.GetFileName(archivePath)}.");
return 0;
