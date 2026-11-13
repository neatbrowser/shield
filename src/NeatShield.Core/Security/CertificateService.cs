using System.Security.Cryptography.X509Certificates;
using NeatShield.Core.Logging;
using NeatShield.Core.Settings;
using Titanium.Web.Proxy.Network;

namespace NeatShield.Core.Security;

public sealed record CertificateStatus(bool Exists, bool Trusted, string? Thumbprint, DateTime? Expires);

/// <summary>
/// The local root certificate NeatShield uses to open HTTPS pages for filtering. It is created on this computer,
/// its private key never leaves it and is saved with a password that only this Windows user can read (DPAPI).
/// </summary>
public sealed class CertificateService
{
    private const string CaName = "NeatShield Local Root CA";
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly CertificateManager _cm;
    private readonly object _gate = new();

    public CertificateService(AppPaths paths, SettingsStore settings, CertificateManager certificateManager)
    {
        _paths = paths;
        _settings = settings;
        _cm = certificateManager;
    }

    /// <summary>Loads the certificate, or creates it the first time. Safe to call again.</summary>
    public void EnsureCreated()
    {
        lock (_gate)
        {
            var secret = SecretStore.Unprotect(_settings.Current.CaSecret);
            if (secret is null)
            {
                // First run, or the saved password cannot be read (other Windows user, damaged). The old key is useless: start over.
                if (File.Exists(_paths.CaKeyFile))
                {
                    Log.Warn("CA key password unreadable; creating a new root certificate");
                    TryDelete(_paths.CaKeyFile);
                }

                secret = SecretStore.NewSecret();
                var protectedSecret = SecretStore.Protect(secret);
                _settings.Update(s => s.CaSecret = protectedSecret);
            }

            _cm.RootCertificateName = CaName;
            _cm.RootCertificateIssuerName = "NeatShield";
            _cm.PfxFilePath = _paths.CaKeyFile;
            _cm.PfxPassword = secret;
            _cm.OverwritePfxFile = false;

            if (!_cm.CreateRootCertificate(persistToFile: true) || _cm.RootCertificate is null)
            {
                throw new InvalidOperationException("The root certificate could not be created or loaded.");
            }

            ExportPublicPart();
        }
    }

    public CertificateStatus GetStatus()
    {
        lock (_gate)
        {
            var cert = _cm.RootCertificate;
            if (cert is null) return new CertificateStatus(false, false, null, null);
            return new CertificateStatus(true, IsInUserRootStore(cert.Thumbprint), cert.Thumbprint, cert.NotAfter);
        }
    }

    /// <summary>Adds the certificate to this user's trusted roots. Windows asks the user to confirm.</summary>
    public void Trust()
    {
        lock (_gate)
        {
            EnsureCreated();
            _cm.TrustRootCertificate(machineTrusted: false);
        }
    }

    public void Untrust()
    {
        lock (_gate)
        {
            _cm.RemoveTrustedRootCertificate(machineTrusted: false);
        }
    }

    private static bool IsInUserRootStore(string thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read the certificate store: {ex.Message}");
            return false;
        }
    }

    private void ExportPublicPart()
    {
        try
        {
            var cert = _cm.RootCertificate;
            if (cert is null) return;
            File.WriteAllBytes(_paths.CaCertFile, cert.Export(X509ContentType.Cert));
        }
        catch (Exception ex)
        {
            Log.Warn($"could not export the public certificate: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { /* ignore */ }
    }
}
