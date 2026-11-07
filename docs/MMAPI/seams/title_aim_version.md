# Engine Fix: title_aim_version

`title_aim_version` is an **engine fix**, an anchored edit with no hook behind it. It shows the exact AIM release that built the current archive in the title screen's lower-left version stack. See [Seams](../SEAMS.md).

The installer stages a generated `mmapi_install_stamp.gml` beside the MMAPI framework. It writes the public AIM SemVer to `global.__aim_installer_version`. The title fix reads the value through the struct accessor, so archives made by older AIM releases simply omit the line.

The text is diagnostic only: it does not identify the current executable, installed mods, or the game's version. It identifies the AIM build which last rebuilt `assets.zip`, which is the useful fact in a screenshot or support report.

At a normal title screen the game version sits on the bottom row and AIM uses the row immediately above it. The fix accounts for the engine's optional debug and assertion labels, keeping the AIM row above them rather than overlapping them.
