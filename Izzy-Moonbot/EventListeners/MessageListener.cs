using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;

namespace Izzy_Moonbot.EventListeners;

public class MessageListener
{
    private readonly LoggingService _logger;
    private readonly Config _config;
    private readonly ModLoggingService _modLogger;

    public MessageListener(LoggingService logger, Config config, ModLoggingService modLogger)
    {
        _logger = logger;
        _config = config;
        _modLogger = modLogger;
    }

    public void RegisterEvents(IIzzyClient client)
    {
        client.MessageUpdated += async (oldContent, newMessage, channel) => await DiscordHelper.LeakOrAwaitTask(ProcessMessageUpdate(oldContent, newMessage, channel, client));
        client.MessageDeleted += async (messageId, message, channelId, channel) => await DiscordHelper.LeakOrAwaitTask(ProcessMessageDelete(messageId, message, channelId, channel, client));
    }

    private async Task ProcessMessageUpdate(
        string? oldContent,
        IIzzyMessage newMessage,
        IIzzyMessageChannel channel,
        IIzzyClient client)
    {
        _logger.Log($"Received MessageUpdated event for message id {newMessage.Id}.");

        var logChannel = GetLogChannel(client);
        if (logChannel == null) return;

        var defaultGuild = client.GetGuild(DiscordHelper.DefaultGuild());
        if (defaultGuild?.GetChannel(channel.Id) is null) return;

        if (newMessage.Content == oldContent)
        {
            // Skipping LogChannel post for MessageUpdated event because the message's .Content did not change.
            // This means some other property was edited, e.g. Discord auto-unfurled a link, or the message was pinned.
            return;
        }

        var author = newMessage.Author;
        if (author.Id == client.CurrentUser.Id) return; // Don't process self.
        if (author.IsBot) return; // Don't listen to bots

        var isMod = defaultGuild.GetUser(author.Id)?.Roles.Any(r => r.Id == _config.ModRole) ?? false;
        if ((newMessage.CreatedAt.AddHours(24) < DateTimeOffset.UtcNow) && !isMod)
        {
            var oldEditWarning = $":warning: >24-hour-old message edit by <@{author.Id}> ({author.Id}) detected: {newMessage.GetJumpUrl()}";
            await _modLogger.CreateModLog(defaultGuild).SetContent(oldEditWarning).SetFileLogContent(oldEditWarning).Send();
        }

        var logMessageTemplate =
            $"Message {newMessage.Id} by {DiscordHelper.DisplayName(author, defaultGuild)} ({author.Username}/{author.Id}) **edited** in {channel.Name}:" +
            "\n{warn}" +
            (oldContent != null ?
                "__Before__:\n{old}\n" :
                "Content before edit unknown (this usually means the original message was too old to be in Izzy's cache).\n") +
            "__After__:\n{new}";

        var oldLength = oldContent?.Length ?? 0;
        var newContent = newMessage.Content;
        var truncationWarning = "";
        if (logMessageTemplate.Length + oldLength + newContent.Length > DiscordHelper.MessageLengthLimit) {
            truncationWarning = "⚠️ The message needed to be truncated";
            var spaceForMessages = DiscordHelper.MessageLengthLimit - logMessageTemplate.Length - truncationWarning.Length - oldLength - newContent.Length;
            var truncationMarker = " [...] ";
            var spaceForHalfMessage = ((spaceForMessages / 2) - truncationMarker.Length) / 2;

            if (oldContent != null)
                oldContent = oldContent.Substring(0, spaceForHalfMessage) +
                    truncationMarker +
                    oldContent.Substring(oldLength - spaceForHalfMessage);

            newContent = newContent.Substring(0, spaceForHalfMessage) +
                truncationMarker +
                newContent.Substring(newContent.Length - spaceForHalfMessage);
        }

        var logMessage = logMessageTemplate.Replace("{warn}", truncationWarning).Replace("{old}", oldContent).Replace("{new}", newContent);
        await logChannel.SendMessageAsync(logMessage, allowedMentions: AllowedMentions.None);
    }

    private async Task ProcessMessageDelete(
        ulong messageId,
        IIzzyMessage? message,
        ulong channelId,
        IIzzyMessageChannel? channel,
        IIzzyClient client)
    {
        _logger.Log($"Received MessageDeleted event for message id {messageId}.");

        var logChannel = GetLogChannel(client);
        if (logChannel == null) return;

        var defaultGuild = client.GetGuild(DiscordHelper.DefaultGuild());
        if (defaultGuild?.GetChannel(channelId) is null) return;

        if (message is null)
        {
            await logChannel.SendMessageAsync($"Message id {messageId} **deleted** in channel {channelId}, but we know nothing else about it. " +
                "This usually means the message was too old to be in Izzy's local cache.", allowedMentions: AllowedMentions.None);
            return;
        }

        var author = message.Author;
        if (author.Id == client.CurrentUser.Id) return; // Don't process self.
        if (author.IsBot) return; // Don't listen to bots

        var logMessage = $"Message id {messageId} by {DiscordHelper.DisplayName(author, defaultGuild)} ({author.Username}/{author.Id}) **deleted**";

        if (channel is null)
            logMessage += $" in unknown channel {channelId}:\n";
        else
            logMessage += $" in {channel.Name}:\n";

        if (message.Attachments?.Any() ?? false)
            logMessage += $"__Content__:\n{message.Content}\n" +
                $"__Attachments__:\n{string.Join('\n', message.Attachments.Select(a => a.ProxyUrl))}";
        else
            logMessage += $"{message.Content}";

        await logChannel.SendMessageAsync(logMessage, allowedMentions: AllowedMentions.None);
    }

    private IIzzySocketTextChannel? GetLogChannel(IIzzyClient client)
    {
        var defaultGuild = client.GetGuild(DiscordHelper.DefaultGuild());

        var logChannelId = _config.LogChannel;
        if (logChannelId == 0)
        {
            _logger.Log("Can't post logs because .config LogChannel hasn't been set.");
            return null;
        }
        var logChannel = defaultGuild?.GetTextChannel(logChannelId);
        if (logChannel == null)
        {
            _logger.Log("Something went wrong trying to access LogChannel.");
            return null;
        }

        return logChannel;
    }
}
