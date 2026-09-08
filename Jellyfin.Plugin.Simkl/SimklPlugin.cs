using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SimklWatched.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SimklWatched
{
    /// <summary>
    /// SIMKL tracker.
    /// </summary>
    public class SimklPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklPlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public SimklPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current instance of the plugin.
        /// </summary>
        public static SimklPlugin? Instance { get; private set; }

        /// <inheritdoc />
        public override Guid Id => new Guid("29C23B82-7B4A-4F24-97D8-8567A618BCC3");

        /// <inheritdoc />
        public override string Name => "Simkl Watched";

        /// <inheritdoc />
        public override string Description => "Send manual Jellyfin watched marks to SIMKL.";

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };
        }
    }
}