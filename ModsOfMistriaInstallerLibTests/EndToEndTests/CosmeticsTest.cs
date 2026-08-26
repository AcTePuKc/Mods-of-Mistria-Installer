using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using ModsOfMistriaInstallerLibTests.Utils;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace ModsOfMistriaInstallerLibTests.EndToEndTests;

[TestFixture]
public class CosmeticsTest
{
    private MockMod GetSimpleMod()
    {
        return new MockMod(new Dictionary<string, object>
        {
            {
                "momi/cosmetics/lryn_celine_cosmetic.toml",
                """
                [lryn_celine_summer_skirt]
                name = "Celine's summer skirt"
                ui_slot = "bottom"
                ui_sub_category = "skirt"

                lut = "images/lut.png"
                ui_sprites = {
                    ui = "images/ui.png",
                    outline = "images/outline.png"
                }
                
                cosmetic_sprites = { waist = "images/skirt.png" }
                """
            },
            {
                "images/skirt.png",
                FixtureHandler.ReadAllBytes("OutfitMod/skirt.png")
            },
            {
                "images/lut.png",
                FixtureHandler.ReadAllBytes("OutfitMod/lut.png")
            },
            {
                "images/outline.png",
                FixtureHandler.ReadAllBytes("OutfitMod/outline.png")
            },
            {
                "images/ui.png",
                FixtureHandler.ReadAllBytes("OutfitMod/ui.png")
            }
        });
    }
    
