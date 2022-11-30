﻿using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot.EventListeners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_MoonbotTests.Helper;

[TestClass()]
public class ConfigHelperTests
{
    [TestMethod()]
    public void Config_GetValueTests()
    {
        var cfg = new Config();
        Assert.AreEqual("you all soon", ConfigHelper.GetValue(cfg, "DiscordActivityName"));
        Assert.AreEqual('.', ConfigHelper.GetValue(cfg, "Prefix"));
        Assert.AreEqual(true, ConfigHelper.GetValue(cfg, "ManageNewUserRoles"));
        Assert.AreEqual(100, ConfigHelper.GetValue(cfg, "UnicycleInterval"));
        Assert.IsTrue(ConfigHelper.GetValue(cfg, "FilterIgnoredChannels") is HashSet<ulong>);
        Assert.IsTrue(ConfigHelper.GetValue(cfg, "Aliases") is Dictionary<string, string>);

        Assert.ThrowsException<KeyNotFoundException>(() => ConfigHelper.GetValue(cfg, "foo"));
    }

    [TestMethod()]
    public async Task Config_SetValue_ValidScalars_TestsAsync()
    {
        var cfg = new Config();

        Assert.AreEqual("you all soon", cfg.DiscordActivityName);
        await ConfigHelper.SetSimpleValue(cfg, "DiscordActivityName", "the hoofball game");
        Assert.AreEqual("the hoofball game", cfg.DiscordActivityName);

        Assert.AreEqual('.', cfg.Prefix);
        await ConfigHelper.SetSimpleValue(cfg, "Prefix", '!');
        Assert.AreEqual('!', cfg.Prefix);

        Assert.AreEqual(true, cfg.ManageNewUserRoles);
        await ConfigHelper.SetBooleanValue(cfg, "ManageNewUserRoles", "false");
        Assert.AreEqual(false, cfg.ManageNewUserRoles);
        await ConfigHelper.SetBooleanValue(cfg, "ManageNewUserRoles", "y");
        Assert.AreEqual(true, cfg.ManageNewUserRoles);
        await ConfigHelper.SetBooleanValue(cfg, "ManageNewUserRoles", "deactivate");
        Assert.AreEqual(false, cfg.ManageNewUserRoles);
        await ConfigHelper.SetBooleanValue(cfg, "ManageNewUserRoles", "enable");
        Assert.AreEqual(true, cfg.ManageNewUserRoles);

        Assert.AreEqual(100, cfg.UnicycleInterval);
        await ConfigHelper.SetSimpleValue(cfg, "UnicycleInterval", 42);
        Assert.AreEqual(42, cfg.UnicycleInterval);

        Assert.AreEqual(10.0, cfg.SpamBasePressure);
        await ConfigHelper.SetSimpleValue(cfg, "SpamBasePressure", 0.5);
        Assert.AreEqual(0.5, cfg.SpamBasePressure);

        Assert.AreEqual(ConfigListener.BannerMode.None, cfg.BannerMode);
        await ConfigHelper.SetSimpleValue(cfg, "BannerMode", ConfigListener.BannerMode.ManebooruFeatured);
        Assert.AreEqual(ConfigListener.BannerMode.ManebooruFeatured, cfg.BannerMode);
    }

    // TODO: figure out Discord.NET test doubles to enable testing users, roles, channels, etc
    /*[TestMethod()]
    public async Task Config_SetValue_ValidDiscordEntitiesTestsAsync()
    {
    }*/

