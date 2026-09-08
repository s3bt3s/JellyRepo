using Jellyfin.Plugin.SimklWatched.API;
using Jellyfin.Plugin.SimklWatched.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SimklWatched
{
    /// <inheritdoc />
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<SimklApi>();
            serviceCollection.AddHostedService<WatchedWorker>();
        }
    }
}
