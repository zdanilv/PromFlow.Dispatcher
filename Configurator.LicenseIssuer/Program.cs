using Configurator.LicenseIssuer;

Environment.ExitCode = await new LicenseIssuerCli(Console.In, Console.Out, Console.Error)
    .RunAsync(args)
    .ConfigureAwait(false);
