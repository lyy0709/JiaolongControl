# Bundled official installer

PawnIO_setup.exe is the **unmodified official signed installer 2.2.0**, not a rebuilt/unsigned driver.

- Source: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`
- Authenticode verified `Valid`, signer `namazso.eu`, when included.
- Official module integration/redistribution guidance: https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules
- The installer is proprietary; official guidance permits redistributing the installer. This repository's license does not relicense it. Existing module licenses remain in PawnIO.LICENSE.txt.

The application embeds the installer and signed modules as resources. Installation is explicit, interactive and administrator-approved. Merely opening a page never installs a driver. No security/signature bypass is provided. Temporary installer copies use `JiaoLongControl-PawnIO-*` under the user's temp folder and may be removed after the installer exits.
