using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Flurl.Http;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.EventListeners;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Settings;
using Microsoft.Extensions.Logging;
using static Izzy_Moonbot.Settings.ScheduledJobRepeatType;
using static Izzy_Moonbot.Adapters.IIzzyClient;

namespace Izzy_Moonbot.Service;

/// <summary>
/// Service responsible for the management and execution of scheduled tasks which need to be non-volatile.
/// </summary>
public class ScheduleService
{
    private readonly Config _config;
    private readonly LoggingService _logger;
    private readonly ModService _mod;
    private readonly ModLoggingService _modLogging;
    private GeneralStorage _generalStorage;

    private readonly List<ScheduledJob> _scheduledJobs;

    private bool _alreadyInitiated;

    public ScheduleService(Config config, ModService mod, ModLoggingService modLogging, LoggingService logger, GeneralStorage generalStorage,
        List<ScheduledJob> scheduledJobs)
    {
        _config = config;
        _logger = logger;
        _mod = mod;
        _modLogging = modLogging;
        _generalStorage = generalStorage;
        _scheduledJobs = scheduledJobs;
    }

    public void RegisterEvents(IIzzyClient client)
    {
        client.ButtonExecuted += async (component) => await DiscordHelper.LeakOrAwaitTask(ButtonEvent(component));
    }

    public void BeginUnicycleLoop(IIzzyClient client)
    {
        if (_alreadyInitiated) return;
        _alreadyInitiated = true;
        UnicycleLoop(client);
    }

    private void UnicycleLoop(IIzzyClient client)
    {
        // Core event loop. Executes every Config.UnicycleInterval seconds.
        Task.Run(async () =>
        {
            await Task.Delay(_config.UnicycleInterval);
            
            // Run unicycle.
            try
            {
                await Unicycle(client);
            }
            catch (Exception exception)
            {
                _logger.Log($"{exception.Message}{Environment.NewLine}{exception.StackTrace}", level: LogLevel.Error);
            }

            // Call self
            UnicycleLoop(client);
        });
    }