    [Test]
    public void ShouldInstallACosmetic()
    {
        var fileModifier = new MockFileModifier(new ());
        
        var mod = GetSimpleMod();
        
        new MockInstaller().InstallMod(mod, fileModifier);
        
        // Check that a UI Item .meta.toml and poly file was created
        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.meta.toml"))
        );
        
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.poly.toml"))    
        );
        
        // Check that a UI Outline .meta.toml and poly file was created
        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_outline.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_outline.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/outline.meta.toml"))
        );
        
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_outline.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_outline.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/outline.poly.toml"))
        );
        
        // Check that the Player Item .meta.toml and poly files were created
        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_waist.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_waist.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/waist.meta.toml"))
        );
        
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_waist.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_waist.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/waist.poly.toml"))
        );
        
        // Check that it was inserted into player_assets
        Assert.That(fileModifier.Exists("assets/fiddle/player_assets.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/fiddle/player_assets.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/player_assets.toml"))
        );
        
        // Check that it was inserted in outlines.json
        Assert.That(fileModifier.Exists("assets/data_files/animation/outlines.json"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/data_files/animation/outlines.json"),
            new ContainsJsonConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/outlines.json"))
        );
        
        // Check that it was inserted into player_asset_parts.json
        Assert.That(fileModifier.Exists("assets/data_files/animation/player_asset_parts.json"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/data_files/animation/player_asset_parts.json"),
            new ContainsJsonConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/player_asset_parts.json"))
        );
    }

    [Test]
    public void ShouldIncludeLutIfAvailable()
    {
        var fileModifier = new MockFileModifier(new());

        var mod = GetSimpleMod();
        
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "skirt"
            ui_sub_category = "skirt"
            default_unlocked = true

            lut = "images/lut.png"
            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );

        new MockInstaller().InstallMod(mod, fileModifier);

        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_lut.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_lut.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/lut.meta.toml"))
        );
        
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_lut.meta.toml"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_lut.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/lut.poly.toml"))
        );
    }

    [Test]
    public void ShouldRequireALutSpriteIfNoFile()
    {
        
    }
    
    [Test]
    public void ShouldNotIncludeLutIfMissing()
    {
        var fileModifier = new MockFileModifier(new ());

        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "skirt"
            ui_sub_category = "skirt"
            default_unlocked = true

            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);
        
        Assert.That(fileModifier.Exists("assets/animations/Player/Skirts/spr_player_lryn_celine_summer_skirt_lut.meta.toml"), Is.False);
        Assert.That(fileModifier.Exists("assets/shapes/Player/Skirts/poly_player_lryn_celine_summer_skirt_lut.meta.toml"), Is.False);
    }

    [Test]
    public void ShouldAllowPassingInDefaultUnlocked()
    {
        var fileModifier = new MockFileModifier(new ());
        
        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "bottom"
            ui_sub_category = "skirt"
            default_unlocked = true

            lut = "images/lut.png"
            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);
        
        Assert.That(
            fileModifier.GetFile("assets/fiddle/player_assets.toml"),
            new ContainsTomlConstraint(new TomlTable
            {
                {
                    "lryn_celine_summer_skirt", new TomlTable
                    {
                        { "default_unlocked", true }
                    }
                }
            })
        );
    }

    [Test]
    public void ShouldNotWriteMissingDefaultUnlocked()
    {
        var fileModifier = new MockFileModifier(new ());
        
        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "bottom"
            ui_sub_category = "skirt"

            lut = "images/lut.png"
            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);

        var toml = TomlSerializer.Deserialize<TomlTable>(fileModifier.GetFile("assets/fiddle/player_assets.toml"))!;
        var asset = toml["lryn_celine_summer_skirt"] as TomlTable;
        
        Assert.That(asset?.ContainsKey("default_unlocked"), Is.False);
    }
    
    [Test]
    public void ShouldAllowPassingInPriceOverride()
    {
        var fileModifier = new MockFileModifier(new ());
        
        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "bottom"
            ui_sub_category = "skirt"
            default_unlocked = true
            price_override = 10

            lut = "images/lut.png"
            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);
        
        Assert.That(
            fileModifier.GetFile("assets/fiddle/player_assets.toml"),
            new ContainsTomlConstraint(new TomlTable
            {
                {
                    "lryn_celine_summer_skirt", new TomlTable
                    {
                        { "price_override", (long) 10 }
                    }
                }
            })
        );
    }

    [Test]
    public void ShouldNotWriteMissingPriceOverride()
    {
        var fileModifier = new MockFileModifier(new ());
        
        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml", 
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "bottom"
            ui_sub_category = "skirt"

            lut = "images/lut.png"
            ui_sprites = {
                ui = "images/ui.png",
                outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);

        var toml = TomlSerializer.Deserialize<TomlTable>(fileModifier.GetFile("assets/fiddle/player_assets.toml"))!;
        var asset = toml["lryn_celine_summer_skirt"] as TomlTable;
        
        Assert.That(asset?.ContainsKey("price_override"), Is.False);
    }

    [Test]
    public void ShouldHandleComplexIcons()
    {
        var fileModifier = new MockFileModifier(new ());
        var mod = GetSimpleMod();
        mod.SetFile(
            "momi/cosmetics/lryn_celine_cosmetic.toml",
            """
            [lryn_celine_summer_skirt]
            name = "Celine's summer skirt"
            ui_slot = "bottom"
            ui_sub_category = "skirt"

            lut = "images/lut.png"
            ui_sprites = {
                asset = "images/ui.png",
                body = "images/ui.png",
                merged = "images/ui.png",
                merged_outline = "images/outline.png"
            }
            
            cosmetic_sprites = { waist = "images/skirt.png" }
            """
        );
        
        new MockInstaller().InstallMod(mod, fileModifier);
        
        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"), Is.False);
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt.meta.toml"), Is.False);
        
        Assert.That(fileModifier.Exists("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_lryn_celine_summer_skirt_outline.meta.toml"), Is.False);
        Assert.That(fileModifier.Exists("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_lryn_celine_summer_skirt_outline.meta.toml"), Is.False);
        
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt_asset.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.meta.toml"))
        );
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt_asset.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.poly.toml"))
        );
        
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt_body.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.meta.toml"))
        );
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt_body.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.poly.toml"))
        );
        
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt_merged.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.meta.toml"))
        );
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt_merged.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.poly.toml"))
        );
        
        Assert.That(
            fileModifier.GetFile("assets/animations/mock.mod/lryn_celine_summer_skirt/spr_ui_item_wearable_lryn_celine_summer_skirt_merged_outline.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.meta.toml"))
        );
        Assert.That(
            fileModifier.GetFile("assets/shapes/mock.mod/lryn_celine_summer_skirt/poly_ui_item_wearable_lryn_celine_summer_skirt_merged_outline.meta.toml"),
            new ContainsTomlConstraint(FixtureHandler.ReadAllText("OutfitMod/cosmetic_output/ui.poly.toml"))
        );
        
        // Check that it was inserted in outlines.json
        Assert.That(fileModifier.Exists("assets/data_files/animation/outlines.json"), Is.True);
        Assert.That(
            fileModifier.GetFile("assets/data_files/animation/outlines.json"),
            new ContainsJsonConstraint(new JObject
            {
                { "spr_ui_item_wearable_lryn_celine_summer_skirt_merged", "spr_ui_item_wearable_lryn_celine_summer_skirt_merged_outline" }
            })
        );
    }

    [Test]
    public void ShouldKeepIndependentHairFaceGearAndLayeredTopAssets()
    {
        var fileModifier = new MockFileModifier(new());
        var mod = new MockMod(new Dictionary<string, object>
        {
            ["momi/cosmetics/test_cosmetics.toml"] = """
                [example_hair]
                name = "Example Hair"
                ui_slot = "hair"
                ui_sub_category = "long_hair"
                lut = "images/lut.png"
                ui_sprites = { asset = "images/ui.png", body = "images/ui.png", merged = "images/ui.png", merged_outline = "images/outline.png" }
                cosmetic_sprites = { hair_back = "images/part.png", hair_mid = "images/part.png" }

                [example_glasses]
                name = "Example Glasses"
                ui_slot = "face_gear"
                ui_sub_category = "glasses"
                lut = "images/lut.png"
                ui_sprites = { asset = "images/ui.png", body = "images/ui.png", merged = "images/ui.png", merged_outline = "images/outline.png" }
                cosmetic_sprites = { face_gear = "images/part.png" }

                [example_jacket]
                name = "Example Jacket"
                ui_slot = "top"
                ui_sub_category = "long_sleeve"
                lut = "images/lut.png"
                ui_sprites = { ui = "images/ui.png", outline = "images/outline.png" }
                cosmetic_sprites = { torso = "images/part.png", sleeve_left = "images/part.png", sleeve_right = "images/part.png" }
                """,
            ["images/part.png"] = FixtureHandler.ReadAllBytes("OutfitMod/skirt.png"),
            ["images/lut.png"] = FixtureHandler.ReadAllBytes("OutfitMod/lut.png"),
            ["images/ui.png"] = FixtureHandler.ReadAllBytes("OutfitMod/ui.png"),
            ["images/outline.png"] = FixtureHandler.ReadAllBytes("OutfitMod/outline.png"),
        });

        new MockInstaller().InstallMod(mod, fileModifier);

        Assert.That(fileModifier.GetFile("assets/data_files/animation/player_asset_parts.json"),
            new ContainsJsonConstraint(new JObject
            {
                ["example_hair"] = new JObject
                {
                    ["hair_back"] = "spr_example_hair_hair_back",
                    ["hair_mid"] = "spr_example_hair_hair_mid",
                },
                ["example_glasses"] = new JObject
                {
                    ["face_gear"] = "spr_example_glasses_face_gear",
                },
                ["example_jacket"] = new JObject
                {
                    ["torso"] = "spr_example_jacket_torso",
                    ["sleeve_left"] = "spr_example_jacket_sleeve_left",
                    ["sleeve_right"] = "spr_example_jacket_sleeve_right",
                },
            }));

        Assert.That(fileModifier.GetFile("assets/fiddle/player_assets.toml"),
            new ContainsTomlConstraint(new TomlTable
            {
                ["example_hair"] = new TomlTable { ["lut"] = "spr_example_hair_lut", ["ui_slot"] = "hair" },
                ["example_glasses"] = new TomlTable { ["lut"] = "spr_example_glasses_lut", ["ui_slot"] = "face_gear" },
                ["example_jacket"] = new TomlTable { ["lut"] = "spr_example_jacket_lut", ["ui_slot"] = "top" },
            }));
    }
}