    [TestMethod()]
    public void Config_SetValue_InvalidValues_Tests()
    {
        var cfg = new Config();

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetSimpleValue(cfg, "foo", "bar"));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetSimpleValue(cfg, "Aliases", "bar"));

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetSimpleValue(cfg, "foo", 'b'));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetSimpleValue(cfg, "Aliases", 'b'));

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetBooleanValue(cfg, "foo", "bar"));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetBooleanValue(cfg, "Aliases", "bar"));

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetSimpleValue(cfg, "foo", 42));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetSimpleValue(cfg, "Aliases", 42));

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetSimpleValue(cfg, "foo", 1.0));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetSimpleValue(cfg, "Aliases", 1.0));

        Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => ConfigHelper.SetSimpleValue(cfg, "foo", ConfigListener.BannerMode.ManebooruFeatured));
        Assert.ThrowsExceptionAsync<ArgumentException>(() => ConfigHelper.SetSimpleValue(cfg, "Aliases", ConfigListener.BannerMode.ManebooruFeatured));
    }

    // The built-in Assert.AreEqual and CollectionsAssert.AreEqual have error messages so bad it was worth writing my own asserts
    void AssertListsAreEqual<T>(IList<T>? expected, IList<T>? actual, string message = "")
    {
        if (expected is null || actual is null)
        {
            Assert.AreEqual(expected, actual);
            return;
        }
        if (expected.Count() != actual.Count())
            Assert.AreEqual(expected, actual, $"\nCount() mismatch: {expected.Count()} != {actual.Count()}");
        foreach (var i in Enumerable.Range(0, expected.Count()))
            Assert.AreEqual(expected[i], actual[i], $"\nItem {i}" + message);
    }

    void AssertSetsAreEqual<T>(ISet<T>? expected, ISet<T>? actual, string message = "")
    {
        if (expected is null || actual is null)
        {
            Assert.AreEqual(expected, actual);
            return;
        }
        if (expected.Count() != actual.Count())
            Assert.AreEqual(expected, actual, $"\nCount() mismatch: {expected.Count()} != {actual.Count()}");
        foreach (var value in expected)
            Assert.IsTrue(actual.Contains(value), $"\nValue {value}" + message);
    }

    [TestMethod()]
    public async Task Config_HashSets_TestsAsync()
    {
        var cfg = new Config();

        AssertSetsAreEqual(new HashSet<string>(), cfg.FilterResponseSilence);
        AssertSetsAreEqual(new HashSet<string>(), ConfigHelper.GetStringSet(cfg, "FilterResponseSilence"));
        Assert.IsFalse(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "FilterResponseSilence", "spam"));

        await ConfigHelper.AddToStringSet(cfg, "FilterResponseSilence", "spam");

        AssertSetsAreEqual(new HashSet<string> { "spam" }, cfg.FilterResponseSilence);
        AssertSetsAreEqual(new HashSet<string> { "spam" }, ConfigHelper.GetStringSet(cfg, "FilterResponseSilence"));
        Assert.IsTrue(ConfigHelper.HasValueInSet(cfg, "FilterResponseSilence", "spam"));

        await ConfigHelper.RemoveFromStringSet(cfg, "FilterResponseSilence", "spam");

        AssertSetsAreEqual(new HashSet<string>(), cfg.FilterResponseSilence);
        AssertSetsAreEqual(new HashSet<string>(), ConfigHelper.GetStringSet(cfg, "FilterResponseSilence"));
        Assert.IsFalse(ConfigHelper.HasValueInSet(cfg, "FilterResponseSilence", "spam"));

        Assert.ThrowsException<KeyNotFoundException>(() => ConfigHelper.GetStringSet(cfg, "foo"));
        Assert.ThrowsException<ArgumentException>(() => ConfigHelper.GetStringSet(cfg, "Prefix"));
    }

    // The built-in Assert.AreEqual and CollectionsAssert.AreEqual don't even work on Dictionaries, so everyone has to write their own
    void AssertDictionariesAreEqual<K, V>(IDictionary<K, V>? expected, IDictionary<K, V>? actual, string message = "")
    {
        if (expected is null || actual is null)
        {
            Assert.AreEqual(expected, actual);
            return;
        }
        AssertListsAreEqual(
            expected.OrderBy(kv => kv.Key).ToList(),
            actual.OrderBy(kv => kv.Key).ToList()
        );
    }

    [TestMethod()]
    public async Task Config_DictionariesOfScalars_TestsAsync()
    {
        var cfg = new Config();

        // Aliases is the only Dict<string, string> in Config

        AssertDictionariesAreEqual(new Dictionary<string, string>(), cfg.Aliases);
        AssertDictionariesAreEqual(new Dictionary<string, string>(), ConfigHelper.GetStringDictionary(cfg, "Aliases"));
        Assert.IsFalse(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "Aliases", "testalias"));

        await ConfigHelper.CreateStringDictionaryKey(cfg, "Aliases", "testalias", "echo hi");

        AssertDictionariesAreEqual(new Dictionary<string, string> { { "testalias", "echo hi" } }, cfg.Aliases);
        AssertDictionariesAreEqual(new Dictionary<string, string> { { "testalias", "echo hi" } }, ConfigHelper.GetStringDictionary(cfg, "Aliases"));
        Assert.IsTrue(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "Aliases", "testalias"));
        Assert.AreEqual("echo hi", ConfigHelper.GetStringDictionaryValue(cfg, "Aliases", "testalias"));

        await ConfigHelper.SetStringDictionaryValue(cfg, "Aliases", "testalias", "echo belizzle it");

        AssertDictionariesAreEqual(new Dictionary<string, string> { { "testalias", "echo belizzle it" } }, cfg.Aliases);
        AssertDictionariesAreEqual(new Dictionary<string, string> { { "testalias", "echo belizzle it" } }, ConfigHelper.GetStringDictionary(cfg, "Aliases"));
        Assert.IsTrue(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "Aliases", "testalias"));
        Assert.AreEqual("echo belizzle it", ConfigHelper.GetStringDictionaryValue(cfg, "Aliases", "testalias"));

        await ConfigHelper.RemoveStringDictionaryKey(cfg, "Aliases", "testalias");

        AssertDictionariesAreEqual(new Dictionary<string, string>(), cfg.Aliases);
        AssertDictionariesAreEqual(new Dictionary<string, string>(), ConfigHelper.GetStringDictionary(cfg, "Aliases"));
        Assert.IsFalse(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "Aliases", "testalias"));

        Assert.ThrowsException<KeyNotFoundException>(() => ConfigHelper.GetStringDictionary(cfg, "foo"));
        Assert.ThrowsException<ArgumentException>(() => ConfigHelper.GetStringDictionary(cfg, "Prefix"));

        // FilterResponseMessages is the only Dict<string, string?> in Config

        AssertDictionariesAreEqual(new Dictionary<string, string?>(), cfg.Aliases);
        AssertDictionariesAreEqual(new Dictionary<string, string?>(), ConfigHelper.GetNullableStringDictionary(cfg, "FilterResponseMessages"));
        Assert.IsFalse(ConfigHelper.DoesStringDictionaryKeyExist(cfg, "FilterResponseMessages", "spam"));

        await ConfigHelper.CreateStringDictionaryKey(cfg, "FilterResponseMessages", "spam", "this is a ham server");

        AssertDictionariesAreEqual(new Dictionary<string, string?> { { "spam", "this is a ham server" } }, cfg.FilterResponseMessages);
        AssertDictionariesAreEqual(new Dictionary<string, string?> { { "spam", "this is a ham server" } }, ConfigHelper.GetNullableStringDictionary(cfg, "FilterResponseMessages"));
        Assert.IsTrue(ConfigHelper.DoesNullableStringDictionaryKeyExist(cfg, "FilterResponseMessages", "spam"));
        Assert.AreEqual("this is a ham server", ConfigHelper.GetNullableStringDictionaryValue(cfg, "FilterResponseMessages", "spam"));

        await ConfigHelper.SetStringDictionaryValue(cfg, "FilterResponseMessages", "spam", "begone spambots");

        AssertDictionariesAreEqual(new Dictionary<string, string?> { { "spam", "begone spambots" } }, cfg.FilterResponseMessages);
        AssertDictionariesAreEqual(new Dictionary<string, string?> { { "spam", "begone spambots" } }, ConfigHelper.GetNullableStringDictionary(cfg, "FilterResponseMessages"));
        Assert.IsTrue(ConfigHelper.DoesNullableStringDictionaryKeyExist(cfg, "FilterResponseMessages", "spam"));
        Assert.AreEqual("begone spambots", ConfigHelper.GetNullableStringDictionaryValue(cfg, "FilterResponseMessages", "spam"));

        await ConfigHelper.RemoveNullableStringDictionaryKey(cfg, "FilterResponseMessages", "spam");

        AssertDictionariesAreEqual(new Dictionary<string, string?>(), cfg.FilterResponseMessages);
        AssertDictionariesAreEqual(new Dictionary<string, string?>(), ConfigHelper.GetNullableStringDictionary(cfg, "FilterResponseMessages"));
        Assert.IsFalse(ConfigHelper.DoesNullableStringDictionaryKeyExist(cfg, "FilterResponseMessages", "spam"));

        Assert.ThrowsException<KeyNotFoundException>(() => ConfigHelper.GetNullableStringDictionary(cfg, "foo"));
        Assert.ThrowsException<ArgumentException>(() => ConfigHelper.GetNullableStringDictionary(cfg, "Prefix"));
    }

    // even my AssertDictionariesAreEqual helper falls apart on Set values
    void AssertDictsOfSetsAreEqual<K, V>(IDictionary<K, HashSet<V>>? expected, IDictionary<K, HashSet<V>>? actual, string message = "")
    {
        if (expected is null || actual is null)
        {
            Assert.AreEqual(expected, actual);
            return;
        }
        if (expected.Count() != actual.Count())
            Assert.AreEqual(expected, actual, $"\nCount() mismatch: {expected.Count()} != {actual.Count()}");
        foreach (var kv in expected)
        {
            AssertSetsAreEqual(expected[kv.Key], actual[kv.Key], $"\nKey {kv.Key}" + message);
        }
    }

    [TestMethod()]
    public async Task Config_DictionariesOfSets_TestsAsync()
    {
        var cfg = new Config();

        // FilteredWords is the only Dict<string, Set<>> in Config

        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>>(), cfg.FilteredWords);
        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>>(), ConfigHelper.GetStringSetDictionary(cfg, "FilteredWords"));
        Assert.IsFalse(ConfigHelper.DoesStringSetDictionaryKeyExist(cfg, "FilteredWords", "jinxies"));

        await ConfigHelper.CreateStringSetDictionaryKey(cfg, "FilteredWords", "jinxies", "mayonnaise");

        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "mayonnaise" } } }, cfg.FilteredWords);
        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "mayonnaise" } } }, ConfigHelper.GetStringSetDictionary(cfg, "FilteredWords"));
        Assert.IsTrue(ConfigHelper.DoesStringSetDictionaryKeyExist(cfg, "FilteredWords", "jinxies"));
        AssertSetsAreEqual(new HashSet<string> { "mayonnaise" }, ConfigHelper.GetStringSetDictionaryValue(cfg, "FilteredWords", "jinxies"));

        await ConfigHelper.AddToStringSetDictionaryValue(cfg, "FilteredWords", "jinxies", "magic");
        await ConfigHelper.AddToStringSetDictionaryValue(cfg, "FilteredWords", "jinxies", "wing");
        await ConfigHelper.AddToStringSetDictionaryValue(cfg, "FilteredWords", "jinxies", "feather");

        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "mayonnaise", "magic", "wing", "feather" } } }, cfg.FilteredWords);
        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "mayonnaise", "magic", "wing", "feather" } } }, ConfigHelper.GetStringSetDictionary(cfg, "FilteredWords"));
        Assert.IsTrue(ConfigHelper.DoesStringSetDictionaryKeyExist(cfg, "FilteredWords", "jinxies"));
        AssertSetsAreEqual(new HashSet<string> { "mayonnaise", "magic", "wing", "feather" }, ConfigHelper.GetStringSetDictionaryValue(cfg, "FilteredWords", "jinxies"));

        await ConfigHelper.RemoveFromStringSetDictionaryValue(cfg, "FilteredWords", "jinxies", "mayonnaise");
        await ConfigHelper.RemoveFromStringSetDictionaryValue(cfg, "FilteredWords", "jinxies", "magic");

        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "wing", "feather" } } }, cfg.FilteredWords);
        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>> { { "jinxies", new HashSet<string> { "wing", "feather" } } }, ConfigHelper.GetStringSetDictionary(cfg, "FilteredWords"));
        Assert.IsTrue(ConfigHelper.DoesStringSetDictionaryKeyExist(cfg, "FilteredWords", "jinxies"));
        AssertSetsAreEqual(new HashSet<string> { "wing", "feather" }, ConfigHelper.GetStringSetDictionaryValue(cfg, "FilteredWords", "jinxies"));

        await ConfigHelper.RemoveStringSetDictionaryKey(cfg, "FilteredWords", "jinxies");

        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>>(), cfg.FilteredWords);
        AssertDictsOfSetsAreEqual(new Dictionary<string, HashSet<string>>(), ConfigHelper.GetStringSetDictionary(cfg, "FilteredWords"));
        Assert.IsFalse(ConfigHelper.DoesStringSetDictionaryKeyExist(cfg, "FilteredWords", "jinxies"));

        Assert.ThrowsException<KeyNotFoundException>(() => ConfigHelper.GetStringSetDictionary(cfg, "foo"));
        Assert.ThrowsException<ArgumentException>(() => ConfigHelper.GetStringSetDictionary(cfg, "Prefix"));
    }
}