using Garethp.ModsOfMistriaInstallerLib.Bindings;

namespace ModsOfMistriaInstallerLibTests.Bindings;

// The names MMAPI accepts, transcribed from its API reference. AIM must never write a name the
// game will reject: the mod silently falls back to its default, which looks like AIM losing the
// edit rather than refusing it.
[TestFixture]
public class MmapiBindingTest
{
    [Test]
    public void ShouldAcceptEveryDocumentedKeyboardName()
    {
        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "F1", "F12", "0", "9", "A", "Z", "INSERT", "DELETE", "HOME", "PAGE_UP", "PAGE_DOWN", "SHIFT", "CONTROL" })
                Assert.That(MmapiBindingVocabulary.TryParse(name), Is.Not.Null, name);
        });
    }

    [Test]
    public void ShouldAcceptEveryDocumentedGamepadName()
    {
        Assert.That(MmapiBindingVocabulary.GamepadNames, Has.Exactly(16).Items);
        Assert.Multiple(() =>
        {
            foreach (var name in MmapiBindingVocabulary.GamepadNames)
                Assert.That(MmapiBindingVocabulary.TryParse(name)?.Device,
                    Is.EqualTo(BindingDevice.Gamepad), name);
        });
    }

    // The docs are explicit that the maps are case-sensitive, and that ALT, the lock keys and the
    // numpad are unsupported.
    [Test]
    public void ShouldRejectWhatTheGameRejects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MmapiBindingVocabulary.TryParse("f7"), Is.Null, "lowercase");
            Assert.That(MmapiBindingVocabulary.TryParse("ALT"), Is.Null);
            Assert.That(MmapiBindingVocabulary.TryParse("NUMPAD_1"), Is.Null);
            Assert.That(MmapiBindingVocabulary.TryParse("CAPS_LOCK"), Is.Null);
            Assert.That(MmapiBindingVocabulary.TryParse("F13"), Is.Null);
            Assert.That(MmapiBindingVocabulary.TryParse(""), Is.Null);
            Assert.That(MmapiBindingVocabulary.TryParse("SHIFT+"), Is.Null, "empty token");
        });
    }

    // Mods themselves are inconsistent - one ships "f7" as its default - so a user copying what
    // they see should be corrected rather than refused.
    [Test]
    public void ShouldNormaliseCasingButNotInventNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MmapiBindingVocabulary.Normalize("f7"), Is.EqualTo("F7"));
            Assert.That(MmapiBindingVocabulary.Normalize("shift+f5"), Is.EqualTo("SHIFT+F5"));
            Assert.That(MmapiBindingVocabulary.Normalize("gamepad_a"), Is.EqualTo("GAMEPAD_A"));
            Assert.That(MmapiBindingVocabulary.Normalize("alt"), Is.Null);
        });
    }

    [Test]
    public void ShouldTreatTheLastPartAsTheTrigger()
    {
        var binding = MmapiBindingVocabulary.TryParse("SHIFT+F5");

        Assert.Multiple(() =>
        {
            Assert.That(binding!.Trigger, Is.EqualTo("F5"));
            Assert.That(binding.Modifiers, Is.EqualTo(new[] { "SHIFT" }));
            Assert.That(binding.IsChord, Is.True);
            Assert.That(binding.ToString(), Is.EqualTo("SHIFT+F5"));
        });
    }

    // "Keyboard and gamepad are separate namespaces, so F1 and GAMEPAD_A never conflict."
    [Test]
    public void ShouldNotConflictAcrossDevices()
    {
        var key = MmapiBindingVocabulary.TryParse("A")!;
        var pad = MmapiBindingVocabulary.TryParse("GAMEPAD_A")!;

        Assert.That(key.OverlapWith(pad), Is.EqualTo(BindingOverlap.None));
    }

    [Test]
    public void ShouldReportIdenticalBindingsAsAClash()
    {
        var left = MmapiBindingVocabulary.TryParse("SHIFT+F5")!;
        var right = MmapiBindingVocabulary.TryParse("SHIFT+F5")!;

        Assert.That(left.OverlapWith(right), Is.EqualTo(BindingOverlap.SameBinding));
    }

    // A chord consumes its trigger, so the bare binding goes quiet whenever the modifier is held.
    // Not a deadlock, but not nothing either.
    [Test]
    public void ShouldReportAChordOverABareBindingAsASharedTrigger()
    {
        var chord = MmapiBindingVocabulary.TryParse("SHIFT+F5")!;
        var bare = MmapiBindingVocabulary.TryParse("F5")!;

        Assert.Multiple(() =>
        {
            Assert.That(chord.OverlapWith(bare), Is.EqualTo(BindingOverlap.SharedTrigger));
            Assert.That(bare.OverlapWith(chord), Is.EqualTo(BindingOverlap.SharedTrigger));
        });
    }

    // A chord may mix families; what decides the conflict is the part that actually fires.
    [Test]
    public void ShouldTakeTheDeviceFromTheTrigger()
    {
        Assert.That(MmapiBindingVocabulary.TryParse("CONTROL+GAMEPAD_Y")!.Device,
            Is.EqualTo(BindingDevice.Gamepad));
    }
}
