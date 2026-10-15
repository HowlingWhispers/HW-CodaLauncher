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
