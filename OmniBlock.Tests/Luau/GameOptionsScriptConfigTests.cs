using System.Text.Json;
using OmniBlock.Client.Options;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

public sealed class GameOptionsScriptConfigTests
{
    [Theory]
    [InlineData("pauseOnFocusLoss")]
    [InlineData("captureMouse")]
    public void FocusOptionsAreEnabledByDefaultAndScriptChoicePersists(string key)
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-focus-options-");
        try
        {
            var path = Path.Combine(directory.FullName, "options.json");
            var options = new GameOptions(null!, directory.FullName);

            Assert.True(options.GetScriptConfig(key).Boolean);
            Assert.Contains(options.UIScreenOptions, option => option.SaveKey == key);
            Assert.False(options.SetScriptConfig(key, LuauConfigValue.From("false")));
            Assert.True(options.SetScriptConfig(key, LuauConfigValue.From(false)));
            Assert.False(options.GetScriptConfig(key).Boolean);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.False(reloaded.GetScriptConfig(key).Boolean);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(document.RootElement.GetProperty("options").GetProperty(key).GetBoolean());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void TypedScriptWritesUseNormalOptionSettersAndCallbacks()
    {
        float observed = -1;
        FloatOption option = new("test", "volume")
        {
            Steps = 10,
            OnChanged = value => observed = value
        };

        var changed = GameOptions.SetOptionValue(option, LuauConfigValue.From(1.7));

        Assert.True(changed);
        Assert.Equal(1, option.Value);
        Assert.Equal(1, observed);
    }

    [Fact]
    public void RejectsWrongTypesAndOutOfRangeCycleValues()
    {
        BoolOption boolean = new("test", "enabled");
        CycleOption cycle = new("test", "mode", ["one", "two"]);

        Assert.False(GameOptions.SetOptionValue(boolean, LuauConfigValue.From("true")));
        Assert.False(GameOptions.SetOptionValue(cycle, LuauConfigValue.From(2.0)));
        Assert.False(GameOptions.SetOptionValue(cycle, LuauConfigValue.From(0.5)));
    }

    [Fact]
    public void Presentation_quality_is_scriptable_and_persistent()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-presentation-options-");
        try
        {
            var options = new GameOptions(null!, directory.FullName);

            Assert.Equal(1, options.PresentationQuality);
            Assert.True(options.SetScriptConfig(
                "presentationQuality", LuauConfigValue.From(0.0)));
            Assert.Equal(0, options.PresentationQuality);
            Assert.Equal(3, options.GetScriptConfigOptions("presentationQuality")!.Count);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.Equal(0, reloaded.PresentationQuality);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void World_distance_options_are_scriptable_persistent_and_effectively_ordered()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-world-distance-options-");
        try
        {
            var options = new GameOptions(null!, directory.FullName);
            Assert.Equal(16, GameOptions.DecodeTerrainHorizonDistance(0));
            Assert.Equal(64, GameOptions.DecodeTerrainHorizonDistance(1));
            Assert.Equal(
                new[] { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f },
                Enumerable.Range(0, 6)
                    .Select(index => GameOptions.DecodeTerrainLodDropoffScale(index / 5f))
                    .ToArray());
            Assert.Null(GameOptions.DecodeFogDistance(0));
            Assert.Equal(8, GameOptions.DecodeFogDistance(1f / 57));
            Assert.Equal(64, GameOptions.DecodeFogDistance(1));
            Assert.Equal(9, options.RenderDistance);
            Assert.Equal(64, options.TerrainHorizonDistance);
            Assert.Equal(1f, options.TerrainLodDropoffScale);
            Assert.Equal(64, options.FogDistance);
            Assert.Equal(9, options.SimulationDistance);

            Assert.True(options.SetScriptConfig("viewDistance", LuauConfigValue.From(0.5)));
            Assert.True(options.SetScriptConfig("terrainHorizonDistance", LuauConfigValue.From(0.0)));
            Assert.True(options.SetScriptConfig("terrainLodDropoffDistance", LuauConfigValue.From(1.0)));
            Assert.True(options.SetScriptConfig("fogDistance", LuauConfigValue.From(0.1)));
            Assert.True(options.SetScriptConfig("simulationDistance", LuauConfigValue.From(1.0)));

            Assert.Equal(18, options.RenderDistance);
            Assert.Equal(18, options.TerrainHorizonDistance);
            Assert.Equal(3f, options.TerrainLodDropoffScale);
            Assert.Equal(18, options.FogDistance);
            Assert.Equal(18, options.SimulationDistance);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.Equal(18, reloaded.RenderDistance);
            Assert.Equal(18, reloaded.TerrainHorizonDistance);
            Assert.Equal(3f, reloaded.TerrainLodDropoffScale);
            Assert.Equal(18, reloaded.FogDistance);
            Assert.Equal(18, reloaded.SimulationDistance);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Entity_impostor_distance_is_aggressive_scriptable_persistent_and_migrates_boolean_gate()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-impostor-option-");
        try
        {
            var options = new GameOptions(null!, directory.FullName);
            Assert.Equal(0, GameOptions.DecodeEntityImpostorDistance(0));
            Assert.Equal(32, GameOptions.DecodeEntityImpostorDistance(1f / 15));
            Assert.Equal(256, GameOptions.DecodeEntityImpostorDistance(1));
            Assert.True(options.EntityImpostors);
            Assert.Equal(48, options.EntityImpostorDistance);
            Assert.True(options.SetScriptConfig("entityImpostorDistance", LuauConfigValue.From(0.0)));
            Assert.False(options.EntityImpostors);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.False(reloaded.EntityImpostors);

            var legacyDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "legacy"));
            File.WriteAllText(Path.Combine(legacyDirectory.FullName, "options.txt"), "entityImpostors:true\n");
            var migrated = new GameOptions(null!, legacyDirectory.FullName);
            Assert.Equal(48, migrated.EntityImpostorDistance);

            Assert.True(migrated.SetScriptConfig("entityImpostors", LuauConfigValue.From(false)));
            Assert.False(migrated.GetScriptConfig("entityImpostors").Boolean);
            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(legacyDirectory.FullName, "options.json")));
            Assert.Equal(0, document.RootElement.GetProperty("options")
                .GetProperty("entityImpostorDistance").GetSingle());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Json_round_trip_preserves_native_types_case_colons_and_bindings()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-json-options-");
        try
        {
            var options = new GameOptions(null!, directory.FullName)
            {
                Skin = "CaseSensitiveSkin",
                LastServer = "example.test:25565"
            };
            options.AdvancedItemTooltips = true;
            options.MusicVolume = 0.25f;
            options.SetKeyBinding(options.KeyBindForward, 123);
            options.SetKeyBinding(options.KeyBindCommand, 124);
            options.ControllerBindings[0].Button = (Silk.NET.GLFW.GamepadButton)7;
            options.SaveOptions();

            var jsonPath = Path.Combine(directory.FullName, "options.json");
            using (var document = JsonDocument.Parse(File.ReadAllText(jsonPath)))
            {
                var root = document.RootElement;
                Assert.Equal(1, root.GetProperty("version").GetInt32());
                Assert.Equal(JsonValueKind.Number,
                    root.GetProperty("options").GetProperty("music").ValueKind);
                Assert.Equal(JsonValueKind.True,
                    root.GetProperty("client").GetProperty("advancedItemTooltips").ValueKind);
                Assert.Equal("CaseSensitiveSkin", root.GetProperty("client").GetProperty("skin").GetString());
                Assert.Equal("example.test:25565", root.GetProperty("client").GetProperty("lastServer").GetString());
            }

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.Equal(0.25f, reloaded.MusicVolume);
            Assert.Equal("CaseSensitiveSkin", reloaded.Skin);
            Assert.Equal("example.test:25565", reloaded.LastServer);
            Assert.True(reloaded.AdvancedItemTooltips);
            Assert.Equal(123, reloaded.KeyBindForward.ScanCode);
            Assert.Equal(124, reloaded.KeyBindCommand.ScanCode);
            Assert.Equal(7, (int)reloaded.ControllerBindings[0].Button);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Legacy_file_is_imported_once_and_preserves_values_containing_colons()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-legacy-options-");
        try
        {
            var legacyPath = Path.Combine(directory.FullName, "options.txt");
            File.WriteAllText(legacyPath,
                "music:0.375\nlastServer:localhost:25565\nskin:MixedCase\nkey_key.forward:91\n");

            var imported = new GameOptions(null!, directory.FullName);

            Assert.Equal(0.375f, imported.MusicVolume);
            Assert.Equal("localhost:25565", imported.LastServer);
            Assert.Equal("MixedCase", imported.Skin);
            Assert.Equal(91, imported.KeyBindForward.ScanCode);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "options.json")));
            Assert.True(File.Exists(legacyPath));

            File.WriteAllText(legacyPath, "music:1\n");
            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.Equal(0.375f, reloaded.MusicVolume);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Invalid_json_entry_does_not_discard_valid_entries_or_defaults()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-invalid-json-options-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "options.json"), """
                {
                  "version": 1,
                  "options": {
                    "music": 0.125,
                    "sound": "not-a-number",
                    "difficulty": 999,
                    "pauseOnFocusLoss": false
                  }
                }
                """);

            var options = new GameOptions(null!, directory.FullName);

            Assert.Equal(0.125f, options.MusicVolume);
            Assert.Equal(1f, options.SoundVolume);
            Assert.Equal(2, options.Difficulty);
            Assert.False(options.PauseOnFocusLossOption.Value);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
