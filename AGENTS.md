# H.O.W.L. development and testing policy

**Effective 2026-10-15. This is a project-wide rule.**

## Nightly is the only human testing channel

1. **All experimental H.O.W.L. loader, launcher, mod, GUI, content-pack and integration builds intended for player testing MUST be delivered through the existing CodaLauncher Nightly channel. No exceptions.** There must be no separate test launcher, standalone test ZIP, manually copied JAR, manual mod folder installation, or user-required download.
2. Nightly Play / Install-Repair automatically discovers, downloads, verifies, installs, updates and enables all approved first-party Nightly test dependencies in the **Nightly profile**. Testing a feature must not require the player to enter GitHub Actions or Releases or press Install in the mod shelf.
3. **Stable never auto-installs experiments.** Nightly must remain isolated from Stable accounts/settings as appropriate, worlds, resource packs and mod directory. Do not downgrade or overwrite user-owned/modified files. For world-changing tests, use an isolated new world and provide a backup/rollback path.
4. Before publication, CI must build and test the complete compatible loader/mod combination, verify Minecraft version and API compatibility, validate exact JAR identities and checksums, and reject duplicate/conflicting mods. Publish immutable, versioned GitHub Releases used by the existing Nightly updater. Do not treat an ephemeral Actions artifact as a launcher distribution channel.
5. Failure of CI, compatibility, checksum, authentication or release discovery **blocks that Nightly update** and preserves the last verified install. It does not trigger a manual fallback or a silent Stable downgrade. Live in-game validation is carried out **in Nightly**, not used as a reason to invent a separate test distribution.
6. All H.O.W.L. development instructions, agent tasks, PR templates and implementation plans must follow these rules. Where existing documentation suggests a manual test ZIP or manually installing a JAR, this policy supersedes it.

## Release path

**Code / PR -> CI compatibility checks -> versioned Nightly loader and mod releases -> automatic CodaLauncher Nightly installation -> real gameplay testing -> approval/promotion to Stable, if appropriate.**

Publishing requires implementation and working release automation, not only a documented intention. An unreleased feature must be called "not yet in Nightly" until the launcher can actually fetch and install it.

## Mandatory version bump and delivery proof

Every player-facing Nightly change **must bump the version of the component that changed** and publish a new, immutable release before it is called delivered. A successful PR, source commit or CI artifact is **not** a Nightly update.

- **Launcher changes:** increment the CodaLauncher executable version in Windows and desktop project files, workflows, Windows installer, and macOS Info.plist. All platform versions must match; the new GitHub Release must exist and the in-app updater must offer it. Existing release tags are immutable, so unchanged version numbers silently skip publishing.
- **Mod changes:** increment `coda.mod.json`, output JAR filename and matching GitHub Release tag, publish the checksum, and exercise an automated upgrade from the previously installed mod to the newly published one. The H.O.W.L. loader may retain its own version when only a mod changes.
- **Loader API/runtime changes:** increment `CodaTarget.LOADER_VERSION`, publish the compatible runtime and verify its required classes.
- Show mod versions where practical in the in-game UI or startup logs, and distinguish loader, launcher and mod versions in testing instructions.
- Never claim the player has the change until version selection and verified automatic Nightly installation have been demonstrated. No manual-install workaround is permitted.
