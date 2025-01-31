namespace Testcontainers.Pulsar;

/// <inheritdoc cref="DockerContainer" />
[PublicAPI]
public sealed class PulsarContainer : DockerContainer
{
    private readonly PulsarConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulsarContainer" /> class.
    /// </summary>
    /// <param name="configuration">The container configuration.</param>
    public PulsarContainer(PulsarConfiguration configuration)
        : base(configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the Pulsar broker address.
    /// </summary>
    /// <returns>The Pulsar broker address.</returns>
    public string GetBrokerAddress()
    {
        return new UriBuilder("pulsar", Hostname, GetMappedPublicPort(PulsarBuilder.PulsarBrokerDataPort)).ToString();
    }

    /// <summary>
    /// Gets the Pulsar web service address.
    /// </summary>
    /// <returns>The Pulsar web service address.</returns>
    public string GetServiceAddress()
    {
        return new UriBuilder(Uri.UriSchemeHttp, Hostname, GetMappedPublicPort(PulsarBuilder.PulsarWebServicePort)).ToString();
    }

    /// <summary>
    /// Creates an authentication token.
    /// </summary>
    /// <param name="expire">The time after the authentication token expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the authentication token has been created.</returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<string> CreateAuthenticationTokenAsync(TimeSpan expire = default, CancellationToken ct = default)
    {
        int secondsToMilliseconds;

        if (_configuration.AuthenticationEnabled.HasValue && !_configuration.AuthenticationEnabled.Value)
        {
            throw new ArgumentException("Failed to create token. Authentication is not enabled.");
        }

        if (_configuration.Image.Tag.StartsWith("3.2") || _configuration.Image.Tag.StartsWith("latest"))
        {
            Logger.LogWarning("The 'apachepulsar/pulsar:3.2.?' image contains a regression. The expiry time is converted to the wrong unit of time: https://github.com/apache/pulsar/issues/22811.");
            secondsToMilliseconds = 1000;
        }
        else
        {
            secondsToMilliseconds = 1;
        }

        var command = new[]
        {
            "bin/pulsar",
            "tokens",
            "create",
            "--secret-key",
            PulsarBuilder.SecretKeyFilePath,
            "--subject",
            PulsarBuilder.Username,
            "--expiry-time",
            $"{secondsToMilliseconds * expire.TotalSeconds}s",
        };

        var tokensResult = await ExecAsync(command, ct)
            .ConfigureAwait(false);

        if (tokensResult.ExitCode != 0)
        {
            throw new ArgumentException($"Failed to create token. Command returned a non-zero exit code: {tokensResult.Stderr}.");
        }

        return tokensResult.Stdout;
    }

    /// <summary>
    /// Copies the Pulsar startup script to the container.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the startup script has been copied.</returns>
    internal Task CopyStartupScriptAsync(CancellationToken ct = default)
    {
        var startupScript = new StringWriter();
        startupScript.NewLine = "\n";
        startupScript.WriteLine("#!/bin/bash");

        if (_configuration.AuthenticationEnabled.HasValue && _configuration.AuthenticationEnabled.Value)
        {
            startupScript.WriteLine("bin/pulsar tokens create-secret-key --output " + PulsarBuilder.SecretKeyFilePath);
            startupScript.WriteLine("export brokerClientAuthenticationParameters=token:$(bin/pulsar tokens create --secret-key $PULSAR_PREFIX_tokenSecretKey --subject $superUserRoles)");
            startupScript.WriteLine("export CLIENT_PREFIX_authParams=$brokerClientAuthenticationParameters");
            startupScript.WriteLine("bin/apply-config-from-env.py conf/standalone.conf");
            startupScript.WriteLine("bin/apply-config-from-env-with-prefix.py CLIENT_PREFIX_ conf/client.conf");
        }

        startupScript.Write("bin/pulsar standalone");

        if (_configuration.FunctionsWorkerEnabled.HasValue && !_configuration.FunctionsWorkerEnabled.Value)
        {
            startupScript.Write(" --no-functions-worker");
            startupScript.Write(" --no-stream-storage");
        }

        return CopyAsync(Encoding.Default.GetBytes(startupScript.ToString()), PulsarBuilder.StartupScriptFilePath, Unix.FileMode755, ct);
    }
}