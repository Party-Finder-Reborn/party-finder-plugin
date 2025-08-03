# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- **Custom Duties & IDutyInfo Abstraction**: Introduced a new `IDutyInfo` interface to enable support for custom duty types alongside traditional FFXIV duties. This abstraction allows the plugin to handle both real duties (via `RealDutyInfo`) and user-defined duties (via `CustomDutyInfo`) through a unified interface. Three custom duties have been added: Hunt (ID: 9999), FATE (ID: 9998), and Role Playing (ID: 9997). This extensible system enables 3rd-party contributors to easily add new duty types without modifying core game data structures, while maintaining full compatibility with existing party finder functionality including search, filtering, and display formatting.