    public async Task Unicycle(IIzzyClient client)
    {
        var scheduledJobsToExecute = new List<ScheduledJob>();

        foreach (var job in _scheduledJobs)
        {
            if (job.ExecuteAt.ToUnixTimeMilliseconds() <= DateTimeHelper.UtcNow.ToUnixTimeMilliseconds())
            {
                scheduledJobsToExecute.Add(job);
            }
        }

        foreach (var job in scheduledJobsToExecute)
        {
            _logger.Log($"Executing {job.Action.Type} job {job.Id} since it was scheduled to execute at {job.ExecuteAt:F}", level: LogLevel.Debug);

            try
            {
                if (client.GetGuild(DiscordHelper.DefaultGuild()) is not IIzzyGuild defaultGuild)
                    throw new InvalidOperationException("Failed to get default guild");

                // Do processing here I guess!
                switch (job.Action)
                {
                    case ScheduledRoleRemovalJob roleRemovalJob:
                        await Unicycle_RemoveRole(roleRemovalJob, defaultGuild);
                        break;
                    case ScheduledRoleAdditionJob roleAdditionJob:
                        await Unicycle_AddRole(roleAdditionJob, defaultGuild);
                        break;
                    case ScheduledUnbanJob unbanJob:
                        await Unicycle_Unban(unbanJob, defaultGuild, client);
                        break;
                    case ScheduledEchoJob echoJob:
                        await Unicycle_Echo(echoJob, defaultGuild, client, job.RepeatType, job.Id);
                        break;
                    case ScheduledBannerRotationJob bannerRotationJob:
                        await Unicycle_BannerRotation(bannerRotationJob, defaultGuild, client);
                        break;
                    default:
                        throw new NotSupportedException($"{job.Action.GetType().Name} is currently not supported.");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(
                    $"Scheduled job threw an exception when trying to execute!{Environment.NewLine}" +
                    $"Type: {ex.GetType().Name}{Environment.NewLine}" +
                    $"Message: {ex.Message}{Environment.NewLine}" +
                    $"Job: {job}{Environment.NewLine}" +
                    $"Stack Trace: {ex.StackTrace}");
            }

            await DeleteOrRepeatScheduledJob(job);
        }
    }

    public ScheduledJob? GetScheduledJob(string id)
    {
        return _scheduledJobs.SingleOrDefault(job => job.Id == id);
    }

    public ScheduledJob? GetScheduledJob(Func<ScheduledJob, bool> predicate)
    {
        return _scheduledJobs.SingleOrDefault(predicate);
    }
    
    public List<ScheduledJob> GetScheduledJobs()
    {
        return _scheduledJobs.ToList();
    }

    public List<ScheduledJob> GetScheduledJobs(Func<ScheduledJob, bool> predicate)
    {
        return _scheduledJobs.Where(predicate).ToList();
    }

    public async Task CreateScheduledJob(ScheduledJob job)
    {
        if (job.RepeatType == ScheduledJobRepeatType.Relative && (job.LastExecutedAt ?? job.CreatedAt) >= job.ExecuteAt)
            throw new ArgumentException($"CreateScheduledJob() was passed a relative repeating job with non-positive interval: {job.ToDiscordString()}");

        _scheduledJobs.Add(job);
        await FileHelper.SaveScheduleAsync(_scheduledJobs);
    }

    public async Task ModifyScheduledJob(string id, ScheduledJob job)
    {
        if (job.RepeatType == ScheduledJobRepeatType.Relative && (job.LastExecutedAt ?? job.CreatedAt) >= job.ExecuteAt)
            throw new ArgumentException($"ModifyScheduledJob() was passed a relative repeating job with non-positive interval: {job.ToDiscordString()}");

        _scheduledJobs[_scheduledJobs.IndexOf(_scheduledJobs.First(altJob => altJob.Id == id))] = job;
        await FileHelper.SaveScheduleAsync(_scheduledJobs);
    }

    public async Task DeleteScheduledJob(ScheduledJob job)
    {
        var result = _scheduledJobs.Remove(job);
        if (!result)
            throw new NullReferenceException("The scheduled job provided was not found in the scheduled job list.");
        await FileHelper.SaveScheduleAsync(_scheduledJobs);
    }

    private async Task DeleteOrRepeatScheduledJob(ScheduledJob job)
    {
        if (job.RepeatType != None)
        {
            // Modify job to allow repeatability.
            var taskIndex = _scheduledJobs.FindIndex(scheduledJob => scheduledJob.Id == job.Id);
            
            // Get LastExecutedAt, or CreatedAt if former is null as well as the execution time.
            var creationAt = job.LastExecutedAt ?? job.CreatedAt;
            var executeAt = job.ExecuteAt;

            // RepeatType is checked against null above.
            switch (job.RepeatType)
            {
                case Relative:
                    // Get the offset.
                    var repeatEvery = executeAt - creationAt;
            
                    // Get the timestamp of next execution.
                    var nextExecuteAt = executeAt + repeatEvery;
            
                    // Set previous execution time and new execution time
                    job.LastExecutedAt = executeAt;
                    job.ExecuteAt = nextExecuteAt;
                    break;
                case Daily:
                    // Just add a single day to the execute at time lol
                    job.LastExecutedAt = executeAt;
                    job.ExecuteAt = executeAt.AddDays(1);
                    break;
                case Weekly:
                    // Add 7 days to the execute at time
                    job.LastExecutedAt = executeAt;
                    job.ExecuteAt = executeAt.AddDays(7);
                    break;
                case Yearly:
                    // Add a year to the execute at time
                    job.LastExecutedAt = executeAt;
                    job.ExecuteAt = executeAt.AddYears(1);
                    break;
            }

            // Update the task and save
            _scheduledJobs[taskIndex] = job;
            await FileHelper.SaveScheduleAsync(_scheduledJobs);

            return;
        }

        await DeleteScheduledJob(job);
    }
    
    // Executors for different types.
    private async Task Unicycle_AddRole(ScheduledRoleAdditionJob job, IIzzyGuild guild)
    {
        var role = guild.GetRole(job.Role);
        var user = guild.GetUser(job.User);
        if (role == null || user == null) return;

        var reason = job.Reason;
        
        _logger.Log(
            $"Adding {role.Name} ({role.Id}) to {user.Username}#{user.Discriminator} ({user.Id})", level: LogLevel.Debug);
        
        await _mod.AddRole(user, role.Id, reason);
        await _modLogging.CreateModLog(guild)
            .SetContent(
                $"Gave <@&{role.Id}> to <@{user.Id}> (`{user.Id}`).")
            .SetFileLogContent(
                $"Gave {role.Name} ({role.Id}) to {user.Username}#{user.Discriminator} ({user.Id}). {(reason != null ? $"Reason: {reason}." : "")}")
            .Send();
    }
    
    private async Task Unicycle_RemoveRole(ScheduledRoleRemovalJob job, IIzzyGuild guild)
    {
        var role = guild.GetRole(job.Role);
        var user = guild.GetUser(job.User);
        if (role == null || user == null) return;

        string? reason = job.Reason;
        
        _logger.Log(
            $"Removing {role.Name} ({role.Id}) from {user.Username}#{user.Discriminator} ({user.Id})", level: LogLevel.Debug);
        
        await _mod.RemoveRole(user, role.Id, reason);
        await _modLogging.CreateModLog(guild)
            .SetContent(
                $"Removed <@&{role.Id}> from <@{user.Id}> (`{user.Id}`)")
            .SetFileLogContent(
                $"Removed {role.Name} ({role.Id}) from {user.Username}#{user.Discriminator} ({user.Id}). {(reason != null ? $"Reason: {reason}." : "")}")
            .Send();
    }

    private async Task Unicycle_Unban(ScheduledUnbanJob job, IIzzyGuild guild, IIzzyClient client)
    {
        if (!await guild.GetIsBannedAsync(job.User)) return;

        var user = await client.GetUserAsync(job.User);
        
        _logger.Log(
            $"Unbanning {(user == null ? job.User : $"")}.",
            level: LogLevel.Debug);

        await guild.RemoveBanAsync(job.User);

        var embed = new EmbedBuilder()
            .WithTitle(
                $"Unbanned {(user != null ? $"{user.Username}#{user.Discriminator}" : "")} ({job.User})")
            .WithColor(16737792)
            .WithDescription($"Gasp! Does this mean I can invite <@{job.User}> to our next traditional unicorn sleepover?")
            .Build();
        
        await _modLogging.CreateModLog(guild)
            .SetEmbed(embed)
            .SetFileLogContent($"Unbanned {(user != null ? $"{user.Username}#{user.Discriminator}" : "")} ({job.User})")
            .Send();
    }

    private async Task Unicycle_Echo(ScheduledEchoJob job, IIzzyGuild guild, IIzzyClient client, ScheduledJobRepeatType repeatType, string jobId)
    {
        if (job.Content == "") return;

        var channel = guild.GetTextChannel(job.ChannelOrUser);
        if (channel == null)
        {
            MessageComponent? components = null;
            if (repeatType != None)
                components = new ComponentBuilder().WithButton(
                    customId: $"cancel-echo-job:{jobId}",
                    label: "Unsubscribe",
                    style: ButtonStyle.Primary
                ).Build();

            await client.SendDirectMessageAsync(job.ChannelOrUser, job.Content, components: components);
            return;
        }

        await channel.SendMessageAsync(job.Content);
    }

    public async Task Unicycle_BannerRotation(ScheduledBannerRotationJob job, IIzzyGuild guild,
        IIzzyClient client)
    {
        if (_config.BannerMode == ConfigListener.BannerMode.None) {
            _logger.Log("Unicycle_BannerRotation early returning because BannerMode is None.");
            return;
        }
        if (_config.BannerMode == ConfigListener.BannerMode.CustomRotation && _config.BannerImages.Count == 0)
        {
            _logger.Log("Unicycle_BannerRotation early returning because BannerMode is CustomRotation but BannerImages is empty.");
            return;
        }

        if (_config.BannerMode == ConfigListener.BannerMode.CustomRotation)
        {
            try
            {
                // Rotate through banners.
                var rand = new Random();
                var number = rand.Next(_config.BannerImages.Count);
                var url = _config.BannerImages.ToList()[number];
                Stream stream;
                try
                {
                    stream = await url
                        .WithHeader("user-agent", $"Izzy-Moonbot (Linux x86_64) Flurl.Http/3.2.4 DotNET/6.0")
                        .GetStreamAsync();
                }
                catch (FlurlHttpException ex)
                {
                    _logger.Log($"Recieved HTTP exception when executing Banner Rotation: {ex.Message}");
                    return;
                }

                var image = new Image(stream);

                await guild.SetBanner(image);

                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Changed banner to <{url}> for banner rotation.")
                    .SetFileLogContent(
                        $"Changed banner to {url} for banner rotation.")
                    .Send();
            }
            catch (FlurlHttpTimeoutException ex)
            {
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner but the host server didn't respond fast enough, is it down? If so please run `.config BannerMode None` to avoid unnecessarily pinging Manebooru.")
                    .SetFileLogContent(
                        $"Tried to change banner but the host server didn't respond fast enough, is it down? If so please run `.config BannerMode None` to avoid unnecessarily pinging Manebooru.")
                    .Send();
                _logger.Log(
                    $"Encountered HTTP timeout exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
            catch (FlurlHttpException ex)
            {
                // Http request failure.
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner and received a {ex.StatusCode} status code when attempting to ask the host server for the image. Doing nothing.")
                    .SetFileLogContent(
                        $"Tried to change banner and received a {ex.StatusCode} status code when attempting to ask the host server for the image. Doing nothing.")
                    .Send();
                _logger.Log(
                    $"Encountered HTTP exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
            catch (Exception ex)
            {
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner and received a general error when attempting to ask the host server for the image. Doing nothing.")
                    .SetFileLogContent(
                        $"Tried to change banner and received a general error when attempting to ask the host server for the image. Doing nothing.")
                    .Send();
                _logger.Log(
                    $"Encountered exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
        }
        else if (_config.BannerMode == ConfigListener.BannerMode.ManebooruFeatured)
        {
            // Set to Manebooru featured.
            try
            {
                var image = await BooruHelper.GetFeaturedImage();

                if (_generalStorage.CurrentBooruFeaturedImage != null)
                {
                    if (image.Id == _generalStorage.CurrentBooruFeaturedImage.Id)
                    {
                        _logger.Log($"Manebooru featured image is still {image.Id}. Nothing to do but update non-ID properties in the cache.", level: LogLevel.Debug);
                        _generalStorage.CurrentBooruFeaturedImage = image;
                        await FileHelper.SaveGeneralStorageAsync(_generalStorage);
                        return;
                    }
                }

                // Don't check the images if they're not ready yet!
                if (!image.ThumbnailsGenerated || image.Representations == null)
                {
                    await _modLogging.CreateModLog(guild)
                        .SetContent(
                            $"Tried to change banner to <https://manebooru.art/images/{image.Id}> but that image hasn't fully been generated yet. Doing nothing and trying again in {_config.BannerInterval} minutes.")
                        .SetFileLogContent(
                            $"Tried to change banner to https://manebooru.art/images/{image.Id} but that image hasn't fully been generated yet. Doing nothing and trying again in {_config.BannerInterval} minutes.")
                        .Send();
                    return;
                }

                if (image.Spoilered)
                {
                    // Image is blocked by current filter, complain.
                    await _modLogging.CreateModLog(guild)
                        .SetContent(
                            $"Tried to change banner to <https://manebooru.art/images/{image.Id}> but that image is blocked by my filter! Doing nothing.")
                        .SetFileLogContent(
                            $"Tried to change banner to https://manebooru.art/images/{image.Id} but that image is blocked by my filter! Doing nothing.")
                        .Send();
                    return;
                }

                var imageStream = await image.Representations.Thumbnail.GetStreamAsync();

                await guild.SetBanner(new Image(imageStream));
                
                _generalStorage.CurrentBooruFeaturedImage =
                    image;
                await FileHelper.SaveGeneralStorageAsync(_generalStorage);
                
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Changed banner to <https://manebooru.art/images/{image.Id}> for Manebooru featured image.")
                    .SetFileLogContent(
                        $"Changed banner to https://manebooru.art/images/{image.Id} for Manebooru featured image.")
                    .Send();
            }
            catch (FlurlHttpTimeoutException ex)
            {
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner but Manebooru didn't respond fast enough, is it down? If so please run `.config BannerMode None` to avoid unnecessarily pinging Manebooru.")
                    .SetFileLogContent(
                        $"Tried to change banner but Manebooru didn't respond fast enough, is it down? If so please run `.config BannerMode None` to avoid unnecessarily pinging Manebooru.")
                    .Send();
                _logger.Log(
                    $"Encountered HTTP timeout exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
            catch (FlurlHttpException ex)
            {
                // Http request failure.
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner and received a {ex.StatusCode} status code when attempting to ask Manebooru for the featured image. Doing nothing.\n" +
                        $"Likely causes:\n" +
                        $"  - I sent a badly formatted request to Manebooru.\n" +
                        $"  - Manebooru thinks I sent a badly formatted request when I didn't.\n" +
                        $"  - Manebooru is down and Cloudflare is giving me a error page.")
                    .SetFileLogContent(
                        $"Tried to change banner and recieved a {ex.StatusCode} status code when attempting to ask Manebooru for the featured image. Doing nothing.\n" +
                        $"Likely causes:\n" +
                        $"  - I sent a badly formatted request to Manebooru.\n" +
                        $"  - Manebooru thinks I sent a badly formatted request when I didn't.\n" +
                        $"  - Manebooru is down and Cloudflare is giving me a error page.")
                    .Send();
                _logger.Log(
                    $"Encountered HTTP exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
            catch (Exception ex)
            {
                await _modLogging.CreateModLog(guild)
                    .SetContent(
                        $"Tried to change banner and received a general error when attempting to ask Manebooru for the featured image. Doing nothing.\n" +
                        $"Likely causes:\n" +
                        $"  - The image is too big for Discord.\n" +
                        $"  - This server cannot have a banner.\n" +
                        $"  - The banner rotation job is an unexpected state.")
                    .SetFileLogContent(
                        $"Tried to change banner and received a general error when attempting to ask Manebooru for the featured image. Doing nothing.\n" +
                        $"Likely causes:\n" +
                        $"  - The image is too big for Discord.\n" +
                        $"  - This server cannot have a banner.\n" +
                        $"  - The banner rotation job is an unexpected state.")
                    .Send();
                _logger.Log(
                    $"Encountered exception when trying to change banner: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
        }
    }

    private async Task ButtonEvent(IIzzySocketMessageComponent component)
    {
        var buttonId = component.Data.CustomId;

        _logger.Log($"Received ButtonExecuted event with button id {buttonId}");
        var idParts = buttonId.Split(':');
        if (idParts.Length == 2 && idParts[0] == "cancel-echo-job")
        {
            var jobId = idParts[1];
            var job = GetScheduledJob(jobId);
            if (job is null)
            {
                _logger.Log($"Ignoring unsubscribe button click for job {jobId} because that job no longer exists");
                return;
            }

            _logger.Log($"Cancelling job {jobId} due to unsubscribe button click");
            await DeleteScheduledJob(job);

            await component.UpdateAsync(msg =>
            {
                msg.Components = new ComponentBuilder().WithButton(
                    customId: "successfully-unsubscribed",
                    label: "Successfully Unsubscribed",
                    disabled: true,
                    style: ButtonStyle.Success
                ).Build();
            });
        }

        await component.DeferAsync();
    }
}
