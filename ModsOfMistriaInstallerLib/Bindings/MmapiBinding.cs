namespace Garethp.ModsOfMistriaInstallerLib.Bindings;

/// <summary>Which family of input a binding name belongs to.</summary>
public enum BindingDevice
{
    Keyboard,
    Gamepad
}

/// <summary>
/// One binding as MMAPI understands it: an ordered chord whose <em>last</em> part is the trigger,
/// with every earlier part required to be held at that moment.
///
/// Names are case-sensitive, exactly as <c>mmapi_hotkey_vk_from_name</c> and
/// <c>mmapi_hotkey_pad_from_name</c> are. Writing "f7" where the game wants "F7" produces a mod
/// that silently falls back to its default, which is a far more confusing outcome than a rejected
/// edit - so AIM refuses to write a name the game would not accept.
/// </summary>
public sealed record MmapiBinding(IReadOnlyList<string> Parts)
{
    public string Trigger => Parts[^1];

    // Take rather than a range: IReadOnlyList has an int indexer, which is all an Index needs, but
    // no Slice, which is what a Range would need.
    public IReadOnlyList<string> Modifiers => [.. Parts.Take(Parts.Count - 1)];

    public bool IsChord => Parts.Count > 1;

    /// <summary>
    /// The device the trigger belongs to. A chord may mix families - "CONTROL+GAMEPAD_Y" is legal -
    /// so what decides conflicts is the trigger, which is the part that actually fires.
    /// </summary>
    public BindingDevice Device => MmapiBindingVocabulary.IsGamepad(Trigger)
        ? BindingDevice.Gamepad
        : BindingDevice.Keyboard;

    public override string ToString() => string.Join("+", Parts);

    // A record's generated equality would compare Parts by reference, so two bindings parsed from
    // the same text would come out unequal. Nothing relies on that today, which is exactly why it
    // is worth fixing now rather than after something quietly does.
    public bool Equals(MmapiBinding? other) =>
        other is not null && Parts.SequenceEqual(other.Parts, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in Parts) hash.Add(part, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Whether two bindings would fight over the same input.
    ///
    /// The rules are MMAPI's, not invented here. Keyboard and gamepad are separate namespaces, so
    /// F1 and GAMEPAD_A never conflict. Identical bindings both fire, which is the real clash. A
    /// chord and a bare binding on the same trigger do not both fire - the chord consumes it - but
    /// MMAPI still logs an overlap warning naming both mods, because the bare binding goes quiet in
    /// a way its author did not choose.
    /// </summary>
    public BindingOverlap OverlapWith(MmapiBinding other)
    {
        if (Device != other.Device) return BindingOverlap.None;
        if (!Trigger.Equals(other.Trigger, StringComparison.Ordinal)) return BindingOverlap.None;

        return Parts.SequenceEqual(other.Parts, StringComparer.Ordinal)
            ? BindingOverlap.SameBinding
            : BindingOverlap.SharedTrigger;
    }
}

public enum BindingOverlap
{
    None,

    /// <summary>Both mods fire. This is the clash a user needs to fix.</summary>
    SameBinding,

    /// <summary>
    /// One is a chord over the other's trigger. Only the chord fires, so the bare binding is
    /// suppressed whenever the modifier happens to be held - worth surfacing, but not a deadlock.
    /// </summary>
    SharedTrigger
}

/// <summary>
/// The exact set of names MMAPI accepts, transcribed from the API reference.
///
/// It is a closed vocabulary on purpose: MMAPI documents that ALT, PAUSE_BREAK, CAPS_LOCK,
/// NUM_LOCK, SCROLL_LOCK and NUMPAD_0-9 are <em>not</em> supported, and that a mod configured with
/// them falls back to its default binding. AIM therefore offers only what will actually work.
/// </summary>
public static class MmapiBindingVocabulary
{
    public static readonly IReadOnlyList<string> KeyboardNames =
    [
        .. Enumerable.Range(1, 12).Select(number => $"F{number}"),
        .. Enumerable.Range(0, 10).Select(digit => digit.ToString()),
        .. Enumerable.Range(0, 26).Select(offset => ((char)('A' + offset)).ToString()),
        "INSERT", "DELETE", "HOME", "PAGE_UP", "PAGE_DOWN", "SHIFT", "CONTROL"
    ];

    public static readonly IReadOnlyList<string> GamepadNames =
    [
        "GAMEPAD_A", "GAMEPAD_B", "GAMEPAD_X", "GAMEPAD_Y",
        "GAMEPAD_LEFT_SHOULDER", "GAMEPAD_RIGHT_SHOULDER",
        "GAMEPAD_LEFT_TRIGGER", "GAMEPAD_RIGHT_TRIGGER",
        "GAMEPAD_DPAD_UP", "GAMEPAD_DPAD_DOWN", "GAMEPAD_DPAD_LEFT", "GAMEPAD_DPAD_RIGHT",
        "GAMEPAD_LEFT_STICK", "GAMEPAD_RIGHT_STICK",
        "GAMEPAD_SELECT", "GAMEPAD_START"
    ];

    /// <summary>Every name, keyboard first. The order the pickers offer them in.</summary>
    public static readonly IReadOnlyList<string> AllNames = [.. KeyboardNames, .. GamepadNames];

    private static readonly HashSet<string> Keyboard = new(KeyboardNames, StringComparer.Ordinal);
    private static readonly HashSet<string> Gamepad = new(GamepadNames, StringComparer.Ordinal);

    public static bool IsKeyboard(string name) => Keyboard.Contains(name);

    public static bool IsGamepad(string name) => Gamepad.Contains(name);

    public static bool IsKnown(string name) => IsKeyboard(name) || IsGamepad(name);

    /// <summary>
    /// Parses a binding name. Returns null for anything MMAPI would reject: an unknown token, an
    /// empty part, or a name whose case does not match.
    /// </summary>
    public static MmapiBinding? TryParse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var parts = name.Split('+');
        if (parts.Length == 0) return null;

        foreach (var part in parts)
            if (!IsKnown(part))
                return null;

        return new MmapiBinding(parts);
    }

    /// <summary>
    /// Corrects a name the user typed to the casing MMAPI expects, when that is the only thing
    /// wrong with it. Returns null when the name is not salvageable.
    ///
    /// Worth doing because mods themselves are inconsistent - one ships "f7" as its default - and a
    /// user copying what they see in a mod's own files would otherwise be told they are wrong.
    /// </summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var corrected = new List<string>();
        foreach (var part in name.Split('+'))
        {
            var match = AllNames.FirstOrDefault(known =>
                known.Equals(part.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            corrected.Add(match);
        }

        return string.Join("+", corrected);
    }
}
