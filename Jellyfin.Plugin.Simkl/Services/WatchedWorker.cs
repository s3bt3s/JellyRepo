using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SimklWatched.API;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SimklWatched.Services;

/// <summary>Persistent delivery of manual watched marks only.</summary>
public sealed class WatchedWorker : BackgroundService
{
    private readonly IUserDataManager _users;
    private readonly ILibraryManager _library;
    private readonly SimklApi _api;
    private readonly ILogger<WatchedWorker> _log;
    private readonly string _path;
    private readonly object _gate = new object();
    private Dictionary<string, Delivery> _deliveries = new Dictionary<string, Delivery>();

    /// <summary>Initializes a new instance of the <see cref="WatchedWorker"/> class.</summary>
    /// <param name="users">User data events.</param>
    /// <param name="library">Media library.</param>
    /// <param name="api">SIMKL API.</param>
    /// <param name="paths">Server paths.</param>
    /// <param name="log">Logger.</param>
    public WatchedWorker(IUserDataManager users, ILibraryManager library, SimklApi api, IApplicationPaths paths, ILogger<WatchedWorker> log)
    {
        _users = users;
        _library = library;
        _api = api;
        _log = log;
        _path = Path.Combine(paths.PluginConfigurationsPath, "SimklWatched.deliveries.json");
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_path))
        {
            _deliveries = JsonSerializer.Deserialize<Dictionary<string, Delivery>>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Invalid SIMKL delivery state");
        }

        _users.UserDataSaved += OnSaved;
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _users.UserDataSaved -= OnSaved;
        return base.StopAsync(cancellationToken);
    }

    private static bool ShouldQueue(UserDataSaveEventArgs e)
    {
        return e.SaveReason == UserDataSaveReason.TogglePlayed && e.UserData.Played
            && (e.Item is Movie || e.Item is Episode);
    }

    private void OnSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (!ShouldQueue(e))
        {
            return;
        }

        var config = SimklPlugin.Instance?.Configuration.GetByGuid(e.UserId);
        if (config == null || string.IsNullOrEmpty(config.UserToken)
            || (e.Item is Movie ? !config.ScrobbleMovies : !config.ScrobbleShows))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var key = $"{e.UserId:N}:{e.Item.Id:N}";
                if (_deliveries.ContainsKey(key))
                {
                    return;
                }

                _deliveries.Add(key, new Delivery { UserId = e.UserId, ItemId = e.Item.Id });
                try
                {
                    Save();
                }
                catch
                {
                    _deliveries.Remove(key);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not persist watched mark for {ItemId}", e.Item.Id);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_deliveries));
        File.Move(_path + ".tmp", _path, true);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            KeyValuePair<string, Delivery>[] pending;
            lock (_gate)
            {
                pending = _deliveries.Where(x => !x.Value.Sent && x.Value.NextAttempt <= DateTime.UtcNow).ToArray();
            }

            foreach (var entry in pending)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var sent = false;
                try
                {
                    var config = SimklPlugin.Instance?.Configuration.GetByGuid(entry.Value.UserId);
                    var item = _library.GetItemById(entry.Value.ItemId);
                    if (item == null || config == null || string.IsNullOrEmpty(config.UserToken))
                    {
                        throw new InvalidOperationException("Media or authorization unavailable");
                    }

                    if (item is Movie ? !config.ScrobbleMovies : !config.ScrobbleShows)
                    {
                        continue;
                    }

                    var dto = new BaseItemDto
                    {
                        Name = item.Name,
                        OriginalTitle = item.OriginalTitle ?? item.Name,
                        ProductionYear = item.ProductionYear,
                        ProviderIds = new Dictionary<string, string>(item.ProviderIds),
                    };
                    if (item is Episode episode)
                    {
                        var series = episode.Series ?? throw new InvalidOperationException("Series unavailable");
                        dto.SeriesName = series.Name;
                        dto.ProductionYear = series.ProductionYear;
                        dto.ProviderIds = new Dictionary<string, string>(series.ProviderIds);
                        dto.ParentIndexNumber = episode.ParentIndexNumber;
                        dto.IndexNumber = episode.IndexNumber;
                        if (!dto.ParentIndexNumber.HasValue || !dto.IndexNumber.HasValue)
                        {
                            throw new InvalidOperationException("Season or episode number missing");
                        }
                    }

                    sent = await _api.SendManualWatched(dto, item is Episode, config.UserToken).ConfigureAwait(false);
                    if (!sent)
                    {
                        _log.LogWarning("SIMKL did not confirm {ItemId}", item.Id);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("SIMKL delivery failed for {ItemId}: {ErrorType}", entry.Value.ItemId, ex.GetType().Name);
                }

                lock (_gate)
                {
                    entry.Value.Sent = sent;
                    entry.Value.Attempts++;
                    entry.Value.NextAttempt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, entry.Value.Attempts))));
                    Save();
                }

                if (sent)
                {
                    _log.LogInformation("SIMKL watched confirmed for {ItemId}", entry.Value.ItemId);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Persisted record without tokens.</summary>
    public sealed class Delivery
    {
        /// <summary>Gets or sets the user.</summary>
        public Guid UserId { get; set; }

        /// <summary>Gets or sets the media.</summary>
        public Guid ItemId { get; set; }

        /// <summary>Gets or sets a value indicating whether delivery succeeded.</summary>
        public bool Sent { get; set; }

        /// <summary>Gets or sets the attempts.</summary>
        public int Attempts { get; set; }

        /// <summary>Gets or sets the retry time.</summary>
        public DateTime NextAttempt { get; set; }
    }
}
