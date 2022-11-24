using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Settings;

namespace Izzy_Moonbot.Service;

// name credit to Scoots <3

public class ModLoggingService
{
    private readonly Config _config;
    private readonly BatchLogger _batchLogger;

    public ModLoggingService(Config config)
    {
        _config = config;
        _batchLogger = new BatchLogger(_config);
    }

    public ModLogConstructor CreateModLog(SocketGuild guild)
    {
        return new ModLogConstructor(_config, guild, _batchLogger);
    }
}

public class ModLog
{
    public SocketTextChannel Channel;
    public string? Content;
    public Embed? Embed;
    public string? FileLogContent;

    public ModLog(SocketTextChannel channel) { Channel = channel; }
}

public class ModLogConstructor
{
    private readonly SocketGuild _guild;
    private readonly Config _config;
    private readonly BatchLogger _batchLogger;

    private readonly ModLog _log;

    public ModLogConstructor(Config config, SocketGuild guild, BatchLogger batchLogger)
    {
        _config = config;
        _guild = guild;
        _batchLogger = batchLogger;

        _log = new ModLog(_guild.GetTextChannel(_config.ModChannel));
    }

    public ModLogConstructor SetContent(string content)
    {
        _log.Content = content;
        return this;
    }

    public ModLogConstructor SetEmbed(Embed embed)
    {
        _log.Embed = embed;
        return this;
    }

    public ModLogConstructor SetFileLogContent(string content)
    {
        _log.FileLogContent = content;
        return this;
    }

    public async Task Send()
    {
        if (_log.Content == null && _log.Embed == null) throw new InvalidOperationException("A moderation log cannot have no content");
        
        // Log to file
        if (_log.FileLogContent is string fileLogContent)
        {
            var modLogFileContent = LoggingService.PrepareMessageForLogging(fileLogContent, null, true);
            var filepath = FileHelper.SetUpFilepath(FilePathType.Root, "moderation", "log");

            if (!File.Exists(filepath))
                await File.WriteAllTextAsync(filepath, $"----------= {DateTimeOffset.UtcNow:F} =----------{Environment.NewLine}");

            await File.AppendAllTextAsync(filepath, modLogFileContent);
        }

        if (_config.BatchSendLogs)
            _batchLogger.AddModLog(_log);
        else
            await _log.Channel.SendMessageAsync(_log.Content, embed: _log.Embed);
    }
}

public class BatchLogger
{
    private readonly List<ModLog> _modLogs = new();
    private readonly Config _config;

    public BatchLogger(Config config)
    {
        _config = config;

        RefreshBatchInterval();
    }

    public void AddModLog(ModLog log)
    {
        _modLogs.Add(log);
    }

    private void RefreshBatchInterval()
    {
        Task.Factory.StartNew(async () =>
        {
            await Task.Delay(Convert.ToInt32(_config.BatchLogsSendRate * 1000));

            SocketTextChannel? modLogChannel = null;
            var modLogContent = new List<string>();
            var modLogEmbeds = new List<Embed>();

            foreach (var modLog in _modLogs)
            {
                modLogChannel = modLog.Channel;
                if (modLog.Embed is not null) modLogEmbeds.Add(modLog.Embed);
                if (modLog.Content is not null) modLogContent.Add(modLog.Content);
            }

            if (modLogChannel != null)
                await modLogChannel.SendMessageAsync(string.Join($"{Environment.NewLine}", modLogContent),
                    embeds: modLogEmbeds.ToArray());
            
            _modLogs.Clear();

            RefreshBatchInterval();
        });
    }
}