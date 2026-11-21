using HomeGed.Api;
using HomeGed.Api.Hosting;

if (args.Contains(SelfHealthCheck.Argument))
{
    return await SelfHealthCheck.RunAsync();
}

await HomeGedApp.Create(args).RunAsync();
return 0;
