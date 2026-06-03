using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Opc.Ua;

namespace Configurator.Infrastructure.OpcUa.Runtime;

/// <summary>
/// Создает конфигурацию OPC UA SDK для клиентской или серверной роли из настроек приложения.
/// </summary>
public sealed class OpcUaApplicationConfigurationFactory
{
    /// <summary>
    /// Создает конфигурацию OPC UA SDK для клиентской роли.
    /// </summary>
    public ApplicationConfiguration CreateClient(OpcUaOptions options)
    {
        var configuration = CreateBaseConfiguration(
            options.Client.ApplicationName,
            options.Client.ApplicationUri,
            options.Client.ProductUri,
            ApplicationType.Client);

        configuration.ClientConfiguration = new ClientConfiguration
        {
            DefaultSessionTimeout = options.Client.SessionTimeoutMilliseconds,
            MinSubscriptionLifetime = 10000
        };

        return configuration;
    }

    /// <summary>
    /// Создает конфигурацию OPC UA SDK для встроенного сервера.
    /// </summary>
    public ApplicationConfiguration CreateServer(OpcUaOptions options)
    {
        var configuration = CreateBaseConfiguration(
            options.Server.ApplicationName,
            options.Server.ApplicationUri,
            options.Server.ProductUri,
            ApplicationType.Server);

        configuration.ServerConfiguration = new ServerConfiguration
        {
            BaseAddresses = { options.Server.EndpointUrl },
            SecurityPolicies =
            {
                new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            },
            MaxSessionCount = 100,
            MinSessionTimeout = 10000,
            MaxSessionTimeout = 3600000,
            MaxSubscriptionCount = 100,
            MaxMessageQueueSize = 100,
            MaxNotificationQueueSize = 100,
            MaxPublishRequestCount = 20
        };

        return configuration;
    }

    /// <summary>
    /// Заполняет общие секции SDK-конфигурации приложения.
    /// </summary>
    private static ApplicationConfiguration CreateBaseConfiguration(
        string applicationName,
        string applicationUri,
        string productUri,
        ApplicationType applicationType)
        => new()
        {
            ApplicationName = applicationName,
            ApplicationUri = applicationUri,
            ProductUri = productUri,
            ApplicationType = applicationType,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = CreateApplicationCertificateIdentifier(
                    applicationName,
                    applicationUri,
                    applicationType),
                AutoAcceptUntrustedCertificates = false,
                AddAppCertToTrustedStore = false,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 0
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 1_048_576,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300000,
                SecurityTokenLifetime = 3600000
            },
            CertificateValidator = new CertificateValidator(DefaultTelemetry.Create(_ => { }))
        };

    /// <summary>
    /// Создает описание сертификата приложения, если сертификаты включены.
    /// </summary>
    private static CertificateIdentifier CreateApplicationCertificateIdentifier(
        string applicationName,
        string applicationUri,
        ApplicationType applicationType)
    {
        var identifier = new CertificateIdentifier();

        if (applicationType == ApplicationType.Server)
        {
            identifier.Certificate = CreateEphemeralServerCertificate(applicationName, applicationUri);
        }

        return identifier;
    }

    /// <summary>
    /// Создает временный self-signed сертификат для локального сервера.
    /// </summary>
    private static X509Certificate2 CreateEphemeralServerCertificate(
        string applicationName,
        string applicationUri)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={applicationName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddUri(new Uri(applicationUri));
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.EphemeralKeySet);
    }
}
